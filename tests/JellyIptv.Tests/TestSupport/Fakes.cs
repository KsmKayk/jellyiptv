using JellyIptv.Downloads;
using Xunit;
using JellyIptv.Xtream;
using System.Net;
using System.Text;
using JellyIptv.Configuration;

namespace JellyIptv.Tests.TestSupport;

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        return Task.FromResult(respond(request));
    }
}

internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal sealed class FixedConfig : IConfigProvider
{
    public PluginConfiguration Current { get; set; } = new()
    {
        BaseUrl = "http://example.com:8880",
        Username = "user",
        Password = "pass",
        MoviesFolder = "/m",
        SeriesFolder = "/tv",
    };
}

internal sealed class FakeXtreamApi : IXtreamApi
{
    public XtreamCredentials Credentials { get; set; } = new("http://example.com:8880", "user", "pass", XtreamCredentials.DefaultUserAgent);

    public List<XtreamCategory> VodCategories { get; set; } = Fixtures.Load<List<XtreamCategory>>("vod_categories.json");

    public List<XtreamCategory> SeriesCategories { get; set; } = Fixtures.Load<List<XtreamCategory>>("series_categories.json");

    public List<XtreamCategory> LiveCategories { get; set; } = [new XtreamCategory { CategoryId = "77", CategoryName = "Canais: 24hrs" }];

    public List<XtreamVodStream> VodStreams { get; set; } = Fixtures.Load<List<XtreamVodStream>>("vod_streams.json");

    public List<XtreamSeries> Series { get; set; } = Fixtures.Load<List<XtreamSeries>>("series.json");

    public XtreamSeriesInfo SeriesInfo { get; set; } = Fixtures.Load<XtreamSeriesInfo>("series_info.json");

    public XtreamVodInfo VodInfo { get; set; } = Fixtures.Load<XtreamVodInfo>("vod_info.json");

    public List<XtreamLiveStream> LiveStreams { get; set; } = Fixtures.Load<List<XtreamLiveStream>>("live_streams.json");

    public List<XmlTvProgramme> Epg { get; set; } = LoadEpg();

    public Exception? Throw { get; set; }

    public List<string> Calls { get; } = [];

    public XtreamUrls Urls => new(Credentials);

    public Task<XtreamUserInfo?> GetUserInfoAsync(XtreamCredentials credentials, CancellationToken cancellationToken)
        => Return<XtreamUserInfo?>("user_info", Fixtures.Load<XtreamAuthResponse>("auth.json").UserInfo);

    public Task<IReadOnlyList<XtreamCategory>> GetVodCategoriesAsync(CancellationToken cancellationToken) => Return<IReadOnlyList<XtreamCategory>>("vod_categories", VodCategories);

    public Task<IReadOnlyList<XtreamCategory>> GetSeriesCategoriesAsync(CancellationToken cancellationToken) => Return<IReadOnlyList<XtreamCategory>>("series_categories", SeriesCategories);

    public Task<IReadOnlyList<XtreamCategory>> GetLiveCategoriesAsync(CancellationToken cancellationToken) => Return<IReadOnlyList<XtreamCategory>>("live_categories", LiveCategories);

    public Task<IReadOnlyList<XtreamVodStream>> GetVodStreamsAsync(string categoryId, CancellationToken cancellationToken) => Return<IReadOnlyList<XtreamVodStream>>($"vod_streams:{categoryId}", VodStreams);

    public Task<IReadOnlyList<XtreamSeries>> GetSeriesAsync(string categoryId, CancellationToken cancellationToken) => Return<IReadOnlyList<XtreamSeries>>($"series:{categoryId}", Series);

    public Task<IReadOnlyList<XtreamVodStream>> GetAllVodStreamsAsync(CancellationToken cancellationToken) => Return<IReadOnlyList<XtreamVodStream>>("vod_streams:all", VodStreams);

    public Task<IReadOnlyList<XtreamSeries>> GetAllSeriesAsync(CancellationToken cancellationToken) => Return<IReadOnlyList<XtreamSeries>>("series:all", Series);

    public Task<XtreamSeriesInfo> GetSeriesInfoAsync(int seriesId, CancellationToken cancellationToken) => Return($"series_info:{seriesId}", SeriesInfo);

    public Task<XtreamVodInfo> GetVodInfoAsync(int vodId, CancellationToken cancellationToken) => Return($"vod_info:{vodId}", VodInfo);

    public Task<IReadOnlyList<XtreamLiveStream>> GetLiveStreamsAsync(CancellationToken cancellationToken) => Return<IReadOnlyList<XtreamLiveStream>>("live_streams", LiveStreams);

    public Task<IReadOnlyList<XmlTvProgramme>> GetEpgAsync(CancellationToken cancellationToken) => Return<IReadOnlyList<XmlTvProgramme>>("epg", Epg);

    private static List<XmlTvProgramme> LoadEpg()
    {
        using var stream = Fixtures.Open("epg.xml");
        return XmlTvParser.Parse(stream).ToList();
    }

    private Task<T> Return<T>(string call, T value)
    {
        lock (Calls)
        {
            Calls.Add(call);
        }

        return Throw is null ? Task.FromResult(value) : Task.FromException<T>(Throw);
    }
}

internal sealed class FakeDownloader : IFileDownloader
{
    private int _calls;

    public Func<int, string, CancellationToken, Task> Behavior { get; set; } = (_, dest, _) =>
    {
        File.WriteAllText(dest, "x");
        return Task.CompletedTask;
    };

    public int Calls => Volatile.Read(ref _calls);

    public List<string> Urls { get; } = [];

    public async Task DownloadAsync(string url, string userAgent, string destinationPath, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _calls);
        lock (Urls)
        {
            Urls.Add(url);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await Behavior(call, destinationPath, cancellationToken);
    }
}

internal sealed class FakeScanTrigger : ILibraryScanTrigger
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Trigger() => Interlocked.Increment(ref _count);
}

internal static class Eventually
{
    public static async Task True(Func<bool> condition, string because, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for: " + because);
            await Task.Delay(20);
        }
    }
}
