using System.Text;
using System.Text.Json;
using JellyIptv.Tests.TestSupport;
using JellyIptv.Xtream;
using Xunit;

namespace JellyIptv.Tests;

public class XtreamJsonTests
{
    [Fact]
    public void Parses_categories_fixture()
    {
        var cats = Fixtures.Load<List<XtreamCategory>>("vod_categories.json");
        Assert.Equal(3, cats.Count);
        Assert.Equal("813", cats[0].CategoryId);
        Assert.Equal("Filmes: Lançamentos 2026", cats[0].CategoryName);
    }

    [Fact]
    public void Parses_vod_streams_fixture()
    {
        var vod = Fixtures.Load<List<XtreamVodStream>>("vod_streams.json");
        Assert.Equal(203260, vod[0].StreamId);
        Assert.Equal("Anaconda (2025)", vod[0].Name);
        Assert.Equal(6.0, vod[0].Rating);
        Assert.Equal("mp4", vod[0].ContainerExtension);
        Assert.Equal("813", vod[0].CategoryId);
        Assert.Equal("1767762535", vod[0].Added);
    }

    [Fact]
    public void Tolerates_numbers_as_strings_and_nulls()
    {
        var cats = JsonSerializer.Deserialize<List<XtreamCategory>>("""[{"category_id":813,"category_name":null}]""", XtreamJson.Options)!;
        Assert.Equal("813", cats[0].CategoryId);
        Assert.Equal(string.Empty, cats[0].CategoryName);

        var vod = JsonSerializer.Deserialize<List<XtreamVodStream>>("""[{"stream_id":"42","rating":"","name":123,"container_extension":null}]""", XtreamJson.Options)!;
        Assert.Equal(42, vod[0].StreamId);
        Assert.Equal(0, vod[0].Rating);
        Assert.Equal("123", vod[0].Name);
        Assert.Equal(string.Empty, vod[0].ContainerExtension);

        var big = JsonSerializer.Deserialize<List<XtreamVodStream>>("""[{"stream_id":1e20}]""", XtreamJson.Options)!;
        Assert.Equal(int.MaxValue, big[0].StreamId);
    }

    [Fact]
    public void Parses_series_info_fixture()
    {
        var info = Fixtures.Load<XtreamSeriesInfo>("series_info.json");
        Assert.Equal("Yu Yu Hakusho", info.Info!.Name);
        Assert.Equal("1992-10-10", info.Info.ReleaseDate);
        Assert.Equal(new[] { 1 }, info.Episodes.Keys.ToArray());
        var ep = info.Episodes[1][0];
        Assert.Equal(71999, ep.Id);
        Assert.Equal(1, ep.EpisodeNum);
        Assert.Equal("Yu Yu Hakusho - S01E01 - A Morte", ep.Title);
        Assert.Equal(1420, ep.Info!.DurationSecs);
        Assert.Equal(5, info.Seasons!.Count);
        Assert.Equal("1ª Temporada: Saga do Detetive Espiritual", info.Seasons[1].Name);
        Assert.Equal(1, info.Seasons[1].SeasonNumber);
    }

    [Fact]
    public void Parses_episodes_as_array_and_info_as_empty_array()
    {
        const string json = """{"seasons":null,"info":[],"episodes":[[{"id":"5","episode_num":"3","season":2,"title":"x","container_extension":"mkv","info":[]}]]}""";
        var info = JsonSerializer.Deserialize<XtreamSeriesInfo>(json, XtreamJson.Options)!;
        Assert.Null(info.Info);
        Assert.Null(info.Seasons);
        var ep = Assert.Single(info.Episodes[2]);
        Assert.Equal(5, ep.Id);
        Assert.Equal(3, ep.EpisodeNum);
        Assert.Null(ep.Info);
    }

    [Fact]
    public void Parses_vod_info_and_auth_fixtures()
    {
        var vod = Fixtures.Load<XtreamVodInfo>("vod_info.json");
        Assert.Equal("mp4", vod.MovieData!.ContainerExtension);
        Assert.Equal("Anaconda (2025)", vod.MovieData.Name);
        Assert.Equal("2025-12-24", vod.Info!.ReleaseDate);

        var auth = Fixtures.Load<XtreamAuthResponse>("auth.json");
        Assert.Equal(1, auth.UserInfo!.Auth);
        Assert.Equal(1, auth.UserInfo.MaxConnections);
        Assert.Equal("1793933940", auth.UserInfo.ExpDate);
    }

    [Fact]
    public void Parses_live_streams_fixture()
    {
        var live = Fixtures.Load<List<XtreamLiveStream>>("live_streams.json");
        Assert.Equal(21028, live[0].StreamId);
        Assert.Equal(1, live[0].Num);
        Assert.Equal("Apenas um Show 24H", live[0].EpgChannelId);
        Assert.Equal("77", live[0].CategoryId);
    }

    [Fact]
    public async Task Skips_unexpected_values_when_streaming()
    {
        var json = "{\"info\":[],\"x\":\"" + new string('a', 20000) + "\",\"episodes\":{\"1\":[{\"id\":1,\"info\":[],\"title\":7}]}}";
        var info = await JsonSerializer.DeserializeAsync<XtreamSeriesInfo>(
            new MemoryStream(Encoding.UTF8.GetBytes(json)),
            new JsonSerializerOptions(XtreamJson.Options) { DefaultBufferSize = 16 });
        Assert.Null(info!.Info);
        var ep = Assert.Single(info.Episodes[1]);
        Assert.Equal(1, ep.Id);
        Assert.Null(ep.Info);
        Assert.Equal("7", ep.Title);
    }

    [Fact]
    public void Parses_episode_stream_info_or_tolerates_arrays()
    {
        var withInfo = JsonSerializer.Deserialize<XtreamEpisodeInfo>(
            """{"video":{"codec_name":"h264","width":960,"height":720,"profile":"High"},"audio":{"codec_name":"aac","channels":2,"profile":"LC"}}""",
            XtreamJson.Options)!;
        Assert.Equal("h264", withInfo.Video!.CodecName);
        Assert.Equal(960, withInfo.Video.Width);
        Assert.Equal(720, withInfo.Video.Height);
        Assert.Equal("High", withInfo.Video.Profile);
        Assert.Equal("aac", withInfo.Audio!.CodecName);
        Assert.Equal(2, withInfo.Audio.Channels);

        var empty = JsonSerializer.Deserialize<XtreamEpisodeInfo>("""{"video":[],"audio":[]}""", XtreamJson.Options)!;
        Assert.Null(empty.Video);
        Assert.Null(empty.Audio);
    }
}
