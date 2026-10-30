using JellyIptv.Live;
using JellyIptv.Tests.TestSupport;
using JellyIptv.Xtream;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyIptv.Tests;

public class IptvLiveTvServiceTests
{
    private readonly FakeXtreamApi _api = new();
    private readonly FixedConfig _config = new();

    private IptvLiveTvService Service => new(_api, _config, NullLogger<IptvLiveTvService>.Instance);

    [Fact]
    public async Task Lists_channels_except_hidden_categories()
    {
        var channels = (await Service.GetChannelsAsync(CancellationToken.None)).ToList();
        Assert.Equal(3, channels.Count);
        Assert.Equal("21028", channels[0].Id);
        Assert.Equal("1", channels[0].Number);
        Assert.Equal("Apenas um Show 24H", channels[0].Name);
        Assert.Equal("http://logos.example.com/Logo/apenas.png", channels[0].ImageUrl);
        Assert.Equal(ChannelType.TV, channels[0].ChannelType);

        _config.Current.HiddenCategories = ["live:77"];
        Assert.Empty(await Service.GetChannelsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Returns_programmes_in_window()
    {
        var all = (await Service.GetProgramsAsync("7598", new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), CancellationToken.None)).ToList();
        Assert.Equal(2, all.Count);
        Assert.Equal("Canal 24H - Seu Servidor Favorito.", all[0].Name);
        Assert.Equal("7598", all[0].ChannelId);
        Assert.Equal(new DateTime(2026, 10, 29, 3, 0, 0, DateTimeKind.Utc), all[0].StartDate);
        Assert.Equal(DateTimeKind.Utc, all[0].StartDate.Kind);
        Assert.NotEqual(all[0].Id, all[1].Id);

        var first = await Service.GetProgramsAsync("7598", new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc), CancellationToken.None);
        Assert.Single(first);

        Assert.Empty(await Service.GetProgramsAsync("999", DateTime.MinValue, DateTime.MaxValue, CancellationToken.None));
    }

    [Fact]
    public async Task Builds_live_stream_source()
    {
        var source = await Service.GetChannelStream("21028", string.Empty, CancellationToken.None);
        Assert.Equal("http://example.com:8880/live/user/pass/21028.ts", source.Path);
        Assert.True(source.IsInfiniteStream);
        Assert.Contains(source.MediaStreams, s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Video && s.IsInterlaced);
        Assert.Equal("VLC/3.0.18 LibVLC/3.0.18", source.RequiredHttpHeaders["User-Agent"]);

        var sources = await Service.GetChannelStreamMediaSources("21028", CancellationToken.None);
        Assert.Equal(source.Path, Assert.Single(sources).Path);
    }

    [Fact]
    public async Task Not_configured_is_empty_but_other_errors_propagate()
    {
        _api.Throw = new XtreamNotConfiguredException("no config");
        Assert.Empty(await Service.GetChannelsAsync(CancellationToken.None));

        _api.Throw = new XtreamException("down");
        await Assert.ThrowsAsync<XtreamException>(() => Service.GetChannelsAsync(CancellationToken.None));
        await Assert.ThrowsAsync<XtreamException>(() => Service.GetProgramsAsync("7598", DateTime.MinValue, DateTime.MaxValue, CancellationToken.None));
    }

    [Fact]
    public async Task Channel_without_epg_id_has_no_programmes()
    {
        _api.LiveStreams[0].EpgChannelId = string.Empty;
        Assert.Empty(await Service.GetProgramsAsync("21028", DateTime.MinValue, DateTime.MaxValue, CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_channel_id_throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service.GetChannelStream("abc", string.Empty, CancellationToken.None));
    }
}
