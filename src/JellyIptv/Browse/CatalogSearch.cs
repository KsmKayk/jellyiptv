using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using JellyIptv.Configuration;
using JellyIptv.Naming;
using JellyIptv.Xtream;

namespace JellyIptv.Browse;

/// <param name="Key">ItemKey text of the category copy ("movie:{id}" / "show:{id}").</param>
public sealed record CatalogSearchItem(string Key, string CategoryId, string Name, int? Year, string? ImageUrl);

public sealed record CatalogSearchResult(IReadOnlyList<CatalogSearchItem> Movies, IReadOnlyList<CatalogSearchItem> Series)
{
    public static CatalogSearchResult Empty { get; } = new([], []);
}

/// <summary>Searches the whole Xtream catalog (movies and series) by name.</summary>
public sealed partial class CatalogSearch(IXtreamApi api, IConfigProvider config)
{
    public const int DefaultLimit = 24;
    public const int MaxLimit = 100;
    private const int MinQueryLength = 2;

    /// <summary>Normalized names per list instance (the Xtream client caches each list for 30 min).</summary>
    private static readonly ConditionalWeakTable<object, Entry[]> Index = new();

    private readonly record struct Entry(string Name, string RankName, CatalogSearchItem Item);

    public async Task<CatalogSearchResult> SearchAsync(string? query, int limit, CancellationToken cancellationToken)
    {
        var tokens = Normalize(query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var phrase = string.Join(' ', tokens);
        if (phrase.Length < MinQueryLength)
        {
            return CatalogSearchResult.Empty;
        }

        limit = limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);
        var hidden = config.Current.HiddenCategories.ToHashSet(StringComparer.Ordinal);
        try
        {
            var vodTask = api.GetAllVodStreamsAsync(cancellationToken);
            var seriesTask = api.GetAllSeriesAsync(cancellationToken);
            await Task.WhenAll(vodTask, seriesTask).ConfigureAwait(false);

            var movies = Index.GetValue(vodTask.Result, _ => vodTask.Result.Select(s =>
            {
                var (title, year) = MediaNaming.ParseTitleYear(s.Name);
                return NewEntry(s.Name, new CatalogSearchItem(new ItemKey(ItemKind.Movie, Id: s.StreamId).Format(), s.CategoryId, title, year, NullIfEmpty(s.StreamIcon)));
            }).ToArray());
            var shows = Index.GetValue(seriesTask.Result, _ => seriesTask.Result.Select(s =>
            {
                var (title, year) = MediaNaming.ParseTitleYear(s.Name, s.ReleaseDate);
                return NewEntry(s.Name, new CatalogSearchItem(new ItemKey(ItemKind.Series, Id: s.SeriesId).Format(), s.CategoryId, title, year, NullIfEmpty(s.Cover)));
            }).ToArray());

            return new CatalogSearchResult(
                Rank(movies, "vod:", hidden, tokens, phrase, limit),
                Rank(shows, "series:", hidden, tokens, phrase, limit));
        }
        catch (XtreamNotConfiguredException)
        {
            return CatalogSearchResult.Empty;
        }
    }

    /// <summary>Lowercase (invariant) without diacritics: "Lançamentos" -> "lancamentos".</summary>
    public static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    /// <summary>Removes leading provider tags ("BR | ", "[4K] ", "4K - ", "(L) ") for ranking.</summary>
    public static string StripTags(string name)
    {
        var value = name.Trim();
        while (TagPrefixRegex().Match(value) is { Success: true, Length: > 0 } match && match.Length < value.Length)
        {
            value = value[match.Length..];
        }

        return value;
    }

    private static Entry NewEntry(string rawName, CatalogSearchItem item) =>
        new(Normalize(rawName), Normalize(StripTags(rawName)), item);

    private static List<CatalogSearchItem> Rank(Entry[] entries, string hiddenPrefix, HashSet<string> hidden, string[] tokens, string phrase, int limit) =>
        entries
            .Where(e => e.Item.CategoryId.Length > 0 && !hidden.Contains(hiddenPrefix + e.Item.CategoryId))
            .Where(e => tokens.All(t => e.Name.Contains(t, StringComparison.Ordinal)))
            .OrderBy(e => e.RankName.StartsWith(phrase, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(e => e.RankName, StringComparer.Ordinal)
            .ThenBy(e => e.Item.Name, StringComparer.InvariantCultureIgnoreCase)
            .Take(limit)
            .Select(e => e.Item)
            .ToList();

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    [GeneratedRegex(@"^(\[[^\]]*\]|\([^)]*\)|[A-Z0-9]{1,4}\s*[|:-])\s*")]
    private static partial Regex TagPrefixRegex();
}
