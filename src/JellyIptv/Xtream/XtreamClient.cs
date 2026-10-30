using System.Text.Json;
using JellyIptv.Configuration;

namespace JellyIptv.Xtream;

public class XtreamException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class XtreamNotConfiguredException(string message) : XtreamException(message);

public interface IXtreamApi
{
    /// <summary>URLs built from the currently configured credentials.</summary>
    XtreamUrls Urls { get; }

    Task<XtreamUserInfo?> GetUserInfoAsync(XtreamCredentials credentials, CancellationToken cancellationToken);

    Task<IReadOnlyList<XtreamCategory>> GetVodCategoriesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<XtreamCategory>> GetSeriesCategoriesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<XtreamCategory>> GetLiveCategoriesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<XtreamVodStream>> GetVodStreamsAsync(string categoryId, CancellationToken cancellationToken);

    Task<IReadOnlyList<XtreamSeries>> GetSeriesAsync(string categoryId, CancellationToken cancellationToken);

    /// <summary>Every movie of the account (no category_id; ~2.5 MB for ~7.7k movies).</summary>
    Task<IReadOnlyList<XtreamVodStream>> GetAllVodStreamsAsync(CancellationToken cancellationToken);

    /// <summary>Every series of the account (no category_id).</summary>
    Task<IReadOnlyList<XtreamSeries>> GetAllSeriesAsync(CancellationToken cancellationToken);

    Task<XtreamSeriesInfo> GetSeriesInfoAsync(int seriesId, CancellationToken cancellationToken);

    Task<XtreamVodInfo> GetVodInfoAsync(int vodId, CancellationToken cancellationToken);

    Task<IReadOnlyList<XtreamLiveStream>> GetLiveStreamsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<XmlTvProgramme>> GetEpgAsync(CancellationToken cancellationToken);
}

public sealed class XtreamClient(IHttpClientFactory httpClientFactory, IConfigProvider config, TtlCache cache) : IXtreamApi
{
    private static readonly TimeSpan ListTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan EpgTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan EpgFailureTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(2);

    private sealed record EpgFailure(string Key, DateTimeOffset Until, XtreamException Error);

    private EpgFailure? _epgFailure;

    public XtreamUrls Urls => new(XtreamCredentials.From(config.Current));

    public async Task<XtreamUserInfo?> GetUserInfoAsync(XtreamCredentials credentials, CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<XtreamAuthResponse>(credentials, new XtreamUrls(credentials).PlayerApi(null), cancellationToken).ConfigureAwait(false);
        return response.UserInfo;
    }

    public Task<IReadOnlyList<XtreamCategory>> GetVodCategoriesAsync(CancellationToken cancellationToken)
        => GetListAsync<XtreamCategory>("get_vod_categories", null, cancellationToken);

    public Task<IReadOnlyList<XtreamCategory>> GetSeriesCategoriesAsync(CancellationToken cancellationToken)
        => GetListAsync<XtreamCategory>("get_series_categories", null, cancellationToken);

    public Task<IReadOnlyList<XtreamCategory>> GetLiveCategoriesAsync(CancellationToken cancellationToken)
        => GetListAsync<XtreamCategory>("get_live_categories", null, cancellationToken);

    public Task<IReadOnlyList<XtreamVodStream>> GetVodStreamsAsync(string categoryId, CancellationToken cancellationToken)
        => GetListAsync<XtreamVodStream>("get_vod_streams", "category_id=" + Uri.EscapeDataString(categoryId), cancellationToken);

    public Task<IReadOnlyList<XtreamSeries>> GetSeriesAsync(string categoryId, CancellationToken cancellationToken)
        => GetListAsync<XtreamSeries>("get_series", "category_id=" + Uri.EscapeDataString(categoryId), cancellationToken);

    public Task<IReadOnlyList<XtreamVodStream>> GetAllVodStreamsAsync(CancellationToken cancellationToken)
        => GetListAsync<XtreamVodStream>("get_vod_streams", null, cancellationToken);

    public Task<IReadOnlyList<XtreamSeries>> GetAllSeriesAsync(CancellationToken cancellationToken)
        => GetListAsync<XtreamSeries>("get_series", null, cancellationToken);

    public Task<XtreamSeriesInfo> GetSeriesInfoAsync(int seriesId, CancellationToken cancellationToken)
        => GetCachedAsync<XtreamSeriesInfo>("get_series_info", $"series_id={seriesId}", cancellationToken);

    public Task<XtreamVodInfo> GetVodInfoAsync(int vodId, CancellationToken cancellationToken)
        => GetCachedAsync<XtreamVodInfo>("get_vod_info", $"vod_id={vodId}", cancellationToken);

    public Task<IReadOnlyList<XtreamLiveStream>> GetLiveStreamsAsync(CancellationToken cancellationToken)
        => GetListAsync<XtreamLiveStream>("get_live_streams", null, cancellationToken);

    public async Task<IReadOnlyList<XmlTvProgramme>> GetEpgAsync(CancellationToken cancellationToken)
    {
        var credentials = RequireCredentials();
        if (_epgFailure is { } failure && failure.Key == credentials.CacheKey && failure.Until > DateTimeOffset.UtcNow)
        {
            throw new XtreamException(failure.Error.Message, failure.Error);
        }

        // Shared request runs with its own timeout instead of the caller's token so one caller aborting doesn't fail the others.
        try
        {
            return await GetEpgCoreAsync(credentials, cancellationToken).ConfigureAwait(false);
        }
        catch (XtreamException ex)
        {
            _epgFailure = new EpgFailure(credentials.CacheKey, DateTimeOffset.UtcNow + EpgFailureTtl, ex);
            throw;
        }
    }

    private Task<IReadOnlyList<XmlTvProgramme>> GetEpgCoreAsync(XtreamCredentials credentials, CancellationToken cancellationToken)
    {
        return cache.GetOrAddAsync($"{credentials.CacheKey}|epg", EpgTtl, async () =>
        {
            using var cts = new CancellationTokenSource(RequestTimeout);
            try
            {
                using var response = await SendAsync(credentials, new XtreamUrls(credentials).XmlTv(), cts.Token).ConfigureAwait(false);
                await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
                return XmlTvParser.Parse(stream);
            }
            catch (IOException ex)
            {
                throw new XtreamException($"Falha ao ler a resposta do servidor Xtream: {ex.Message}", ex);
            }
            catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
            {
                throw new XtreamException("Tempo esgotado ao contatar o servidor Xtream.", ex);
            }
        }).WaitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string action, string? query, CancellationToken cancellationToken)
        => await GetCachedAsync<List<T>>(action, query, cancellationToken).ConfigureAwait(false);

    private async Task<T> GetCachedAsync<T>(string action, string? query, CancellationToken cancellationToken)
    {
        var credentials = RequireCredentials();
        var url = new XtreamUrls(credentials).PlayerApi(action, query);
        return await cache.GetOrAddAsync($"{credentials.CacheKey}|{action}|{query}", ListTtl, async () =>
        {
            using var cts = new CancellationTokenSource(RequestTimeout);
            try
            {
                return await GetJsonAsync<T>(credentials, url, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
            {
                throw new XtreamException("Tempo esgotado ao contatar o servidor Xtream.", ex);
            }
        }).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private XtreamCredentials RequireCredentials()
    {
        var credentials = XtreamCredentials.From(config.Current);
        return credentials.IsConfigured ? credentials : throw new XtreamNotConfiguredException("JellyIPTV não configurado: informe URL, usuário e senha.");
    }

    private async Task<T> GetJsonAsync<T>(XtreamCredentials credentials, string url, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(credentials, url, cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, XtreamJson.Options, cancellationToken).ConfigureAwait(false)
                ?? throw new XtreamException("Resposta vazia do servidor Xtream.");
        }
        catch (JsonException ex)
        {
            throw new XtreamException("Resposta inválida do servidor Xtream.", ex);
        }
        catch (IOException ex)
        {
            throw new XtreamException($"Falha ao ler a resposta do servidor Xtream: {ex.Message}", ex);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(XtreamCredentials credentials, string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new XtreamException($"URL do servidor inválida: {credentials.BaseUrl}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", credentials.UserAgent);
        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient("JellyIptv")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new XtreamException($"Não foi possível conectar ao servidor Xtream: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new XtreamException("Tempo esgotado ao contatar o servidor Xtream.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            var hint = status == 403 ? " O servidor recusou o acesso: credenciais erradas ou IP bloqueado (VPN/datacenter)." : string.Empty;
            throw new XtreamException($"Servidor Xtream respondeu HTTP {status}.{hint}");
        }

        return response;
    }
}
