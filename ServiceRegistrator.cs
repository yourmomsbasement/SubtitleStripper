using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SubtitleStripper;

/// <summary>
/// Registers plugin services into Jellyfin's DI container.
/// Jellyfin discovers this class automatically via reflection.
/// </summary>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // IHostedService: starts on Jellyfin boot, subscribes to ItemAdded.
        serviceCollection.AddHostedService<ItemAddedConsumer>();
    }
}
