using JellyIptv.Configuration;
using JellyIptv.Xtream;
using Xunit;

namespace JellyIptv.Tests;

public class XtreamUrlsTests
{
    private static readonly XtreamUrls Urls = new(new XtreamCredentials("http://example.com:8880/", "us er", "p&ss", "UA"));

    [Fact]
    public void Builds_player_api_urls_with_escaped_credentials()
    {
        Assert.Equal("http://example.com:8880/player_api.php?username=us%20er&password=p%26ss", Urls.PlayerApi(null));
        Assert.Equal("http://example.com:8880/player_api.php?username=us%20er&password=p%26ss&action=get_vod_streams&category_id=813", Urls.PlayerApi("get_vod_streams", "category_id=813"));
    }

    [Fact]
    public void Builds_stream_urls()
    {
        Assert.Equal("http://example.com:8880/movie/us%20er/p%26ss/203260.mp4", Urls.Movie(203260, "mp4"));
        Assert.Equal("http://example.com:8880/series/us%20er/p%26ss/71999.mkv", Urls.Episode(71999, "mkv"));
        Assert.Equal("http://example.com:8880/live/us%20er/p%26ss/21028.ts", Urls.Live(21028));
        Assert.Equal("http://example.com:8880/xmltv.php?username=us%20er&password=p%26ss", Urls.XmlTv());
    }

    [Fact]
    public void Credentials_from_config_normalize_values()
    {
        var c = XtreamCredentials.From(new PluginConfiguration { BaseUrl = " tv.example.org:8880/ ", Username = " u ", Password = "p", UserAgent = " " });
        Assert.Equal("http://tv.example.org:8880", c.BaseUrl);
        Assert.Equal("u", c.Username);
        Assert.Equal(XtreamCredentials.DefaultUserAgent, c.UserAgent);
        Assert.True(c.IsConfigured);
        Assert.False(XtreamCredentials.From(new PluginConfiguration()).IsConfigured);
    }

    [Theory]
    [InlineData("http://iptv.example.com:8880 ", "http://iptv.example.com:8880")]
    [InlineData("http://iptv.example.com :8880", "http://iptv.example.com:8880")]
    [InlineData("\thttp://iptv.example.com:8880/\n", "http://iptv.example.com:8880")]
    public void Base_url_whitespace_is_removed(string input, string expected)
    {
        Assert.Equal(expected, XtreamCredentials.From(new PluginConfiguration { BaseUrl = input, Username = "u", Password = "p" }).BaseUrl);
    }

    [Fact]
    public void ToString_does_not_leak_password()
    {
        Assert.DoesNotContain("p&ss", new XtreamCredentials("http://x", "u", "p&ss", "UA").ToString());
    }
}
