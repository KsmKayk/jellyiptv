using System.Globalization;
using System.Text.RegularExpressions;

namespace JellyIptv.Naming;

public static partial class MediaNaming
{
    private const string InvalidChars = "\\/:*?\"<>|";

    public static (string Title, int? Year) ParseTitleYear(string? name, string? releaseDate = null)
    {
        name = (name ?? string.Empty).Trim();
        var match = TitleYearRegex().Match(name);
        if (match.Success && match.Groups["title"].Value.Length > 0)
        {
            return (match.Groups["title"].Value.Trim(), int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture));
        }

        var year = LeadingYearRegex().Match((releaseDate ?? string.Empty).Trim());
        return (name, year.Success ? int.Parse(year.Groups["year"].Value, CultureInfo.InvariantCulture) : null);
    }

    /// <summary>"Yu Yu Hakusho - S01E01 - A Morte" -> "A Morte".</summary>
    public static string CleanEpisodeTitle(string? raw)
    {
        var title = EpisodePrefixRegex().Replace(raw ?? string.Empty, string.Empty, 1);
        return title.Trim().TrimEnd('-').Trim();
    }

    public static string Sanitize(string? value)
    {
        var chars = (value ?? string.Empty).Where(c => !InvalidChars.Contains(c)).Select(c => char.IsControl(c) ? ' ' : c).ToArray();
        var cleaned = MultiSpaceRegex().Replace(new string(chars), " ").Trim();
        string previous;
        do
        {
            previous = cleaned;
            cleaned = cleaned.TrimStart('.').TrimEnd('.', ' ').Trim();
        }
        while (cleaned != previous);

        return cleaned.Length == 0 ? "Sem titulo" : cleaned;
    }

    public static string NormalizeExtension(string? extension)
    {
        var ext = new string((extension ?? string.Empty).ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
        return ext.Length == 0 ? "mp4" : ext;
    }

    public static string WithYear(string title, int? year) => year is null ? title : $"{title} ({year})";

    public static string EpisodeLabel(string seriesTitle, int season, int episode, string episodeTitle)
    {
        var label = string.Create(CultureInfo.InvariantCulture, $"{seriesTitle} S{season:00}E{episode:00}");
        return episodeTitle.Length > 0 ? $"{label} - {episodeTitle}" : label;
    }

    public static string MoviePath(string root, string title, int? year, string extension)
    {
        var name = Sanitize(WithYear(title, year));
        return Path.Combine(root, name, $"{name}.{NormalizeExtension(extension)}");
    }

    public static string EpisodePath(string root, string seriesTitle, int? seriesYear, int season, int episode, string episodeTitle, string extension)
    {
        var show = Sanitize(WithYear(seriesTitle, seriesYear));
        var seasonDir = string.Create(CultureInfo.InvariantCulture, $"Season {season:00}");
        var file = Sanitize(EpisodeLabel(seriesTitle, season, episode, episodeTitle));
        return Path.Combine(root, show, seasonDir, $"{file}.{NormalizeExtension(extension)}");
    }

    [GeneratedRegex(@"^(?<title>.*?)\s*[\(\[](?<year>(19|20)\d{2})[\)\]]\s*$")]
    private static partial Regex TitleYearRegex();

    [GeneratedRegex(@"^(?<year>(19|20)\d{2})")]
    private static partial Regex LeadingYearRegex();

    [GeneratedRegex(@"^.*?\bS\d{1,3}\s*E\d{1,4}\b\s*-?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodePrefixRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();
}
