using JellyIptv.Browse;
using Xunit;

namespace JellyIptv.Tests;

public class ItemKeyTests
{
    public static TheoryData<string, ItemKey> Keys => new()
    {
        { "vod", new ItemKey(ItemKind.VodRoot) },
        { "series", new ItemKey(ItemKind.SeriesRoot) },
        { "vodcat:813", new ItemKey(ItemKind.VodCategory, CategoryId: "813") },
        { "sercat:691", new ItemKey(ItemKind.SeriesCategory, CategoryId: "691") },
        { "vodcat:a:b", new ItemKey(ItemKind.VodCategory, CategoryId: "a:b") },
        { "movie:203260", new ItemKey(ItemKind.Movie, Id: 203260) },
        { "show:873", new ItemKey(ItemKind.Series, Id: 873) },
        { "season:873:1", new ItemKey(ItemKind.Season, Id: 873, Season: 1) },
        { "episode:873:1:71999", new ItemKey(ItemKind.Episode, Id: 873, Season: 1, EpisodeId: 71999) },
        { "vodall", new ItemKey(ItemKind.VodAll) },
        { "serall", new ItemKey(ItemKind.SeriesAll) },
        { "allmovie:203260", new ItemKey(ItemKind.Movie, Id: 203260, InAll: true) },
        { "allshow:873", new ItemKey(ItemKind.Series, Id: 873, InAll: true) },
        { "allseason:873:1", new ItemKey(ItemKind.Season, Id: 873, Season: 1, InAll: true) },
        { "allepisode:873:1:71999", new ItemKey(ItemKind.Episode, Id: 873, Season: 1, EpisodeId: 71999, InAll: true) },
    };

    [Theory]
    [MemberData(nameof(Keys))]
    public void Round_trips(string text, ItemKey key)
    {
        Assert.Equal(text, key.Format());
        Assert.True(ItemKey.TryParse(text, out var parsed));
        Assert.Equal(key, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("movie:")]
    [InlineData("movie:abc")]
    [InlineData("movie:+5")]
    [InlineData("movie:-1")]
    [InlineData("movie: 5")]
    [InlineData("season:1")]
    [InlineData("vodcat:")]
    [InlineData("https://example.com/x")]
    [InlineData("vodall:1")]
    [InlineData("allvod")]
    [InlineData("allvodcat:813")]
    [InlineData("allmovie:")]
    public void Rejects_invalid(string? text)
    {
        Assert.False(ItemKey.TryParse(text, out _));
    }
}
