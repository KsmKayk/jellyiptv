using JellyIptv.Downloads;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace JellyIptv.Tests;

public class PlaybackWatcherTests
{
    private static Movie NewItem() => new() { Id = Guid.NewGuid() };

    [Fact]
    public void Stale_full_item_with_no_now_playing_is_not_streaming()
    {
        var stale = NewItem();
        Assert.False(PlaybackWatcher.IsStreaming([((Guid?)null, (BaseItem?)stale)], _ => true));
    }

    [Fact]
    public void Matching_iptv_item_is_streaming()
    {
        var item = NewItem();
        Assert.True(PlaybackWatcher.IsStreaming([((Guid?)item.Id, (BaseItem?)item)], _ => true));
    }

    [Fact]
    public void Full_item_not_matching_now_playing_is_not_streaming()
    {
        var staleIptv = NewItem();
        Assert.False(PlaybackWatcher.IsStreaming([((Guid?)Guid.NewGuid(), (BaseItem?)staleIptv)], _ => true));
    }

    [Fact]
    public void Non_iptv_playback_is_not_streaming()
    {
        var item = NewItem();
        Assert.False(PlaybackWatcher.IsStreaming([((Guid?)item.Id, (BaseItem?)item)], _ => false));
        Assert.False(PlaybackWatcher.IsStreaming([], _ => true));
    }
}
