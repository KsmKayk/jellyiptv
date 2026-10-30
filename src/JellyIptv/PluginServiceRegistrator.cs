using JellyIptv.Browse;
using JellyIptv.Configuration;
using JellyIptv.Downloads;
using JellyIptv.Live;
using JellyIptv.Web;
using JellyIptv.Xtream;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace JellyIptv;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IConfigProvider, PluginConfigProvider>();
        serviceCollection.AddSingleton<TtlCache>();
        serviceCollection.AddSingleton<IXtreamApi, XtreamClient>();
        serviceCollection.AddSingleton<IChannel, IptvChannel>();
        serviceCollection.AddSingleton<ILiveTvService, IptvLiveTvService>();
        serviceCollection.AddSingleton<IptvItemResolver>();
        serviceCollection.AddSingleton<CatalogSearch>();
        serviceCollection.AddSingleton<IChannelBrowser, JellyfinChannelBrowser>();
        serviceCollection.AddSingleton<IptvItemMaterializer>();
        serviceCollection.AddSingleton<DownloadPlanner>();
        serviceCollection.AddSingleton<IFileDownloader, HttpFileDownloader>();
        serviceCollection.AddSingleton<ILibraryScanTrigger, JellyfinLibraryScanTrigger>();
        serviceCollection.AddSingleton(sp => new DownloadQueue(
            Path.Combine(sp.GetRequiredService<IServerApplicationPaths>().DataPath, "jellyiptv", "queue.json")));
        serviceCollection.AddSingleton<DownloadManager>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<DownloadManager>());
        serviceCollection.AddHostedService<PlaybackWatcher>();
        serviceCollection.AddHostedService<ScriptInjector>();
    }
}
