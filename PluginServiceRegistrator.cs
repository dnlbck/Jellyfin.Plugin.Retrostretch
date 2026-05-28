using System;
using System.Linq;
using MediaBrowser.Controller;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RetroStretch
{
    /// <summary>
    /// Hooks into Jellyfin's DI two ways:
    ///   - Replaces <see cref="ITranscodeManager"/> with a decorator that
    ///     rewrites the ffmpeg scale step on matching transcodes.
    ///   - Registers an MVC action filter that forces matching devices to
    ///     transcode (no direct play / direct stream) so the decorator
    ///     always gets a chance to run.
    /// We discover the original ITranscodeManager implementation type from the
    /// existing service descriptor rather than referencing the
    /// MediaBrowser.MediaEncoding assembly directly (it isn't part of the
    /// Jellyfin NuGet contract).
    /// </summary>
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc />
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            ReplaceTranscodeManager(serviceCollection);
            RegisterMvcFilter(serviceCollection);
        }

        private static void ReplaceTranscodeManager(IServiceCollection services)
        {
            var existing = services.FirstOrDefault(d => d.ServiceType == typeof(ITranscodeManager));
            if (existing == null || existing.ImplementationType == null)
            {
                return;
            }

            Type innerType = existing.ImplementationType;

            services.Replace(
                ServiceDescriptor.Singleton<ITranscodeManager>(sp =>
                {
                    var inner = (ITranscodeManager)ActivatorUtilities.CreateInstance(sp, innerType);
                    return new RetroTranscodeManager(
                        inner,
                        sp.GetRequiredService<ISessionManager>(),
                        sp.GetRequiredService<ILogger<RetroTranscodeManager>>());
                }));
        }

        private static void RegisterMvcFilter(IServiceCollection services)
        {
            services.TryAddSingleton<RetroPlaybackInfoFilter>();
            services.Configure<MvcOptions>(opts =>
            {
                // AddService registers a ServiceFilterAttribute, which pulls
                // the filter instance from the service provider — required so
                // our ctor gets ISessionManager / ILogger via DI.
                opts.Filters.AddService<RetroPlaybackInfoFilter>();
            });
        }
    }
}
