using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.RetroStretch
{
    /// <summary>
    /// Jellyfin plugin that pre-stretches 4:3 content to 16:9 for HDMI-to-RCA
    /// converter setups connecting to CRT TVs. The converter squeezes the 16:9
    /// image back to 4:3 geometry on the CRT, eliminating black bars.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        // Stable GUID identifying this plugin. Do not change.
        private static readonly Guid PluginId = new Guid("b8e3f2c1-4d5a-4f3a-8d2e-7a9b1c3d5e8f");

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public static Plugin? Instance { get; private set; }

        /// <inheritdoc />
        public override string Name => "Retro Stretch";

        /// <inheritdoc />
        public override string Description =>
            "Pre-stretch 4:3 content to 16:9 for HDMI-to-RCA converter + CRT TV setups.";

        /// <inheritdoc />
        public override Guid Id => PluginId;

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
                }
            };
        }
    }
}
