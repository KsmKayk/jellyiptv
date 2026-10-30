using JellyIptv.Configuration;
using JellyIptv.Xtream;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace JellyIptv.Browse;

/// <remarks>
/// ISupportsMediaProbe: Jellyfin probes each item once on first play and stores duration and codecs.
/// Without a duration Jellyfin treats VOD as a live stream (no seeking) and always transcodes.
/// The provider accepts ffmpeg's default User-Agent, so the probe works without our header.
/// </remarks>
public class IptvChannel(IXtreamApi api, IConfigProvider config, ILogger<IptvChannel> logger) : IChannel, ISupportsMediaProbe
{
    public const string ChannelName = "IPTV";

    /// <summary>Bumped when the stored media-source format changes, so existing items are re-saved once.</summary>
    internal static readonly DateTime SourcesFormatUtc = new(2026, 10, 30, 2, 0, 0, DateTimeKind.Utc);

    public string Name => ChannelName;

    public string Description => "Filmes e séries da sua conta Xtream.";

    public string DataVersion => $"{typeof(IptvChannel).Assembly.GetName().Version}-{config.Current.ContentVersion()}";

    public string HomePageUrl => string.Empty;

    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    public InternalChannelFeatures GetChannelFeatures() => new()
    {
        ContentTypes = [ChannelMediaContentType.Movie, ChannelMediaContentType.Episode],
        MediaTypes = [ChannelMediaType.Video],
    };

    public bool IsEnabledFor(string userId) => true;

    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        => Task.FromResult(new DynamicImageResponse { HasImage = false });

    public IEnumerable<ImageType> GetSupportedChannelImages() => [];

    public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<ChannelItemInfo> all;
        try
        {
            all = await GetItemsAsync(query.FolderId, cancellationToken).ConfigureAwait(false);
        }
        catch (XtreamNotConfiguredException)
        {
            all = [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never return an empty list on provider errors: Jellyfin caches results and deletes children missing from them.
            logger.LogError(ex, "JellyIPTV: falha ao listar {FolderId}", query.FolderId);
            throw;
        }

        // Jellyfin 10.10 doesn't pass StartIndex/Limit; kept for API completeness.
        IEnumerable<ChannelItemInfo> page = all;
        if (query.StartIndex is int start)
        {
            page = page.Skip(start);
        }

        if (query.Limit is int limit)
        {
            page = page.Take(limit);
        }

        return new ChannelItemResult { Items = page.ToList(), TotalRecordCount = all.Count };
    }

    private async Task<IReadOnlyList<ChannelItemInfo>> GetItemsAsync(string? folderId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(folderId))
        {
            return
            [
                ChannelItemBuilder.Folder(new ItemKey(ItemKind.VodRoot), "Filmes"),
                ChannelItemBuilder.Folder(new ItemKey(ItemKind.SeriesRoot), "Séries"),
            ];
        }

        if (!ItemKey.TryParse(folderId, out var key))
        {
            return [];
        }

        var cfg = config.Current;
        var hidden = cfg.HiddenCategories.ToHashSet(StringComparer.Ordinal);
        var userAgent = XtreamCredentials.From(cfg).UserAgent;
        var stamp = DateTime.SpecifyKind(cfg.SourcesChangedUtc, DateTimeKind.Utc);
        var sourcesChanged = stamp > SourcesFormatUtc ? stamp : SourcesFormatUtc;
        switch (key.Kind)
        {
            case ItemKind.VodRoot:
                return (await api.GetVodCategoriesAsync(ct).ConfigureAwait(false))
                    .Where(c => !hidden.Contains("vod:" + c.CategoryId))
                    .Select(c => ChannelItemBuilder.Folder(new ItemKey(ItemKind.VodCategory, CategoryId: c.CategoryId), c.CategoryName))
                    .Prepend(ChannelItemBuilder.Folder(new ItemKey(ItemKind.VodAll), "Todos os filmes"))
                    .ToList();
            case ItemKind.SeriesRoot:
                return (await api.GetSeriesCategoriesAsync(ct).ConfigureAwait(false))
                    .Where(c => !hidden.Contains("series:" + c.CategoryId))
                    .Select(c => ChannelItemBuilder.Folder(new ItemKey(ItemKind.SeriesCategory, CategoryId: c.CategoryId), c.CategoryName))
                    .Prepend(ChannelItemBuilder.Folder(new ItemKey(ItemKind.SeriesAll), "Todas as séries"))
                    .ToList();
            case ItemKind.VodAll:
                var allUrls = api.Urls;
                return (await api.GetAllVodStreamsAsync(ct).ConfigureAwait(false))
                    .Where(s => !hidden.Contains("vod:" + s.CategoryId))
                    .Select(s => ChannelItemBuilder.Movie(s, allUrls, userAgent, sourcesChanged, inAll: true))
                    .OrderBy(i => i.Name, StringComparer.InvariantCultureIgnoreCase)
                    .ToList();
            case ItemKind.SeriesAll:
                return (await api.GetAllSeriesAsync(ct).ConfigureAwait(false))
                    .Where(s => !hidden.Contains("series:" + s.CategoryId))
                    .Select(s => ChannelItemBuilder.Show(s, inAll: true))
                    .OrderBy(i => i.Name, StringComparer.InvariantCultureIgnoreCase)
                    .ToList();
            case ItemKind.VodCategory:
                var urls = api.Urls;
                return (await api.GetVodStreamsAsync(key.CategoryId, ct).ConfigureAwait(false))
                    .Select(s => ChannelItemBuilder.Movie(s, urls, userAgent, sourcesChanged))
                    .ToList();
            case ItemKind.SeriesCategory:
                return (await api.GetSeriesAsync(key.CategoryId, ct).ConfigureAwait(false))
                    .Select(s => ChannelItemBuilder.Show(s))
                    .ToList();
            case ItemKind.Series:
                return ChannelItemBuilder.Seasons(key.Id, await api.GetSeriesInfoAsync(key.Id, ct).ConfigureAwait(false), key.InAll);
            case ItemKind.Season:
                var info = await api.GetSeriesInfoAsync(key.Id, ct).ConfigureAwait(false);
                return ChannelItemBuilder.Episodes(key.Id, key.Season, info, api.Urls, userAgent, sourcesChanged, key.InAll);
            default:
                return [];
        }
    }
}
