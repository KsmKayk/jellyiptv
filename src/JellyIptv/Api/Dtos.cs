using System.Globalization;
using JellyIptv.Downloads;
using JellyIptv.Xtream;

namespace JellyIptv.Api;

public sealed record ItemInfoResponse(string? Kind, bool CanDownload);

public sealed class DownloadRequest
{
    public Guid ItemId { get; set; }
}

public sealed record DownloadResponse(int Added, int Total);

public sealed record ErrorResponse(string Message);

public sealed record CategoryDto(string Id, string Name);

public sealed record CategoriesResponse(IReadOnlyList<CategoryDto> Vod, IReadOnlyList<CategoryDto> Series, IReadOnlyList<CategoryDto> Live);

public sealed record QueueResponse(bool Paused, IReadOnlyList<QueueItemDto> Items);

public sealed record QueueItemDto(
    Guid Id,
    string Title,
    string Kind,
    string State,
    long BytesDownloaded,
    long? TotalBytes,
    double BytesPerSecond,
    int Attempts,
    string? Error,
    string DestinationPath,
    DateTime CreatedUtc)
{
    public static QueueItemDto From(DownloadJob job) => new(
        job.Id,
        job.Title,
        job.Kind.ToString(),
        job.State.ToString(),
        job.BytesDownloaded,
        job.TotalBytes,
        job.BytesPerSecond,
        job.Attempts,
        job.Error,
        job.DestinationPath,
        job.CreatedUtc);
}

public sealed class TestConnectionRequest
{
    public string BaseUrl { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string UserAgent { get; set; } = string.Empty;
}

public sealed record TestConnectionResponse(bool Ok, string Message, int? MaxConnections, DateTime? ExpiresUtc)
{
    public static TestConnectionResponse FromUserInfo(XtreamUserInfo? info)
    {
        if (info is null || info.Auth != 1)
        {
            return new TestConnectionResponse(false, "Credenciais recusadas pelo servidor Xtream.", null, null);
        }

        DateTime? expires = long.TryParse(info.ExpDate, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;
        var connections = info.MaxConnections == 1 ? "1 conexão simultânea" : $"{info.MaxConnections} conexões simultâneas";
        var expiry = expires is null ? string.Empty : $" Expira em {expires:dd/MM/yyyy}.";
        return new TestConnectionResponse(true, $"Conectado ({info.Status}). {connections}.{expiry}", info.MaxConnections, expires);
    }
}
