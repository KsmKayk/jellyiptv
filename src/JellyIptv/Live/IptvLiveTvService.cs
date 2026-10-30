using System.Globalization;
using JellyIptv.Browse;
using JellyIptv.Configuration;
using JellyIptv.Xtream;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.Logging;

namespace JellyIptv.Live;

public class IptvLiveTvService(IXtreamApi api, IConfigProvider config, ILogger<IptvLiveTvService> logger) : ILiveTvService
{
    public const string ServiceName = "JellyIPTV";

    public string Name => ServiceName;

    public string HomePageUrl => string.Empty;

    private sealed record EpgIds(IReadOnlyList<XtreamLiveStream> Source, Dictionary<int, string> Map);

    private sealed record EpgIndex(IReadOnlyList<XmlTvProgramme> Source, ILookup<string, XmlTvProgramme> ByChannel);

    private EpgIds? _epgIds;
    private EpgIndex? _epgIndex;

    private Dictionary<int, string> GetEpgIds(IReadOnlyList<XtreamLiveStream> streams)
    {
        if (_epgIds is { } cached && ReferenceEquals(cached.Source, streams))
        {
            return cached.Map;
        }

        var map = new Dictionary<int, string>();
        foreach (var s in streams)
        {
            map[s.StreamId] = s.EpgChannelId;
        }

        _epgIds = new EpgIds(streams, map);
        return map;
    }

    private ILookup<string, XmlTvProgramme> GetEpgIndex(IReadOnlyList<XmlTvProgramme> epg)
    {
        if (_epgIndex is { } cached && ReferenceEquals(cached.Source, epg))
        {
            return cached.ByChannel;
        }

        var lookup = epg.ToLookup(p => p.ChannelId, StringComparer.Ordinal);
        _epgIndex = new EpgIndex(epg, lookup);
        return lookup;
    }

    public async Task<IEnumerable<ChannelInfo>> GetChannelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var hidden = config.Current.HiddenCategories.ToHashSet(StringComparer.Ordinal);
            return (await api.GetLiveStreamsAsync(cancellationToken).ConfigureAwait(false))
                .Where(s => !hidden.Contains("live:" + s.CategoryId))
                .Select(s => new ChannelInfo
                {
                    Id = s.StreamId.ToString(CultureInfo.InvariantCulture),
                    Number = s.Num.ToString(CultureInfo.InvariantCulture),
                    Name = s.Name,
                    ImageUrl = string.IsNullOrWhiteSpace(s.StreamIcon) ? null : s.StreamIcon,
                    ChannelType = ChannelType.TV,
                })
                .ToList();
        }
        catch (XtreamNotConfiguredException)
        {
            return [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Returning empty would make Jellyfin's guide refresh delete all channels and programmes.
            logger.LogError(ex, "JellyIPTV: falha ao listar canais ao vivo");
            throw;
        }
    }

    public async Task<IEnumerable<ProgramInfo>> GetProgramsAsync(string channelId, DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken)
    {
        try
        {
            if (!int.TryParse(channelId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var streamId))
            {
                return [];
            }

            var streams = await api.GetLiveStreamsAsync(cancellationToken).ConfigureAwait(false);
            var epgId = GetEpgIds(streams).GetValueOrDefault(streamId);
            if (string.IsNullOrEmpty(epgId))
            {
                return [];
            }

            var epg = await api.GetEpgAsync(cancellationToken).ConfigureAwait(false);
            return GetEpgIndex(epg)[epgId]
                .Where(p => p.Stop.UtcDateTime > startDateUtc && p.Start.UtcDateTime < endDateUtc)
                .DistinctBy(p => p.Start)
                .Select(p => new ProgramInfo
                {
                    Id = string.Create(CultureInfo.InvariantCulture, $"jellyiptv-{channelId}-{p.Start.ToUnixTimeSeconds()}"),
                    ChannelId = channelId,
                    Name = p.Title,
                    Overview = p.Description,
                    StartDate = p.Start.UtcDateTime,
                    EndDate = p.Stop.UtcDateTime,
                })
                .ToList();
        }
        catch (XtreamNotConfiguredException)
        {
            return [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Cached EPG failures repeat for every channel, so keep those at Debug.
            logger.Log(ex is XtreamException ? LogLevel.Debug : LogLevel.Error, ex, "JellyIPTV: falha ao carregar EPG do canal {ChannelId}", channelId);
            throw;
        }
    }

    public Task<MediaSourceInfo> GetChannelStream(string channelId, string streamId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(channelId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            throw new ArgumentException("Canal inválido", nameof(channelId));
        }

        var userAgent = XtreamCredentials.From(config.Current).UserAgent;
        return Task.FromResult(MediaSourceFactory.Create("live:" + channelId, api.Urls.Live(id), "ts", userAgent, isLive: true));
    }

    public async Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(string channelId, CancellationToken cancellationToken)
        => [await GetChannelStream(channelId, string.Empty, cancellationToken).ConfigureAwait(false)];

    public Task CloseLiveStream(string id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ResetTuner(string id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IEnumerable<TimerInfo>> GetTimersAsync(CancellationToken cancellationToken) => Task.FromResult<IEnumerable<TimerInfo>>([]);

    public Task<IEnumerable<SeriesTimerInfo>> GetSeriesTimersAsync(CancellationToken cancellationToken) => Task.FromResult<IEnumerable<SeriesTimerInfo>>([]);

    public Task<SeriesTimerInfo> GetNewTimerDefaultsAsync(CancellationToken cancellationToken, ProgramInfo? program = null) => Task.FromResult(new SeriesTimerInfo());

    public Task CreateTimerAsync(TimerInfo info, CancellationToken cancellationToken) => throw NotSupported();

    public Task CreateSeriesTimerAsync(SeriesTimerInfo info, CancellationToken cancellationToken) => throw NotSupported();

    public Task UpdateTimerAsync(TimerInfo updatedTimer, CancellationToken cancellationToken) => throw NotSupported();

    public Task UpdateSeriesTimerAsync(SeriesTimerInfo info, CancellationToken cancellationToken) => throw NotSupported();

    public Task CancelTimerAsync(string timerId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CancelSeriesTimerAsync(string timerId, CancellationToken cancellationToken) => Task.CompletedTask;

    private static NotSupportedException NotSupported() => new("Gravação não é suportada pelo JellyIPTV.");
}
