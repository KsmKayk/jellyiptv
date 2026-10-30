namespace JellyIptv.Downloads;

public enum DownloadKind
{
    Movie,
    Episode,
}

public enum DownloadState
{
    Pending,
    Downloading,
    Completed,
    Skipped,
    Failed,
    Cancelled,
}

/// <summary>One file to download. The URL is rebuilt at download time so it never stores credentials.</summary>
public sealed class DownloadJob
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DownloadKind Kind { get; set; }

    public int StreamId { get; set; }

    public string Extension { get; set; } = "mp4";

    public string Title { get; set; } = string.Empty;

    public string DestinationPath { get; set; } = string.Empty;

    public DownloadState State { get; set; }

    public long BytesDownloaded { get; set; }

    public long? TotalBytes { get; set; }

    public double BytesPerSecond { get; set; }

    public int Attempts { get; set; }

    public string? Error { get; set; }

    public DateTime? NotBeforeUtc { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DownloadJob Clone() => (DownloadJob)MemberwiseClone();
}

public static class DownloadPaths
{
    public static string Part(string destinationPath) => destinationPath + ".part";
}
