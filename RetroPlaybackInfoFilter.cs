using System;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RetroStretch
{
    /// <summary>
    /// MVC action filter that forces transcoding (no direct play, no direct
    /// stream / remux) for sessions whose DeviceName or Client matches the
    /// plugin's pattern list. Without this, a Roku that can direct-play the
    /// source codec bypasses our ITranscodeManager decorator entirely and the
    /// stretch never gets applied.
    ///
    /// The filter targets <c>MediaInfoController.GetPostedPlaybackInfo</c> —
    /// the endpoint clients POST to during playback negotiation. It overrides
    /// the bound <c>enableDirectPlay</c> / <c>enableDirectStream</c> action
    /// arguments so the controller's <c>??= playbackInfoDto?.EnableDirectPlay
    /// ?? true</c> chain falls through to our forced value.
    /// </summary>
    public sealed class RetroPlaybackInfoFilter : IAsyncActionFilter
    {
        // Jellyfin's internal claim key for the DeviceId. Kept inline to avoid
        // dragging Jellyfin.Api as a reference (it isn't a NuGet contract).
        private const string DeviceIdClaim = "Jellyfin-DeviceId";

        // Action we care about. Match by name only; if Jellyfin renames it in
        // a future release we no-op rather than crash.
        private const string TargetAction = "GetPostedPlaybackInfo";

        private readonly ISessionManager _sessions;
        private readonly ILogger<RetroPlaybackInfoFilter> _logger;

        public RetroPlaybackInfoFilter(
            ISessionManager sessions,
            ILogger<RetroPlaybackInfoFilter> logger)
        {
            _sessions = sessions;
            _logger = logger;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            try
            {
                MaybeForceTranscode(context);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Retro Stretch: PlaybackInfo filter failed; passing through");
            }

            await next().ConfigureAwait(false);
        }

        private void MaybeForceTranscode(ActionExecutingContext context)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.Enabled || !config.ForceTranscodeForMatchedDevices)
            {
                return;
            }

            if (context.ActionDescriptor is not ControllerActionDescriptor cad
                || !string.Equals(cad.ActionName, TargetAction, StringComparison.Ordinal))
            {
                return;
            }

            string deviceId = context.HttpContext.User?.FindFirst(DeviceIdClaim)?.Value;
            if (string.IsNullOrEmpty(deviceId))
            {
                return;
            }

            var session = _sessions.Sessions
                .FirstOrDefault(s => string.Equals(s?.DeviceId, deviceId, StringComparison.Ordinal));
            if (session == null || !IsCrtDevice(session.DeviceName, session.Client, config))
            {
                return;
            }

            // Boxed (bool?)false beats the controller's `??= …` defaulting to
            // true; if the action's parameter type isn't bool? we'd no-op
            // silently (MVC won't unbox a mismatched type — preferable to a
            // crash in the request path).
            OverrideArg(context, "enableDirectPlay", false);
            OverrideArg(context, "enableDirectStream", false);

            _logger.LogInformation(
                "Retro Stretch: forced transcode for device '{Device}' (client='{Client}') on PlaybackInfo",
                session.DeviceName, session.Client);
        }

        private static void OverrideArg(ActionExecutingContext context, string name, bool value)
        {
            // Use indexer so we add the key if MVC bound it as missing/null.
            context.ActionArguments[name] = (bool?)value;
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
    }
}
