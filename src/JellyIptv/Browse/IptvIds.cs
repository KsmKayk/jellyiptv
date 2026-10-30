using MediaBrowser.Controller.Library;

namespace JellyIptv.Browse;

/// <summary>Jellyfin ids of the IPTV channel and its items, computed exactly like ChannelManager (10.10.7).</summary>
public static class IptvIds
{
    /// <summary>ChannelManager.GetInternalChannelId: GetNewItemId("Channel " + name, typeof(Channel)).</summary>
    public const string ChannelKey = "Channel " + IptvChannel.ChannelName;

    /// <summary>ChannelManager.GetIdToHash: externalId + channelName + "16".</summary>
    public static string IdToHash(string externalId) => externalId + IptvChannel.ChannelName + "16";

    public static Guid ChannelId(ILibraryManager libraryManager) =>
        libraryManager.GetNewItemId(ChannelKey, typeof(MediaBrowser.Controller.Channels.Channel));

    /// <param name="type">Entity type ChannelManager creates: Movie for movies, Folder for container folders.</param>
    public static Guid ItemId(ILibraryManager libraryManager, string externalId, Type type) =>
        libraryManager.GetNewItemId(IdToHash(externalId), type);
}
