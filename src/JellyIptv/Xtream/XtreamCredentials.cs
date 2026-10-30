using JellyIptv.Configuration;

namespace JellyIptv.Xtream;

public sealed record XtreamCredentials(string BaseUrl, string Username, string Password, string UserAgent)
{
    public const string DefaultUserAgent = "VLC/3.0.18 LibVLC/3.0.18";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("BaseUrl = ").Append(BaseUrl).Append(", Username = ").Append(Username).Append(", UserAgent = ").Append(UserAgent);
        return true;
    }

    public string CacheKey => $"{BaseUrl}|{Username}|{Password}";

    public static XtreamCredentials From(PluginConfiguration config) => new(
        NormalizeBaseUrl(config.BaseUrl),
        config.Username.Trim(),
        config.Password,
        string.IsNullOrWhiteSpace(config.UserAgent) ? DefaultUserAgent : config.UserAgent.Trim());

    private static string NormalizeBaseUrl(string url)
    {
        // Pasted URLs often carry stray spaces/tabs/newlines; a URL never contains whitespace.
        url = new string(url.Where(c => !char.IsWhiteSpace(c)).ToArray()).TrimEnd('/');
        if (url.Length > 0 && !url.Contains("://", StringComparison.Ordinal))
        {
            url = "http://" + url;
        }

        return url;
    }
}

public sealed class XtreamUrls(XtreamCredentials credentials)
{
    private string Base => credentials.BaseUrl.Trim().TrimEnd('/');

    private string User => Uri.EscapeDataString(credentials.Username);

    private string Pass => Uri.EscapeDataString(credentials.Password);

    public string PlayerApi(string? action, string? query = null)
    {
        var url = $"{Base}/player_api.php?username={User}&password={Pass}";
        if (!string.IsNullOrEmpty(action))
        {
            url += $"&action={action}";
        }

        if (!string.IsNullOrEmpty(query))
        {
            url += "&" + query;
        }

        return url;
    }

    public string XmlTv() => $"{Base}/xmltv.php?username={User}&password={Pass}";

    public string Movie(int streamId, string extension) => $"{Base}/movie/{User}/{Pass}/{streamId}.{extension}";

    public string Episode(int episodeId, string extension) => $"{Base}/series/{User}/{Pass}/{episodeId}.{extension}";

    public string Live(int streamId) => $"{Base}/live/{User}/{Pass}/{streamId}.ts";
}
