using System.Security.Cryptography;
using System.Text;
using MediaBrowser.Model.Plugins;

namespace JellyIptv.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string BaseUrl { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string MoviesFolder { get; set; } = "/data/movies";

    public string SeriesFolder { get; set; } = "/data/tv";

    public int MaxConcurrentDownloads { get; set; } = 1;

    public string UserAgent { get; set; } = "VLC/3.0.18 LibVLC/3.0.18";

    /// <summary>Category ids prefixed by type: "vod:813", "series:691", "live:77".</summary>
    public string[] HiddenCategories { get; set; } = [];

    public bool AllowNonAdminDownloads { get; set; }

    public bool PauseWhileStreaming { get; set; } = true;

    /// <summary>Bumped when credentials or User-Agent change, so Jellyfin re-saves the stored media sources (via DateModified).</summary>
    public DateTime SourcesChangedUtc { get; set; }

    public static void StampSourceChanges(PluginConfiguration previous, PluginConfiguration next, DateTime nowUtc)
    {
        var changed = previous.BaseUrl != next.BaseUrl
            || previous.Username != next.Username
            || previous.Password != next.Password
            || previous.UserAgent != next.UserAgent;
        next.SourcesChangedUtc = changed ? nowUtc : previous.SourcesChangedUtc;
    }

    /// <summary>Changes whenever a setting that affects browsable content changes.</summary>
    public string ContentVersion()
    {
        var hidden = string.Join(',', HiddenCategories.Order(StringComparer.Ordinal));
        var raw = string.Join('|', BaseUrl, Username, Password, UserAgent, hidden);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16];
    }
}
