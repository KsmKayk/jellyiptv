using JellyIptv.Browse;
using JellyIptv.Configuration;
using JellyIptv.Naming;
using JellyIptv.Xtream;

namespace JellyIptv.Downloads;

public sealed class DownloadPlanner(IXtreamApi api, IConfigProvider config)
{
    public async Task<IReadOnlyList<DownloadJob>> PlanAsync(ItemKey key, CancellationToken cancellationToken)
    {
        var cfg = config.Current;
        switch (key.Kind)
        {
            case ItemKind.Movie:
                return [await PlanMovieAsync(key.Id, RequireFolder(cfg.MoviesFolder, "filmes"), cancellationToken).ConfigureAwait(false)];
            case ItemKind.Series or ItemKind.Season or ItemKind.Episode:
                return await PlanEpisodesAsync(key, RequireFolder(cfg.SeriesFolder, "séries"), cancellationToken).ConfigureAwait(false);
            default:
                throw new ArgumentException($"'{key.Format()}' não pode ser baixado.", nameof(key));
        }
    }

    private async Task<DownloadJob> PlanMovieAsync(int vodId, string root, CancellationToken ct)
    {
        var info = await api.GetVodInfoAsync(vodId, ct).ConfigureAwait(false);
        var name = FirstNonEmpty(info.MovieData?.Name, info.Info?.Name) ?? $"Filme {vodId}";
        var (title, year) = MediaNaming.ParseTitleYear(name, info.Info?.ReleaseDate);
        var ext = MediaNaming.NormalizeExtension(info.MovieData?.ContainerExtension);
        return new DownloadJob
        {
            Kind = DownloadKind.Movie,
            StreamId = vodId,
            Extension = ext,
            Title = MediaNaming.WithYear(title, year),
            DestinationPath = MediaNaming.MoviePath(root, title, year, ext),
        };
    }

    private async Task<IReadOnlyList<DownloadJob>> PlanEpisodesAsync(ItemKey key, string root, CancellationToken ct)
    {
        var info = await api.GetSeriesInfoAsync(key.Id, ct).ConfigureAwait(false);
        var (title, year) = MediaNaming.ParseTitleYear(FirstNonEmpty(info.Info?.Name) ?? $"Serie {key.Id}", info.Info?.ReleaseDate);
        var jobs = new List<DownloadJob>();
        foreach (var (season, episodes) in info.Episodes.OrderBy(p => p.Key))
        {
            if (key.Kind != ItemKind.Series && season != key.Season)
            {
                continue;
            }

            for (var i = 0; i < episodes.Count; i++)
            {
                var episode = episodes[i];
                if (key.Kind == ItemKind.Episode && episode.Id != key.EpisodeId)
                {
                    continue;
                }

                var number = episode.NumberOr(i);
                var episodeTitle = MediaNaming.CleanEpisodeTitle(episode.Title);
                var ext = MediaNaming.NormalizeExtension(episode.ContainerExtension);
                jobs.Add(new DownloadJob
                {
                    Kind = DownloadKind.Episode,
                    StreamId = episode.Id,
                    Extension = ext,
                    Title = MediaNaming.EpisodeLabel(title, season, number, episodeTitle),
                    DestinationPath = MediaNaming.EpisodePath(root, title, year, season, number, episodeTitle, ext),
                });
            }
        }

        return jobs;
    }

    private static string RequireFolder(string folder, string label) =>
        string.IsNullOrWhiteSpace(folder) ? throw new InvalidOperationException($"Pasta de {label} não configurada.") : folder.Trim();

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
