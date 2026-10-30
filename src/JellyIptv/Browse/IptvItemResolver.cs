using JellyIptv.Live;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;

namespace JellyIptv.Browse;

public sealed class IptvItemResolver(ILibraryManager libraryManager)
{
    private readonly Lazy<Guid> channelId = new(() => IptvIds.ChannelId(libraryManager));

    /// <summary>The ItemKey of a Jellyfin item that belongs to the IPTV channel, or null.</summary>
    public ItemKey? Resolve(Guid itemId)
    {
        var item = libraryManager.GetItemById(itemId);
        if (item is null || !IsFromIptvChannel(item))
        {
            return null;
        }

        return ItemKey.TryParse(item.ExternalId, out var key) ? key : null;
    }

    /// <summary>True when playing this item opens a connection to the Xtream server.</summary>
    public bool IsIptvPlayback(BaseItem? item) => item switch
    {
        null => false,
        LiveTvChannel channel => string.Equals(channel.ServiceName, IptvLiveTvService.ServiceName, StringComparison.OrdinalIgnoreCase),
        _ => IsFromIptvChannel(item),
    };

    private bool IsFromIptvChannel(BaseItem item) =>
        !item.ChannelId.Equals(Guid.Empty)
        && item.ChannelId.Equals(channelId.Value);
}
