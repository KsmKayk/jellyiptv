using JellyIptv.Browse;
using JellyIptv.Downloads;
using JellyIptv.Live;
using JellyIptv.Web;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.LiveTv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace JellyIptv.Tests;

public class PluginTests
{
    [Fact]
    public void Pages_point_to_embedded_resources()
    {
        var names = typeof(Plugin).Assembly.GetManifestResourceNames();
        Assert.Contains("JellyIptv.Web.config.html", names);
        Assert.Contains("JellyIptv.Web.downloads.html", names);
        Assert.Contains("JellyIptv.Web.jellyiptv.js", names);
    }

    [Fact]
    public void Registers_services()
    {
        var services = new ServiceCollection();
        new PluginServiceRegistrator().RegisterServices(services, null!);

        Assert.Contains(services, d => d.ServiceType == typeof(IChannel) && d.ImplementationType == typeof(IptvChannel));
        Assert.Contains(services, d => d.ServiceType == typeof(ILiveTvService) && d.ImplementationType == typeof(IptvLiveTvService));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(PlaybackWatcher));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ScriptInjector));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationFactory is not null);
        Assert.Contains(services, d => d.ServiceType == typeof(DownloadManager));
        Assert.Contains(services, d => d.ServiceType == typeof(JellyIptv.Browse.CatalogSearch));
        Assert.Contains(services, d => d.ServiceType == typeof(JellyIptv.Browse.IptvItemMaterializer));
        Assert.Contains(services, d => d.ServiceType == typeof(JellyIptv.Browse.IChannelBrowser) && d.ImplementationType == typeof(JellyIptv.Browse.JellyfinChannelBrowser));
    }
}
