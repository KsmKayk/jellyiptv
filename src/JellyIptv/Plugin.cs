using JellyIptv.Configuration;
using JellyIptv.Live;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;

namespace JellyIptv;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static readonly Guid PluginId = Guid.Parse("f16dea81-517f-4f93-9da7-cb0dd194a373");

    private readonly ITaskManager _taskManager;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ITaskManager taskManager)
        : base(applicationPaths, xmlSerializer)
    {
        _taskManager = taskManager;
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "JellyIPTV";

    public override Guid Id => PluginId;

    public override string Description => "Assista e baixe filmes, séries e TV ao vivo de uma conta Xtream Codes.";

    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        if (configuration is PluginConfiguration next)
        {
            PluginConfiguration.StampSourceChanges(Configuration, next, DateTime.UtcNow);
        }

        base.UpdateConfiguration(configuration);

        // Live channels only show up after a guide refresh (Jellyfin runs it once a day).
        GuideRefresher.Queue(_taskManager);
    }

    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.config.html",
        },
        new PluginPageInfo
        {
            Name = "JellyIptvDownloads",
            DisplayName = "Downloads IPTV",
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.downloads.html",
            EnableInMainMenu = true,
            MenuIcon = "download",
        },
    ];
}
