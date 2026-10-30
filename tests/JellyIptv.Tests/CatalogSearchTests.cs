using JellyIptv.Browse;
using JellyIptv.Tests.TestSupport;
using JellyIptv.Xtream;
using Xunit;

namespace JellyIptv.Tests;

public class CatalogSearchTests
{
    private readonly FakeXtreamApi _api = new();
    private readonly FixedConfig _config = new();

    private CatalogSearch Search => new(_api, _config);

    private Task<CatalogSearchResult> Find(string? query, int limit = CatalogSearch.DefaultLimit)
        => Search.SearchAsync(query, limit, CancellationToken.None);

    private static XtreamVodStream Movie(int id, string name, string category = "813") =>
        new() { StreamId = id, Name = name, CategoryId = category };

    [Fact]
    public async Task Returns_movie_and_series_dtos()
    {
        var movies = (await Find("anaconda")).Movies;
        var movie = Assert.Single(movies);
        Assert.Equal("movie:203260", movie.Key);
        Assert.Equal("813", movie.CategoryId);
        Assert.Equal("Anaconda", movie.Name);
        Assert.Equal(2025, movie.Year);
        Assert.Equal("https://image.tmdb.org/t/p/w600_and_h900_bestv2/hRzpf8QELtzJnMt4SiDoFUmZ9KM.jpg", movie.ImageUrl);

        var result = await Find("timao");
        Assert.Empty(result.Movies);
        var show = Assert.Single(result.Series);
        Assert.Equal("show:73", show.Key);
        Assert.Equal("691", show.CategoryId);
        Assert.Equal("Timão e Pumba", show.Name);
        Assert.Equal(1995, show.Year);
        Assert.Contains("vod_streams:all", _api.Calls);
        Assert.Contains("series:all", _api.Calls);
    }

    [Fact]
    public async Task Ignores_accents_and_case_on_both_sides()
    {
        _api.VodStreams = [Movie(1, "Filmes LANÇAMENTOS 2026"), Movie(2, "Outro")];

        Assert.Equal("movie:1", Assert.Single((await Find("lancamentos")).Movies).Key);
        Assert.Single((await Find("Lançamentos")).Movies);
        Assert.Single((await Find("LANCAMENTOS")).Movies);
    }

    [Fact]
    public async Task Every_token_must_match()
    {
        _api.Series = [new XtreamSeries { SeriesId = 873, Name = "Yu Yu Hakusho", CategoryId = "691" }];

        Assert.Single((await Find("hak  yu")).Series);
        Assert.Empty((await Find("yu naruto")).Series);
    }

    [Fact]
    public async Task Skips_hidden_categories()
    {
        _api.VodStreams = [Movie(1, "Matrix (1999)", "813"), Movie(2, "Matrix Reloaded (2003)", "540")];
        _api.Series = [new XtreamSeries { SeriesId = 9, Name = "Matrix: Série", CategoryId = "691" }];
        _config.Current.HiddenCategories = ["vod:540", "series:691"];

        var result = await Find("matrix");

        Assert.Equal("movie:1", Assert.Single(result.Movies).Key);
        Assert.Empty(result.Series);
    }

    [Fact]
    public async Task Ranks_prefix_matches_first_then_by_name()
    {
        _api.VodStreams =
        [
            Movie(1, "The Matrix (1999)"),
            Movie(2, "Animatrix (2003)"),
            Movie(3, "matrix Resurrections (2021)"),
            Movie(4, "Matrix (1999)"),
        ];

        var names = (await Find("matrix")).Movies.Select(m => m.Name).ToArray();

        Assert.Equal(new[] { "Matrix", "matrix Resurrections", "Animatrix", "The Matrix" }, names);
    }

    [Fact]
    public async Task Ranking_ignores_leading_tags_but_keeps_display_names()
    {
        _api.VodStreams =
        [
            Movie(1, "Animatrix (2003)"),
            Movie(2, "[4K] Matrix Reloaded (2003)"),
            Movie(3, "BR | Matrix (1999)"),
            Movie(4, "(L) 4K - Matrix Resurrections (2021)"),
        ];

        var names = (await Find("matrix")).Movies.Select(m => m.Name).ToArray();

        Assert.Equal(new[] { "BR | Matrix", "[4K] Matrix Reloaded", "(L) 4K - Matrix Resurrections", "Animatrix" }, names);
    }

    [Theory]
    [InlineData("BR | Matrix", "Matrix")]
    [InlineData("[4K] Matrix", "Matrix")]
    [InlineData("4K - Matrix", "Matrix")]
    [InlineData("(L) [BR] 4K: Matrix", "Matrix")]
    [InlineData("Matrix (1999)", "Matrix (1999)")]
    [InlineData("Matrix - Reloaded", "Matrix - Reloaded")]
    public void Strips_leading_tags(string input, string expected)
    {
        Assert.Equal(expected, CatalogSearch.StripTags(input));
    }

    [Fact]
    public async Task Skips_items_without_category()
    {
        _api.VodStreams = [Movie(1, "Matrix", ""), Movie(2, "Matrix 2")];
        _api.Series = [new XtreamSeries { SeriesId = 9, Name = "Matrix", CategoryId = "" }];

        var result = await Find("matrix");

        Assert.Equal("movie:2", Assert.Single(result.Movies).Key);
        Assert.Empty(result.Series);
    }

    [Fact]
    public async Task Repeated_searches_on_the_same_list_stay_consistent()
    {
        _api.VodStreams = [Movie(1, "Lançamento"), Movie(2, "Outro")];

        Assert.Single((await Find("lanca")).Movies);
        Assert.Single((await Find("outro")).Movies);
        Assert.Single((await Find("lanca")).Movies);
    }

    [Fact]
    public async Task Limits_each_list()
    {
        _api.VodStreams = Enumerable.Range(1, 30).Select(i => Movie(i, $"Filme {i:00}")).ToList();

        Assert.Equal(CatalogSearch.DefaultLimit, (await Find("filme")).Movies.Count);
        Assert.Equal(5, (await Find("filme", 5)).Movies.Count);
        Assert.Equal("Filme 01", (await Find("filme", 5)).Movies[0].Name);
        Assert.Equal(CatalogSearch.DefaultLimit, (await Find("filme", 0)).Movies.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("  a  ")]
    public async Task Short_queries_are_empty_without_calling_xtream(string? query)
    {
        var result = await Find(query);

        Assert.Empty(result.Movies);
        Assert.Empty(result.Series);
        Assert.Empty(_api.Calls);
    }

    [Fact]
    public async Task Not_configured_is_empty()
    {
        _api.Throw = new XtreamNotConfiguredException("no config");

        var result = await Find("matrix");

        Assert.Empty(result.Movies);
        Assert.Empty(result.Series);
    }

    [Theory]
    [InlineData("Lançamentos", "lancamentos")]
    [InlineData("ÁÉÍÕÜ ç", "aeiou c")]
    [InlineData("Timão", "timao")]
    public void Normalizes(string input, string expected)
    {
        Assert.Equal(expected, CatalogSearch.Normalize(input));
    }
}
