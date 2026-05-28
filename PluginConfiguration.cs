using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.RetroStretch
{
    /// <summary>
    /// Plugin configuration. Editable via the Jellyfin dashboard under
    /// Plugins → Retro Stretch.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Master toggle for the plugin.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Comma-separated case-insensitive substrings matched against each
        /// session's <c>DeviceName</c> and <c>Client</c>. Any session whose
        /// device or client contains one of these substrings receives the
        /// stretch treatment. Example: <c>Roku</c> matches both
        /// <c>Roku Streambar (9102X)</c> and <c>Jellyfin Roku</c>.
        /// </summary>
        public string DeviceIdentifiers { get; set; } = "Roku";

        /// <summary>
        /// Target output width for the stretched frame.
        /// </summary>
        public string TargetWidth { get; set; } = "1920";

        /// <summary>
        /// Target output height for the stretched frame.
        /// </summary>
        public string TargetHeight { get; set; } = "1080";

        /// <summary>
        /// When true, the plugin overrides Jellyfin's playback negotiation so
        /// matching devices always receive a transcoding URL — direct play and
        /// direct stream / remux are disabled for those devices. Required for
        /// "set and forget" operation: without this, a client that can direct
        /// play the source codec bypasses our filter rewrite entirely.
        /// </summary>
        public bool ForceTranscodeForMatchedDevices { get; set; } = true;
    }
}
