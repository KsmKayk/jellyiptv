using System.Globalization;

namespace JellyIptv.Browse;

public enum ItemKind
{
    VodRoot,
    SeriesRoot,
    VodCategory,
    SeriesCategory,
    Movie,
    Series,
    Season,
    Episode,
    VodAll,
    SeriesAll,
}

/// <summary>Identifies a channel item; stored by Jellyfin as BaseItem.ExternalId.</summary>
/// <param name="InAll">
/// Item lives under "Todos os filmes"/"Todas as séries" (key prefixed with "all"). A Jellyfin channel item has a
/// single ParentId and its id is derived from this key, so the same key in two folders would make the item jump
/// between them and leave the other folder empty while Jellyfin's 3 h channel cache is fresh.
/// </param>
public readonly record struct ItemKey(ItemKind Kind, string CategoryId = "", int Id = 0, int Season = 0, int EpisodeId = 0, bool InAll = false)
{
    private const string AllPrefix = "all";

    public string Format() => Kind switch
    {
        ItemKind.VodRoot => "vod",
        ItemKind.SeriesRoot => "series",
        ItemKind.VodAll => "vodall",
        ItemKind.SeriesAll => "serall",
        ItemKind.VodCategory => $"vodcat:{CategoryId}",
        ItemKind.SeriesCategory => $"sercat:{CategoryId}",
        ItemKind.Movie => Scope + Invariant($"movie:{Id}"),
        ItemKind.Series => Scope + Invariant($"show:{Id}"),
        ItemKind.Season => Scope + Invariant($"season:{Id}:{Season}"),
        ItemKind.Episode => Scope + Invariant($"episode:{Id}:{Season}:{EpisodeId}"),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
    };

    private string Scope => InAll ? AllPrefix : string.Empty;

    public static bool TryParse(string? value, out ItemKey key)
    {
        key = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (value.StartsWith(AllPrefix, StringComparison.Ordinal))
        {
            if (TryParse(value[AllPrefix.Length..], out var inner)
                && inner.Kind is ItemKind.Movie or ItemKind.Series or ItemKind.Season or ItemKind.Episode
                && !inner.InAll)
            {
                key = inner with { InAll = true };
                return true;
            }

            return false;
        }

        var p = value.Split(':');
        switch (p[0])
        {
            case "vodall" when p.Length == 1:
                key = new ItemKey(ItemKind.VodAll);
                return true;
            case "serall" when p.Length == 1:
                key = new ItemKey(ItemKind.SeriesAll);
                return true;
            case "vodcat" or "sercat" when value.Split(':', 2) is [_, { Length: > 0 } categoryId]:
                key = new ItemKey(p[0] == "vodcat" ? ItemKind.VodCategory : ItemKind.SeriesCategory, CategoryId: categoryId);
                return true;
            case "vod" when p.Length == 1:
                key = new ItemKey(ItemKind.VodRoot);
                return true;
            case "series" when p.Length == 1:
                key = new ItemKey(ItemKind.SeriesRoot);
                return true;
            case "movie" when p.Length == 2 && Int(p[1], out var movie):
                key = new ItemKey(ItemKind.Movie, Id: movie);
                return true;
            case "show" when p.Length == 2 && Int(p[1], out var show):
                key = new ItemKey(ItemKind.Series, Id: show);
                return true;
            case "season" when p.Length == 3 && Int(p[1], out var series) && Int(p[2], out var season):
                key = new ItemKey(ItemKind.Season, Id: series, Season: season);
                return true;
            case "episode" when p.Length == 4 && Int(p[1], out var s) && Int(p[2], out var n) && Int(p[3], out var e):
                key = new ItemKey(ItemKind.Episode, Id: s, Season: n, EpisodeId: e);
                return true;
            default:
                return false;
        }
    }

    private static bool Int(string s, out int value) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
