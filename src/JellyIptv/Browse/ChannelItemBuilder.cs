using System.Globalization;
using JellyIptv.Naming;
using JellyIptv.Xtream;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;

namespace JellyIptv.Browse;

internal static class ChannelItemBuilder
{
    public static ChannelItemInfo Folder(ItemKey key, string name, string? imageUrl = null, string? overview = null) => new()
    {
        Id = key.Format(),
        Name = name,
        Type = ChannelItemType.Folder,
        FolderType = ChannelFolderType.Container,
        ImageUrl = NullIfEmpty(imageUrl),
        Overview = NullIfEmpty(overview),
    };

    public static ChannelItemInfo Movie(XtreamVodStream stream, XtreamUrls urls, string userAgent, DateTime sourcesChangedUtc, bool inAll = false)
    {
        var (title, year) = MediaNaming.ParseTitleYear(stream.Name);
        var key = new ItemKey(ItemKind.Movie, Id: stream.StreamId, InAll: inAll).Format();
        var ext = MediaNaming.NormalizeExtension(stream.ContainerExtension);
        return new ChannelItemInfo
        {
            Id = key,
            Name = title,
            ProductionYear = year,
            Type = ChannelItemType.Media,
            ContentType = ChannelMediaContentType.Movie,
            MediaType = ChannelMediaType.Video,
            ImageUrl = NullIfEmpty(stream.StreamIcon),
            CommunityRating = stream.Rating > 0 ? (float)stream.Rating : null,
            DateCreated = FromUnix(stream.Added),
            DateModified = sourcesChangedUtc,
            MediaSources = [MediaSourceFactory.Create(key, urls.Movie(stream.StreamId, ext), ext, userAgent, isLive: false)],
        };
    }

    public static ChannelItemInfo Show(XtreamSeries series, bool inAll = false)
    {
        var (title, year) = MediaNaming.ParseTitleYear(series.Name, series.ReleaseDate);
        var item = Folder(new ItemKey(ItemKind.Series, Id: series.SeriesId, InAll: inAll), title, series.Cover, series.Plot);
        item.ProductionYear = year;
        item.CommunityRating = series.Rating > 0 ? (float)series.Rating : null;
        item.Genres = SplitGenres(series.Genre);
        return item;
    }

    /// <summary>Seasons come from the episodes keys; the "seasons" list only supplies names/covers.</summary>
    public static IReadOnlyList<ChannelItemInfo> Seasons(int seriesId, XtreamSeriesInfo info, bool inAll = false) => info.Episodes.Keys
        .Order()
        .Select(n =>
        {
            var season = info.Seasons?.FirstOrDefault(s => s.SeasonNumber == n);
            var item = Folder(
                new ItemKey(ItemKind.Season, Id: seriesId, Season: n, InAll: inAll),
                SeasonName(n, season),
                NullIfEmpty(season?.Cover) ?? info.Info?.Cover,
                season?.Overview);
            item.IndexNumber = n;
            return item;
        })
        .ToList();

    public static IReadOnlyList<ChannelItemInfo> Episodes(int seriesId, int season, XtreamSeriesInfo info, XtreamUrls urls, string userAgent, DateTime sourcesChangedUtc, bool inAll = false)
    {
        if (!info.Episodes.TryGetValue(season, out var episodes))
        {
            return [];
        }

        var seriesTitle = MediaNaming.ParseTitleYear(info.Info?.Name).Title;
        return episodes.Select((episode, index) =>
        {
            var number = episode.NumberOr(index);
            var title = MediaNaming.CleanEpisodeTitle(episode.Title);
            var key = new ItemKey(ItemKind.Episode, Id: seriesId, Season: season, EpisodeId: episode.Id, InAll: inAll).Format();
            var ext = MediaNaming.NormalizeExtension(episode.ContainerExtension);
            return new ChannelItemInfo
            {
                Id = key,
                Name = title.Length > 0 ? $"{number}. {title}" : $"Episódio {number}",
                SeriesName = seriesTitle,
                IndexNumber = number,
                ParentIndexNumber = season,
                Type = ChannelItemType.Media,
                ContentType = ChannelMediaContentType.Episode,
                MediaType = ChannelMediaType.Video,
                Overview = NullIfEmpty(episode.Info?.Plot),
                ImageUrl = NullIfEmpty(episode.Info?.MovieImage) ?? NullIfEmpty(info.Info?.Cover),
                RunTimeTicks = episode.Info is { DurationSecs: > 0 } details ? TimeSpan.FromSeconds(details.DurationSecs).Ticks : null,
                DateCreated = FromUnix(episode.Added),
                DateModified = sourcesChangedUtc,
                MediaSources = [MediaSourceFactory.Create(key, urls.Episode(episode.Id, ext), ext, userAgent, isLive: false, episode.Info?.Video, episode.Info?.Audio)],
            };
        }).ToList();
    }

    public static string SeasonName(int number, XtreamSeason? season)
    {
        if (!string.IsNullOrWhiteSpace(season?.Name))
        {
            return season.Name;
        }

        return number == 0 ? "Especiais" : $"Temporada {number}";
    }

    private static List<string> SplitGenres(string genres) =>
        genres.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static DateTime? FromUnix(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
