using System;
using System.Linq;
using MediaBrowser.Controller;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RetroStretch
{
    /// <summary>
    /// Replaces Jellyfin's default <see cref="ITranscodeManager"/> registration
    /// with our decorator. We discover the original implementation type from
    /// the existing service descriptor rather than referencing the
    /// <c>MediaBrowser.MediaEncoding</c> assembly directly (which isn't part of
    /// the Jellyfin NuGet contract). That way the plugin keeps working if
    /// Jellyfin reshuffles internals.
    /// </summary>
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc />
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            var existing = serviceCollection.FirstOrDefault(d => d.ServiceType == typeof(ITranscodeManager));
            if (existing == null || existing.ImplementationType == null)
            {
                // Jellyfin doesn't have ITranscodeManager registered with a
                // discoverable implementation type. Nothing we can do.
                return;
            }

            Type innerType = existing.ImplementationType;

            serviceCollection.Replace(
                ServiceDescriptor.Singleton<ITranscodeManager>(sp =>
                {
                    var inner = (ITranscodeManager)ActivatorUtilities.CreateInstance(sp, innerType);
                    return new RetroTranscodeManager(
                        inner,
                        sp.GetRequiredService<ISessionManager>(),
                        sp.GetRequiredService<ILogger<RetroTranscodeManager>>());
                }));
        }
    }
}
