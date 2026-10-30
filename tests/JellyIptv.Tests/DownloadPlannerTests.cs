using JellyIptv.Browse;
using JellyIptv.Downloads;
using JellyIptv.Tests.TestSupport;
using Xunit;

namespace JellyIptv.Tests;

public class DownloadPlannerTests
{
    private readonly FakeXtreamApi _api = new();
    private readonly FixedConfig _config = new();

    private DownloadPlanner Planner => new(_api, _config);

    private Task<IReadOnlyList<DownloadJob>> Plan(ItemKey key) => Planner.PlanAsync(key, CancellationToken.None);

    [Fact]
    public async Task Plans_movie()
    {
        var job = Assert.Single(await Plan(new ItemKey(ItemKind.Movie, Id: 203260)));
        Assert.Equal(DownloadKind.Movie, job.Kind);
        Assert.Equal(203260, job.StreamId);
        Assert.Equal("mp4", job.Extension);
        Assert.Equal("Anaconda (2025)", job.Title);
        Assert.Equal(Path.Combine("/m", "Anaconda (2025)", "Anaconda (2025).mp4"), job.DestinationPath);
        Assert.Contains("vod_info:203260", _api.Calls);
    }

    [Fact]
    public async Task Plans_items_from_all_folders_like_their_category_copies()
    {
        Assert.Equal(203260, Assert.Single(await Plan(new ItemKey(ItemKind.Movie, Id: 203260, InAll: true))).StreamId);
        Assert.Equal(3, (await Plan(new ItemKey(ItemKind.Series, Id: 873, InAll: true))).Count);
        Assert.Equal(71999, Assert.Single(await Plan(new ItemKey(ItemKind.Episode, Id: 873, Season: 1, EpisodeId: 71999, InAll: true))).StreamId);
    }

    [Fact]
    public async Task Plans_whole_series()
    {
        var jobs = await Plan(new ItemKey(ItemKind.Series, Id: 873));
        Assert.Equal(3, jobs.Count);
        Assert.Equal(DownloadKind.Episode, jobs[0].Kind);
        Assert.Equal(71999, jobs[0].StreamId);
        Assert.Equal("Yu Yu Hakusho S01E01 - A Morte", jobs[0].Title);
        Assert.Equal(Path.Combine("/tv", "Yu Yu Hakusho (1992)", "Season 01", "Yu Yu Hakusho S01E01 - A Morte.mp4"), jobs[0].DestinationPath);
    }

    [Fact]
    public async Task Plans_season_and_episode()
    {
        Assert.Equal(3, (await Plan(new ItemKey(ItemKind.Season, Id: 873, Season: 1))).Count);
        Assert.Empty(await Plan(new ItemKey(ItemKind.Season, Id: 873, Season: 2)));

        var job = Assert.Single(await Plan(new ItemKey(ItemKind.Episode, Id: 873, Season: 1, EpisodeId: 72000)));
        Assert.Equal(72000, job.StreamId);
        Assert.Equal(
            Path.Combine("/tv", "Yu Yu Hakusho (1992)", "Season 01", "Yu Yu Hakusho S01E02 - No Mundo Espiritual Teste da Ressurreição.mp4"),
            job.DestinationPath);
    }

    [Fact]
    public async Task Rejects_folders_and_missing_config()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Plan(new ItemKey(ItemKind.VodCategory, CategoryId: "813")));

        _config.Current.MoviesFolder = " ";
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Plan(new ItemKey(ItemKind.Movie, Id: 1)));
        Assert.Contains("filmes", ex.Message, StringComparison.Ordinal);
    }
}
