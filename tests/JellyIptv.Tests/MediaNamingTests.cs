using JellyIptv.Naming;
using Xunit;

namespace JellyIptv.Tests;

public class MediaNamingTests
{
    [Theory]
    [InlineData("Anaconda (2025)", null, "Anaconda", 2025)]
    [InlineData("Exterminio O Templo dos Ossos (2026)", null, "Exterminio O Templo dos Ossos", 2026)]
    [InlineData("Yu Yu Hakusho", "1992-10-10", "Yu Yu Hakusho", 1992)]
    [InlineData("Timão e Pumba", "", "Timão e Pumba", null)]
    [InlineData("Filme [2019]", "2001-01-01", "Filme", 2019)]
    public void Parses_title_and_year(string name, string? releaseDate, string title, int? year)
    {
        Assert.Equal((title, year), MediaNaming.ParseTitleYear(name, releaseDate));
    }

    [Theory]
    [InlineData("Yu Yu Hakusho - S01E01 - A Morte", "A Morte")]
    [InlineData("Yu Yu Hakusho - S01E41 - ", "")]
    [InlineData("Show S2E10 Final", "Final")]
    [InlineData("Episódio especial", "Episódio especial")]
    public void Cleans_episode_titles(string raw, string expected)
    {
        Assert.Equal(expected, MediaNaming.CleanEpisodeTitle(raw));
    }

    [Theory]
    [InlineData("No Mundo Espiritual: Teste?", "No Mundo Espiritual Teste")]
    [InlineData("  a/b\\c*d  ", "abcd")]
    [InlineData("Fim...", "Fim")]
    [InlineData(":::", "Sem titulo")]
    [InlineData("a. .", "a")]
    [InlineData(".hack", "hack")]
    [InlineData("y\t\nz", "y z")]
    public void Sanitizes_file_names(string value, string expected)
    {
        Assert.Equal(expected, MediaNaming.Sanitize(value));
    }

    [Theory]
    [InlineData(".MKV", "mkv")]
    [InlineData("", "mp4")]
    [InlineData(null, "mp4")]
    [InlineData("m p4", "mp4")]
    public void Normalizes_extensions(string? ext, string expected)
    {
        Assert.Equal(expected, MediaNaming.NormalizeExtension(ext));
    }

    [Fact]
    public void Builds_movie_path()
    {
        Assert.Equal(
            Path.Combine("/m", "Anaconda (2025)", "Anaconda (2025).mp4"),
            MediaNaming.MoviePath("/m", "Anaconda", 2025, "mp4"));
        Assert.Equal(
            Path.Combine("/m", "Sem Ano", "Sem Ano.mkv"),
            MediaNaming.MoviePath("/m", "Sem Ano", null, "mkv"));
    }

    [Fact]
    public void Builds_episode_path_and_label()
    {
        Assert.Equal(
            Path.Combine("/tv", "Yu Yu Hakusho (1992)", "Season 01", "Yu Yu Hakusho S01E02 - No Mundo Espiritual Teste.mp4"),
            MediaNaming.EpisodePath("/tv", "Yu Yu Hakusho", 1992, 1, 2, "No Mundo Espiritual: Teste", "mp4"));
        Assert.Equal(
            Path.Combine("/tv", "Show", "Season 00", "Show S00E101.mkv"),
            MediaNaming.EpisodePath("/tv", "Show", null, 0, 101, "", "mkv"));
        Assert.Equal("Yu Yu Hakusho S01E02 - A: B", MediaNaming.EpisodeLabel("Yu Yu Hakusho", 1, 2, "A: B"));
    }
}
