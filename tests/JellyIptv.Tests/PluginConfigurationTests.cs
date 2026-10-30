using JellyIptv.Configuration;
using Xunit;

namespace JellyIptv.Tests;

public class PluginConfigurationTests
{
    [Fact]
    public void Defaults_match_spec()
    {
        var c = new PluginConfiguration();
        Assert.Equal("/data/movies", c.MoviesFolder);
        Assert.Equal("/data/tv", c.SeriesFolder);
        Assert.Equal(1, c.MaxConcurrentDownloads);
        Assert.Equal("VLC/3.0.18 LibVLC/3.0.18", c.UserAgent);
        Assert.Empty(c.HiddenCategories);
        Assert.False(c.AllowNonAdminDownloads);
        Assert.True(c.PauseWhileStreaming);
    }

    [Fact]
    public void ContentVersion_is_stable_and_changes_with_content_settings()
    {
        var a = new PluginConfiguration { BaseUrl = "http://x", Username = "u", Password = "p", HiddenCategories = ["vod:1", "live:2"] };
        var b = new PluginConfiguration { BaseUrl = "http://x", Username = "u", Password = "p", HiddenCategories = ["live:2", "vod:1"] };
        Assert.Equal(a.ContentVersion(), b.ContentVersion());

        b.HiddenCategories = ["vod:1"];
        Assert.NotEqual(a.ContentVersion(), b.ContentVersion());

        var c = new PluginConfiguration { BaseUrl = "http://x", Username = "u", Password = "p", HiddenCategories = ["vod:1", "live:2"], MoviesFolder = "/other" };
        Assert.Equal(a.ContentVersion(), c.ContentVersion());
    }

    [Fact]
    public void StampSourceChanges_stamps_only_when_source_settings_change()
    {
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var previous = new PluginConfiguration { BaseUrl = "http://x", Username = "u", Password = "p", SourcesChangedUtc = old };

        var passwordChanged = new PluginConfiguration { BaseUrl = "http://x", Username = "u", Password = "q" };
        PluginConfiguration.StampSourceChanges(previous, passwordChanged, now);
        Assert.Equal(now, passwordChanged.SourcesChangedUtc);

        var folderChanged = new PluginConfiguration { BaseUrl = "http://x", Username = "u", Password = "p", MoviesFolder = "/other" };
        PluginConfiguration.StampSourceChanges(previous, folderChanged, now);
        Assert.Equal(old, folderChanged.SourcesChangedUtc);
    }

    [Fact]
    public void ContentVersion_changes_with_user_agent()
    {
        var a = new PluginConfiguration { BaseUrl = "http://x", Username = "u", Password = "p" };
        var b = new PluginConfiguration { BaseUrl = "http://x", Username = "u", Password = "p", UserAgent = "Other/1" };
        Assert.NotEqual(a.ContentVersion(), b.ContentVersion());
    }
}
