using System.Security.Cryptography;
using System.Text;
using Jellyfin.Data.Entities;
using JellyIptv.Browse;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace JellyIptv.Tests;

public class IptvItemMaterializerTests
{
    private readonly FakeChannelBrowser _browser = new();

    private IptvItemMaterializer Materializer => new(_browser, TimeSpan.FromMilliseconds(1));

    private Task<Guid?> Open(ItemKey key, string categoryId = "813") => Materializer.MaterializeAsync(key, categoryId, null, CancellationToken.None);

    private static readonly ItemKey Movie = new(ItemKind.Movie, Id: 203260);

    [Fact]
    public async Task Existing_item_is_returned_without_browsing()
    {
        var id = _browser.ItemId("movie:203260", typeof(Movie));
        _browser.Existing.Add(id);

        Assert.Equal(id, await Open(Movie));
        Assert.Empty(_browser.Browsed);
    }

    [Fact]
    public async Task Browses_the_folder_chain_from_the_channel_root()
    {
        var vod = _browser.ItemId("vod", typeof(Folder));
        var category = _browser.ItemId("vodcat:813", typeof(Folder));
        var movie = _browser.ItemId("movie:203260", typeof(Movie));
        _browser.Creates[Guid.Empty] = [vod];
        _browser.Creates[vod] = [category];
        _browser.Creates[category] = [movie];

        Assert.Equal(movie, await Open(Movie));
        Assert.Equal(new[] { Guid.Empty, vod, category }, _browser.Browsed);
    }

    [Fact]
    public async Task Starts_at_the_deepest_existing_folder()
    {
        var category = _browser.ItemId("vodcat:813", typeof(Folder));
        var movie = _browser.ItemId("movie:203260", typeof(Movie));
        _browser.Existing.Add(_browser.ItemId("vod", typeof(Folder)));
        _browser.Existing.Add(category);
        _browser.Creates[category] = [movie];

        Assert.Equal(movie, await Open(Movie));
        Assert.Equal(new[] { category }, _browser.Browsed);
    }

    [Fact]
    public async Task Series_are_container_folders_under_the_series_root()
    {
        var root = _browser.ItemId("series", typeof(Folder));
        var category = _browser.ItemId("sercat:691", typeof(Folder));
        var show = _browser.ItemId("show:873", typeof(Folder));
        _browser.Creates[Guid.Empty] = [root];
        _browser.Creates[root] = [category];
        _browser.Creates[category] = [show];

        Assert.Equal(show, await Open(new ItemKey(ItemKind.Series, Id: 873), "691"));
        Assert.Equal(new[] { Guid.Empty, root, category }, _browser.Browsed);
    }

    [Fact]
    public async Task Missing_after_browsing_is_null()
    {
        var vod = _browser.ItemId("vod", typeof(Folder));
        var category = _browser.ItemId("vodcat:813", typeof(Folder));
        _browser.Existing.Add(vod);
        _browser.Existing.Add(category);

        Assert.Null(await Open(Movie));
        Assert.Equal(new[] { category }, _browser.Browsed);
    }

    [Fact]
    public async Task Stops_when_a_folder_cannot_be_created()
    {
        // e.g. hidden category: the root browse doesn't list it.
        _browser.Existing.Add(_browser.ItemId("vod", typeof(Folder)));

        Assert.Null(await Open(Movie));
        Assert.Equal(new[] { _browser.ItemId("vod", typeof(Folder)) }, _browser.Browsed);
    }

    [Theory]
    [InlineData("season:873:1", "691")]
    [InlineData("allmovie:203260", "813")]
    [InlineData("vodcat:813", "813")]
    [InlineData("movie:203260", "")]
    public async Task Rejects_unsupported_keys(string key, string categoryId)
    {
        Assert.True(ItemKey.TryParse(key, out var parsed));
        await Assert.ThrowsAsync<ArgumentException>(() => Open(parsed, categoryId));
    }

    [Fact]
    public async Task Concurrent_opens_are_serialized_and_both_succeed()
    {
        var category = _browser.ItemId("vodcat:813", typeof(Folder));
        var movie = _browser.ItemId("movie:203260", typeof(Movie));
        var other = _browser.ItemId("movie:205119", typeof(Movie));
        _browser.Existing.Add(_browser.ItemId("vod", typeof(Folder)));
        _browser.Existing.Add(category);
        _browser.Creates[category] = [movie, other];
        _browser.BrowseDelay = TimeSpan.FromMilliseconds(50);
        var materializer = Materializer;

        var results = await Task.WhenAll(
            materializer.MaterializeAsync(Movie, "813", null, CancellationToken.None),
            materializer.MaterializeAsync(new ItemKey(ItemKind.Movie, Id: 205119), "813", null, CancellationToken.None));

        Assert.Equal(new Guid?[] { movie, other }, results);
        Assert.Equal(1, _browser.MaxConcurrentBrowses);
        Assert.Single(_browser.Browsed);
    }

    [Fact]
    public async Task Waits_for_items_created_after_the_browse_returns()
    {
        // ChannelManager creates items after releasing its lock: a concurrent browse can return before they exist.
        var category = _browser.ItemId("vodcat:813", typeof(Folder));
        var movie = _browser.ItemId("movie:203260", typeof(Movie));
        _browser.Existing.Add(_browser.ItemId("vod", typeof(Folder)));
        _browser.Existing.Add(category);
        _browser.AppearsAfterChecks[movie] = 4;

        Assert.Equal(movie, await Open(Movie));
        Assert.Single(_browser.Browsed);
    }

    [Fact]
    public async Task Gives_up_after_retries()
    {
        var category = _browser.ItemId("vodcat:813", typeof(Folder));
        var movie = _browser.ItemId("movie:203260", typeof(Movie));
        _browser.Existing.Add(_browser.ItemId("vod", typeof(Folder)));
        _browser.Existing.Add(category);
        _browser.AppearsAfterChecks[movie] = 50;

        Assert.Null(await Open(Movie));
    }

    [Fact]
    public async Task Browses_without_the_request_token()
    {
        // An aborted request must not leave a folder half-created for Jellyfin's 3 h channel cache.
        using var cts = new CancellationTokenSource();
        var category = _browser.ItemId("vodcat:813", typeof(Folder));
        _browser.Existing.Add(_browser.ItemId("vod", typeof(Folder)));
        _browser.Existing.Add(category);
        _browser.Creates[category] = [_browser.ItemId("movie:203260", typeof(Movie))];

        await Materializer.MaterializeAsync(Movie, "813", null, cts.Token);

        Assert.All(_browser.Tokens, t => Assert.False(t.CanBeCanceled));
    }

    [Fact]
    public void Hashes_ids_like_channel_manager()
    {
        // ChannelManager.GetIdToHash: externalId + channelName + "16"; channel: "Channel " + name.
        Assert.Equal("movie:203260IPTV16", IptvIds.IdToHash("movie:203260"));
        Assert.Equal("Channel IPTV", IptvIds.ChannelKey);
    }

    private sealed class FakeChannelBrowser : IChannelBrowser
    {
        public HashSet<Guid> Existing { get; } = [];

        public Dictionary<Guid, List<Guid>> Creates { get; } = [];

        public List<Guid> Browsed { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        /// <summary>Item id -> number of Exists checks returning false before it appears.</summary>
        public Dictionary<Guid, int> AppearsAfterChecks { get; } = [];

        public TimeSpan BrowseDelay { get; set; }

        public int MaxConcurrentBrowses { get; private set; }

        private int _active;

        public Guid ItemId(string externalId, Type type) =>
            new(MD5.HashData(Encoding.UTF8.GetBytes(externalId + "|" + type.FullName)));

        public bool Exists(Guid itemId)
        {
            lock (this)
            {
                if (AppearsAfterChecks.TryGetValue(itemId, out var left))
                {
                    AppearsAfterChecks[itemId] = left - 1;
                    return left <= 0;
                }

                return Existing.Contains(itemId);
            }
        }

        public async Task BrowseAsync(Guid parentId, User? user, CancellationToken cancellationToken)
        {
            lock (this)
            {
                MaxConcurrentBrowses = Math.Max(MaxConcurrentBrowses, ++_active);
                Browsed.Add(parentId);
                Tokens.Add(cancellationToken);
            }

            if (BrowseDelay > TimeSpan.Zero)
            {
                await Task.Delay(BrowseDelay);
            }

            lock (this)
            {
                if (Creates.TryGetValue(parentId, out var children))
                {
                    Existing.UnionWith(children);
                }

                _active--;
            }
        }
    }
}
