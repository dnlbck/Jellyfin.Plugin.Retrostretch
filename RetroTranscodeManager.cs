using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.Streaming;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RetroStretch
{
    /// <summary>
    /// In-process decorator for <see cref="ITranscodeManager"/>. Intercepts
    /// <see cref="StartFfMpeg"/> calls right before ffmpeg launches and rewrites
    /// the scale filter so that 4:3 sources are pre-stretched to the configured
    /// 16:9 target dimensions. Falls back to a pass-through whenever the
    /// requesting session doesn't match the configured device patterns, the
    /// source isn't 4:3, or no recognisable scale step is in the filter chain.
    /// </summary>
    public sealed class RetroTranscodeManager : ITranscodeManager, IDisposable
    {
        private readonly ITranscodeManager _inner;
        private readonly ISessionManager _sessionManager;
        private readonly ILogger<RetroTranscodeManager> _logger;

        // Hardware scale filter names Jellyfin emits. Order matters: longer
        // tokens come first so a regex alternation doesn't match prefixes.
        private static readonly string[] HwScaleNames =
        {
            "scale_vaapi",
            "scale_qsv",
            "vpp_qsv",
            "scale_cuda",
            "scale_opencl",
            "scale_vt",
            "scale_npp",
            "scale_rkrga"
        };

        // Matches "scale_xxx=..." up to the next comma, quote, whitespace, or
        // filter-graph delimiter (; [ ]).
        // Group 1 = filter name, Group 2 = body (after the '=').
        private static readonly Regex HwScaleRegex = new Regex(
            @"(?<![A-Za-z_])(scale_vaapi|scale_qsv|vpp_qsv|scale_cuda|scale_opencl|scale_vt|scale_npp|scale_rkrga)=([^,""\s;\[\]]*)",
            RegexOptions.Compiled);

        // Matches a SW "scale=..." step. Excludes the same graph delimiters so
        // the body can't accidentally absorb the rest of a filter_complex.
        private static readonly Regex SwScaleRegex = new Regex(
            @"(?<![A-Za-z_])scale=([^,""\s;\[\]]+)",
            RegexOptions.Compiled);

        // Matches an existing "-aspect <ratio>" flag we may need to overwrite.
        private static readonly Regex AspectRegex = new Regex(
            @"-aspect\s+\S+",
            RegexOptions.Compiled);

        public RetroTranscodeManager(
            ITranscodeManager inner,
            ISessionManager sessionManager,
            ILogger<RetroTranscodeManager> logger)
        {
            _inner = inner;
            _sessionManager = sessionManager;
            _logger = logger;
        }

        public Task<TranscodingJob> StartFfMpeg(
            StreamState state,
            string outputPath,
            string commandLineArguments,
            Guid userId,
            TranscodingJobType transcodingJobType,
            CancellationTokenSource cancellationTokenSource,
            string workingDirectory = null)
        {
            string rewritten = commandLineArguments;
            try
            {
                rewritten = MaybeRewrite(state, commandLineArguments) ?? commandLineArguments;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Retro Stretch: rewrite failed; passing through");
                rewritten = commandLineArguments;
            }

            return _inner.StartFfMpeg(
                state, outputPath, rewritten, userId, transcodingJobType, cancellationTokenSource, workingDirectory);
        }

        private string MaybeRewrite(StreamState state, string args)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.Enabled || state == null || !state.IsOutputVideo)
            {
                return null;
            }

            string deviceId = state.Request?.DeviceId;
            if (string.IsNullOrEmpty(deviceId))
            {
                return null;
            }

            // Resolve the live session so we can substring-match DeviceName/Client.
            var session = _sessionManager.Sessions
                .FirstOrDefault(s => string.Equals(s?.DeviceId, deviceId, StringComparison.Ordinal));
            if (session == null)
            {
                _logger.LogDebug("Retro Stretch: no session for DeviceId {DeviceId}; pass-through", deviceId);
                return null;
            }

            if (!IsCrtDevice(session.DeviceName, session.Client, config))
            {
                return null;
            }

            // Resolve source DAR: prefer the AspectRatio metadata ("4:3" /
            // "16:9") because anamorphic NTSC DVD rips are 720x480 (pixel
            // ratio 1.5) but display as 4:3. Fall back to pixel dimensions
            // only when no DAR is recorded.
            double? srcAspect = TryParseAspect(state.VideoStream?.AspectRatio);
            if (srcAspect == null)
            {
                int srcW = state.VideoStream?.Width ?? 0;
                int srcH = state.VideoStream?.Height ?? 0;
                if (srcW > 0 && srcH > 0)
                {
                    srcAspect = (double)srcW / srcH;
                }
            }

            if (srcAspect == null)
            {
                _logger.LogDebug("Retro Stretch: no aspect info for {Path}; pass-through", state.MediaPath);
                return null;
            }

            if (!(srcAspect > 1.30 && srcAspect < 1.36))
            {
                // Not a 4:3 source. Pillarboxed 4:3 inside a 16:9 container is
                // not auto-detected — that would need cropdetect, which is too
                // costly to run synchronously on every transcode start.
                return null;
            }

            if (!int.TryParse(config.TargetWidth, out int targetW) || targetW <= 0)
            {
                targetW = 1920;
            }
            if (!int.TryParse(config.TargetHeight, out int targetH) || targetH <= 0)
            {
                targetH = 1080;
            }

            // Filter-graph (subtitle burn-in, overlays, etc.) is too risky to
            // rewrite blindly — bail out and let it play unstretched.
            if (args.IndexOf("-filter_complex", StringComparison.Ordinal) >= 0)
            {
                _logger.LogInformation(
                    "Retro Stretch: -filter_complex chain detected for '{Device}'; "
                    + "skipping stretch to preserve subtitle / overlay graph",
                    session.DeviceName);
                return null;
            }

            string rewritten = TryRewriteScale(args, targetW, targetH, out string filterKind);
            if (rewritten == null)
            {
                _logger.LogInformation(
                    "Retro Stretch: no recognised scale step in command line; cannot stretch "
                    + "for device '{Device}'. The transcode will play as-is (4:3 letterboxed).",
                    session.DeviceName);
                return null;
            }

            // Anamorphic sources (and ffmpeg's HW scale path generally) preserve
            // the source's SAR through to the output container, so a player will
            // still see a 4:3 DAR even after we stretched the pixels. Pin the
            // output DAR to the target aspect so the Roku displays full-frame.
            rewritten = EnsureOutputAspect(rewritten, targetW, targetH);

            _logger.LogInformation(
                "Retro Stretch: stretched 4:3 → {W}x{H} via {Kind} filter (-aspect set) for '{Device}' ({Path})",
                targetW, targetH, filterKind, session.DeviceName, state.MediaPath);
            return rewritten;
        }

        /// <summary>
        /// Ensure the ffmpeg args carry an <c>-aspect &lt;W&gt;:&lt;H&gt;</c>
        /// output option. If one is already present, it's overwritten; otherwise
        /// we insert it immediately after the rewritten <c>-vf "..."</c> block,
        /// which is always inside the output-option region of the command line.
        /// </summary>
        private static string EnsureOutputAspect(string args, int targetW, int targetH)
        {
            string flag = $"-aspect {targetW}:{targetH}";

            var existing = AspectRegex.Match(args);
            if (existing.Success)
            {
                return args.Substring(0, existing.Index) + flag + args.Substring(existing.Index + existing.Length);
            }

            // Insert right after the closing quote of -vf "..." (which we just
            // rewrote, so it's reliably present).
            int vfIdx = args.IndexOf("-vf \"", StringComparison.Ordinal);
            if (vfIdx < 0)
            {
                // Fall back to appending; ffmpeg accepts -aspect anywhere among
                // output options.
                return args + " " + flag;
            }

            int endQuote = args.IndexOf('"', vfIdx + 5);
            if (endQuote < 0)
            {
                return args + " " + flag;
            }

            return args.Substring(0, endQuote + 1) + " " + flag + args.Substring(endQuote + 1);
        }

        /// <summary>
        /// Replace the scale step's width and height in <paramref name="args"/>.
        /// HW chains have their <c>w=NNN:h=NNN</c> swapped (no aspect option —
        /// hardware scalers stretch by default). SW chains get the whole
        /// <c>scale=...</c> body replaced with a forced-aspect equivalent.
        /// Returns null if no matching step is found.
        /// </summary>
        private static string TryRewriteScale(string args, int targetW, int targetH, out string kind)
        {
            kind = null;

            // Try HW scaler first — these chains usually keep frames on the GPU
            // through to the encoder, so a SW <c>scale=</c> insertion would
            // force a costly hwdownload.
            var hwMatch = HwScaleRegex.Match(args);
            if (hwMatch.Success)
            {
                kind = hwMatch.Groups[1].Value;
                string body = hwMatch.Groups[2].Value;
                string rewrittenBody = RewriteHwBody(body, targetW, targetH);
                string newStep = hwMatch.Groups[1].Value + "=" + rewrittenBody;
                return args.Substring(0, hwMatch.Index) + newStep + args.Substring(hwMatch.Index + hwMatch.Length);
            }

            var swMatch = SwScaleRegex.Match(args);
            if (swMatch.Success)
            {
                kind = "scale";
                string newStep = $"scale={targetW}:{targetH}:force_original_aspect_ratio=disable";
                return args.Substring(0, swMatch.Index) + newStep + args.Substring(swMatch.Index + swMatch.Length);
            }

            return null;
        }

        private static string RewriteHwBody(string body, int targetW, int targetH)
        {
            // Jellyfin emits HW scaler bodies in one of these shapes:
            //   w=W:h=H[:format=fmt]    (size-fixed; may also include format)
            //   format=fmt              (format-only)
            //   (empty)                 (rare; just the filter name)
            // Surgery: ensure w= and h= are present with our target values; keep
            // any non-w/h key=value tokens untouched.
            if (string.IsNullOrEmpty(body))
            {
                return $"w={targetW}:h={targetH}";
            }

            bool hasW = Regex.IsMatch(body, @"(?<![A-Za-z_])w=\d+");
            bool hasH = Regex.IsMatch(body, @"(?<![A-Za-z_])h=\d+");

            if (hasW)
            {
                body = Regex.Replace(body, @"(?<![A-Za-z_])w=\d+", "w=" + targetW);
            }
            if (hasH)
            {
                body = Regex.Replace(body, @"(?<![A-Za-z_])h=\d+", "h=" + targetH);
            }
            if (!hasW || !hasH)
            {
                string wh = (hasW ? string.Empty : "w=" + targetW)
                    + ((!hasW && !hasH) ? ":" : string.Empty)
                    + (hasH ? string.Empty : "h=" + targetH);
                body = string.IsNullOrEmpty(body) ? wh : wh + ":" + body;
            }
            return body;
        }

        private static double? TryParseAspect(string aspect)
        {
            if (string.IsNullOrWhiteSpace(aspect))
            {
                return null;
            }

            int sep = aspect.IndexOf(':');
            if (sep > 0
                && double.TryParse(aspect.Substring(0, sep), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double num)
                && double.TryParse(aspect.Substring(sep + 1), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double den)
                && den != 0)
            {
                return num / den;
            }

            return double.TryParse(aspect, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double single)
                ? single
                : (double?)null;
        }

        private static bool IsCrtDevice(string deviceName, string client, PluginConfiguration config)
        {
            var identifiers = (config.DeviceIdentifiers ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s));

            deviceName ??= string.Empty;
            client ??= string.Empty;
            foreach (string id in identifiers)
            {
                if (deviceName.Contains(id, StringComparison.OrdinalIgnoreCase)
                    || client.Contains(id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // ── Plain forwarding for the rest of ITranscodeManager ─────────────

        public TranscodingJob GetTranscodingJob(string playSessionId)
            => _inner.GetTranscodingJob(playSessionId);

        public TranscodingJob GetTranscodingJob(string path, TranscodingJobType type)
            => _inner.GetTranscodingJob(path, type);

        public void PingTranscodingJob(string playSessionId, bool? isUserPaused)
            => _inner.PingTranscodingJob(playSessionId, isUserPaused);

        public Task KillTranscodingJobs(string deviceId, string playSessionId, Func<string, bool> deleteFiles)
            => _inner.KillTranscodingJobs(deviceId, playSessionId, deleteFiles);

        public void ReportTranscodingProgress(
            TranscodingJob job, StreamState state, TimeSpan? transcodingPosition, float? framerate,
            double? percentComplete, long? bytesTranscoded, int? bitRate)
            => _inner.ReportTranscodingProgress(job, state, transcodingPosition, framerate, percentComplete, bytesTranscoded, bitRate);

        public TranscodingJob OnTranscodeBeginRequest(string path, TranscodingJobType type)
            => _inner.OnTranscodeBeginRequest(path, type);

        public void OnTranscodeEndRequest(TranscodingJob job)
            => _inner.OnTranscodeEndRequest(job);

        public ValueTask<IDisposable> LockAsync(string outputPath, CancellationToken cancellationToken)
            => _inner.LockAsync(outputPath, cancellationToken);

        public void Dispose()
        {
            (_inner as IDisposable)?.Dispose();
        }
    }
}
