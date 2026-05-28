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
        /// Comma-separated list of substrings to match against the client device
        /// name (JELLYFIN_DEVICE_NAME / JELLYFIN_CLIENT environment variable).
        /// Only devices whose name contains one of these substrings will receive
        /// the stretch treatment.  Examples: "Roku", "Roku,CRT,FireTV"
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
        /// Sensitivity for the cropdetect black-bar analysis.
        /// Lower values are more sensitive (detect faint bars).
        /// Default 0.1 works well for clean pillarboxes.
        /// </summary>
        public double CropDetectLimit { get; set; } = 0.1;
    }
}
