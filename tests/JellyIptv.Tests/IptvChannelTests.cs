using JellyIptv.Browse;
using JellyIptv.Tests.TestSupport;
using JellyIptv.Xtream;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyIptv.Tests;

public class IptvChannelTests
{
    private readonly FakeXtreamApi _api = new();
    private readonly FixedConfig _config = new();

    private IptvChannel Channel => new(_api, _config, NullLogger<IptvChannel>.Instance);

    private Task<ChannelItemResult> Get(string? folderId, int? start = null, int? limit = null)
        => Channel.GetChannelItems(new InternalChannelItemQuery { FolderId = folderId!, StartIndex = start, Limit = limit }, CancellationToken.None);

    [Fact]
    public async Task Root_has_movies_and_series_folders()
    {
        var result = await Get(null);
        Assert.Equal(new[] { "vod", "series" }, result.Items.Select(i => i.Id).ToArray());
        Assert.Equal(new[] { "Filmes", "Séries" }, result.Items.Select(i => i.Name).ToArray());
        Assert.All(result.Items, i => Assert.Equal(ChannelItemType.Folder, i.Type));
    }

    [Fact]
    public async Task Lists_categories_except_hidden()
    {
        Assert.Equal(4, (await Get("vod")).Items.Count);

        _config.Current.HiddenCategories = ["vod:540"];
        var result = await Get("vod");
        Assert.Equal(new[] { "vodall", "vodcat:813", "vodcat:757" }, result.Items.Select(i => i.Id).ToArray());
        Assert.Equal("Filmes: Lançamentos 2026", result.Items[1].Name);

        _config.Current.HiddenCategories = ["series:691"];
        Assert.Equal(new[] { "serall", "sercat:640", "sercat:808" }, (await Get("series")).Items.Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task All_folders_come_first()
    {
        var vod = (await Get("vod")).Items[0];
        Assert.Equal("vodall", vod.Id);
        Assert.Equal("Todos os filmes", vod.Name);
        Assert.Equal(ChannelItemType.Folder, vod.Type);

        var series = (await Get("series")).Items[0];
        Assert.Equal("serall", series.Id);
        Assert.Equal("Todas as séries", series.Name);
    }

    [Fact]
    public async Task All_movies_are_sorted_by_name_and_skip_hidden_categories()
    {
        _api.VodStreams =
        [
            new XtreamVodStream { StreamId = 1, Name = "zebra (2020)", CategoryId = "813" },
            new XtreamVodStream { StreamId = 2, Name = "Ábaco (2021)", CategoryId = "540" },
            new XtreamVodStream { StreamId = 3, Name = "Matrix (1999)", CategoryId = "757" },
            new XtreamVodStream { StreamId = 4, Name = "abelha", CategoryId = "813" },
        ];
        _config.Current.HiddenCategories = ["vod:757", "series:813"];

        var result = await Get("vodall");

        Assert.Equal(new[] { "Ábaco", "abelha", "zebra" }, result.Items.Select(i => i.Name).ToArray());
        Assert.Equal(new[] { "allmovie:2", "allmovie:4", "allmovie:1" }, result.Items.Select(i => i.Id).ToArray());
        Assert.All(result.Items, i => Assert.Equal(ChannelMediaContentType.Movie, i.ContentType));
        Assert.Equal("http://example.com:8880/movie/user/pass/1.mp4", result.Items[2].MediaSources[0].Path);
        Assert.Contains("vod_streams:all", _api.Calls);
    }

    [Fact]
    public async Task All_series_are_sorted_and_keep_their_subtree_separate()
    {
        _api.Series =
        [
            new XtreamSeries { SeriesId = 873, Name = "Yu Yu Hakusho", CategoryId = "691" },
            new XtreamSeries { SeriesId = 5, Name = "alf", CategoryId = "640" },
            new XtreamSeries { SeriesId = 6, Name = "Hidden", CategoryId = "808" },
        ];
        _config.Current.HiddenCategories = ["series:808", "vod:691"];

        var shows = await Get("serall");
        Assert.Equal(new[] { "allshow:5", "allshow:873" }, shows.Items.Select(i => i.Id).ToArray());
        Assert.Contains("series:all", _api.Calls);

        var season = Assert.Single((await Get("allshow:873")).Items);
        Assert.Equal("allseason:873:1", season.Id);

        var episodes = (await Get("allseason:873:1")).Items;
        Assert.Equal("allepisode:873:1:71999", episodes[0].Id);
        Assert.Equal("http://example.com:8880/series/user/pass/71999.mp4", episodes[0].MediaSources[0].Path);
    }

    [Fact]
    public async Task Lists_movies_with_playable_sources()
    {
        var result = await Get("vodcat:813");

        Assert.Equal(3, result.Items.Count);
        var movie = result.Items[0];
        Assert.Equal("movie:203260", movie.Id);
        Assert.Equal("Anaconda", movie.Name);
        Assert.Equal(2025, movie.ProductionYear);
        Assert.Equal(ChannelItemType.Media, movie.Type);
        Assert.Equal(ChannelMediaContentType.Movie, movie.ContentType);
        var source = Assert.Single(movie.MediaSources);
        Assert.Equal("http://example.com:8880/movie/user/pass/203260.mp4", source.Path);
        Assert.Equal("VLC/3.0.18 LibVLC/3.0.18", source.RequiredHttpHeaders["User-Agent"]);
        Assert.False(source.SupportsDirectPlay);
        Assert.Contains("vod_streams:813", _api.Calls);
    }

    [Fact]
    public async Task Lists_series_seasons_and_episodes()
    {
        var shows = await Get("sercat:691");
        Assert.Equal("show:73", shows.Items[0].Id);
        Assert.Equal("Timão e Pumba", shows.Items[0].Name);
        Assert.Equal(1995, shows.Items[0].ProductionYear);

        var seasons = await Get("show:873");
        var season = Assert.Single(seasons.Items);
        Assert.Equal("season:873:1", season.Id);
        Assert.Equal("1ª Temporada: Saga do Detetive Espiritual", season.Name);
        Assert.Equal(1, season.IndexNumber);

        var episodes = await Get("season:873:1");
        Assert.Equal(3, episodes.Items.Count);
        var ep = episodes.Items[0];
        Assert.Equal("episode:873:1:71999", ep.Id);
        Assert.Equal("1. A Morte", ep.Name);
        Assert.Equal("Yu Yu Hakusho", ep.SeriesName);
        Assert.Equal(1, ep.IndexNumber);
        Assert.Equal(1, ep.ParentIndexNumber);
        Assert.Equal(TimeSpan.FromSeconds(1420).Ticks, ep.RunTimeTicks);
        Assert.Equal(ChannelMediaContentType.Episode, ep.ContentType);
        Assert.Equal("http://example.com:8880/series/user/pass/71999.mp4", ep.MediaSources[0].Path);

        Assert.Empty((await Get("season:873:9")).Items);
    }

    [Fact]
    public async Task Pages_results()
    {
        var result = await Get("vodcat:813", start: 1, limit: 1);
        Assert.Equal(3, result.TotalRecordCount);
        Assert.Equal("movie:205119", Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task Unknown_folders_are_empty()
    {
        Assert.Empty((await Get("nonsense")).Items);
    }

    [Fact]
    public async Task Not_configured_is_empty_but_other_errors_propagate()
    {
        _api.Throw = new XtreamNotConfiguredException("no config");
        Assert.Empty((await Get("vod")).Items);

        _api.Throw = new XtreamException("down");
        await Assert.ThrowsAsync<XtreamException>(() => Get("vod"));
    }

    [Fact]
    public async Task Series_listing_requests_series_info()
    {
        await Get("show:873");
        await Get("season:873:1");
        Assert.Contains("series_info:873", _api.Calls);
    }

    [Fact]
    public async Task Episode_source_is_not_direct_play_and_has_user_agent()
    {
        var ep = (await Get("season:873:1")).Items[0];
        var source = ep.MediaSources[0];
        Assert.False(source.SupportsDirectPlay);
        Assert.False(source.SupportsDirectStream);
        Assert.Equal("VLC/3.0.18 LibVLC/3.0.18", source.RequiredHttpHeaders["User-Agent"]);
    }

    [Fact]
    public async Task Source_ids_are_stable_and_distinct()
    {
        var first = (await Get("vodcat:813")).Items;
        var second = (await Get("vodcat:813")).Items;
        Assert.Equal(first[0].MediaSources[0].Id, second[0].MediaSources[0].Id);
        Assert.NotEqual(first[0].MediaSources[0].Id, first[1].MediaSources[0].Id);
    }

    [Fact]
    public async Task Media_items_carry_sources_changed_stamp_as_date_modified()
    {
        var stamp = IptvChannel.SourcesFormatUtc.AddDays(1);
        _config.Current.SourcesChangedUtc = stamp;

        var movie = (await Get("vodcat:813")).Items[0];
        var episode = (await Get("season:873:1")).Items[0];

        Assert.Equal(stamp, movie.DateModified);
        Assert.Equal(stamp, episode.DateModified);
    }

    [Fact]
    public async Task Date_modified_is_at_least_sources_format_stamp()
    {
        // Older stored sources (from before media probing) must be re-saved once after upgrading.
        _config.Current.SourcesChangedUtc = default;

        var movie = (await Get("vodcat:813")).Items[0];

        Assert.Equal(IptvChannel.SourcesFormatUtc, movie.DateModified);
    }

    [Fact]
    public void Channel_lets_jellyfin_probe_media()
    {
        // With ISupportsMediaProbe Jellyfin probes once and stores duration/codecs, so VOD is seekable
        // instead of being treated as a live stream (no RunTimeTicks => live HLS).
        Assert.IsAssignableFrom<ISupportsMediaProbe>(Channel);
    }

    [Fact]
    public async Task Movie_source_has_placeholder_streams_without_codec()
    {
        var movie = (await Get("vodcat:813")).Items[0];
        var video = Assert.Single(movie.MediaSources[0].MediaStreams, s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Video);
        Assert.Null(video.Codec);
        Assert.False(video.IsInterlaced);
        Assert.Contains(movie.MediaSources[0].MediaStreams, s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Audio);
    }

    [Fact]
    public async Task Episode_source_carries_real_codec_info()
    {
        var episode = _api.SeriesInfo.Episodes[1][0];
        episode.Info ??= new XtreamEpisodeInfo();
        episode.Info.Video = new XtreamStreamInfo { CodecName = "h264", Width = 960, Height = 720, Profile = "High" };
        episode.Info.Audio = new XtreamStreamInfo { CodecName = "aac", Channels = 2, Profile = "LC" };

        var streams = (await Get("season:873:1")).Items[0].MediaSources[0].MediaStreams;
        var video = Assert.Single(streams, s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Video);
        Assert.Equal("h264", video.Codec);
        Assert.Equal(960, video.Width);
        Assert.Equal(720, video.Height);
        Assert.False(video.IsInterlaced);
        var audio = Assert.Single(streams, s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Audio);
        Assert.Equal("aac", audio.Codec);
        Assert.Equal(2, audio.Channels);
    }

    [Fact]
    public void Data_version_follows_config()
    {
        var before = Channel.DataVersion;
        _config.Current.HiddenCategories = ["vod:1"];
        Assert.NotEqual(before, Channel.DataVersion);
        Assert.Equal("IPTV", Channel.Name);
    }
}
