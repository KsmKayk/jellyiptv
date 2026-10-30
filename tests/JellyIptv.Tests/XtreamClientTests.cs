using System.Net;
using JellyIptv.Configuration;
using JellyIptv.Tests.TestSupport;
using JellyIptv.Xtream;
using Xunit;

namespace JellyIptv.Tests;

public class XtreamClientTests
{
    private static (XtreamClient Client, StubHandler Handler, FixedConfig Config) Create(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var handler = new StubHandler(respond ?? (req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("xmltv.php", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixtures.Text("epg.xml")) };
            }

            if (url.Contains("action=get_vod_streams", StringComparison.Ordinal))
            {
                return StubHandler.Json(Fixtures.Text("vod_streams.json"));
            }

            if (url.Contains("action=get_series_info", StringComparison.Ordinal))
            {
                return StubHandler.Json(Fixtures.Text("series_info.json"));
            }

            if (url.Contains("action=get_series", StringComparison.Ordinal))
            {
                return StubHandler.Json(Fixtures.Text("series.json"));
            }

            return StubHandler.Json(Fixtures.Text("auth.json"));
        }));
        var config = new FixedConfig();
        return (new XtreamClient(new FakeHttpClientFactory(handler), config, new TtlCache()), handler, config);
    }

    [Fact]
    public async Task Requests_vod_streams_with_user_agent_and_parses()
    {
        var (client, handler, _) = Create();

        var streams = await client.GetVodStreamsAsync("813", CancellationToken.None);

        Assert.Equal(3, streams.Count);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("http://example.com:8880/player_api.php?username=user&password=pass&action=get_vod_streams&category_id=813", req.RequestUri!.ToString());
        Assert.Equal("VLC/3.0.18 LibVLC/3.0.18", req.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Requests_all_vod_streams_without_category_and_caches()
    {
        var (client, handler, _) = Create();

        var streams = await client.GetAllVodStreamsAsync(CancellationToken.None);
        await client.GetAllVodStreamsAsync(CancellationToken.None);

        Assert.Equal(3, streams.Count);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("http://example.com:8880/player_api.php?username=user&password=pass&action=get_vod_streams", req.RequestUri!.ToString());

        await client.GetVodStreamsAsync("813", CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Requests_all_series_without_category_and_caches()
    {
        var (client, handler, _) = Create();

        var series = await client.GetAllSeriesAsync(CancellationToken.None);
        await client.GetAllSeriesAsync(CancellationToken.None);

        Assert.Equal(2, series.Count);
        Assert.Equal("Timão e Pumba", series[0].Name);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("http://example.com:8880/player_api.php?username=user&password=pass&action=get_series", req.RequestUri!.ToString());
    }

    [Fact]
    public async Task Caches_responses_per_credentials()
    {
        var (client, handler, config) = Create();

        await client.GetSeriesInfoAsync(873, CancellationToken.None);
        await client.GetSeriesInfoAsync(873, CancellationToken.None);
        Assert.Single(handler.Requests);

        config.Current = new PluginConfiguration { BaseUrl = "http://other", Username = "user", Password = "pass" };
        await client.GetSeriesInfoAsync(873, CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Throws_when_not_configured_or_http_error()
    {
        var (client, _, config) = Create(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAnyAsync<XtreamException>(() => client.GetVodCategoriesAsync(CancellationToken.None));

        config.Current = new PluginConfiguration();
        var ex = await Assert.ThrowsAnyAsync<XtreamException>(() => client.GetVodCategoriesAsync(CancellationToken.None));
        Assert.Contains("não configurad", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_url_becomes_friendly_error()
    {
        var (client, _, _) = Create();

        var ex = await Assert.ThrowsAnyAsync<XtreamException>(() => client.GetUserInfoAsync(new XtreamCredentials("http://iptv.example.com:88a80", "u", "p", "UA"), CancellationToken.None));
        Assert.Contains("URL do servidor inválida", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Forbidden_hints_at_blocked_ip()
    {
        var (client, _, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var ex = await Assert.ThrowsAnyAsync<XtreamException>(() => client.GetUserInfoAsync(new XtreamCredentials("http://x:1", "u", "p", "UA"), CancellationToken.None));
        Assert.Contains("HTTP 403", ex.Message, StringComparison.Ordinal);
        Assert.Contains("VPN", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_credentials_surface_as_faulted_task()
    {
        var (client, _, config) = Create();
        config.Current = new PluginConfiguration();

        var task = client.GetEpgAsync(CancellationToken.None);

        await Assert.ThrowsAnyAsync<XtreamException>(() => task);
        var task2 = client.GetSeriesInfoAsync(1, CancellationToken.None);
        await Assert.ThrowsAnyAsync<XtreamException>(() => task2);
    }

    [Fact]
    public async Task Parses_epg_and_user_info()
    {
        var (client, handler, _) = Create();

        var epg = await client.GetEpgAsync(CancellationToken.None);
        Assert.Equal(4, epg.Count);
        Assert.Equal("http://example.com:8880/xmltv.php?username=user&password=pass", handler.Requests[0].RequestUri!.ToString());

        var info = await client.GetUserInfoAsync(new XtreamCredentials("http://test:1", "a", "b", "UA"), CancellationToken.None);
        Assert.Equal(1, info!.Auth);
        Assert.Equal("http://test:1/player_api.php?username=a&password=b", handler.Requests[1].RequestUri!.ToString());
    }

    [Fact]
    public async Task Epg_failure_is_cached_briefly()
    {
        var (client, handler, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAnyAsync<XtreamException>(() => client.GetEpgAsync(CancellationToken.None));
        await Assert.ThrowsAnyAsync<XtreamException>(() => client.GetEpgAsync(CancellationToken.None));

        Assert.Single(handler.Requests);
    }
}
