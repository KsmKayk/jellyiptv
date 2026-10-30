using System.Security.Cryptography;
using System.Text;
using JellyIptv.Xtream;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace JellyIptv.Browse;

internal static class MediaSourceFactory
{
    /// <summary>
    /// Direct play is disabled on purpose so clients don't fetch the URL themselves (they would skip the required User-Agent).
    /// Jellyfin proxies/remuxes instead. Note PlaybackInfo still exposes Path to clients (accepted risk).
    /// Direct stream is disabled too: a static remote proxy can't seek, so Jellyfin must remux/transcode to HLS.
    /// </summary>
    public static MediaSourceInfo Create(string key, string url, string container, string userAgent, bool isLive, XtreamStreamInfo? video = null, XtreamStreamInfo? audio = null) => new()
    {
        Id = new Guid(MD5.HashData(Encoding.UTF8.GetBytes(key))).ToString("N"),
        Name = "IPTV",
        Path = url,
        Protocol = MediaProtocol.Http,
        Container = container,
        IsRemote = true,
        IsInfiniteStream = isLive,
        SupportsDirectPlay = false,
        SupportsDirectStream = false,
        SupportsTranscoding = true,
        SupportsProbing = true,
        // Placeholder streams so Jellyfin doesn't ffprobe on every play (probing would also lack our User-Agent).
        // Without a codec Jellyfin transcodes; episodes get real codec info from Xtream so they can be remuxed.
        MediaStreams =
        [
            new MediaStream
            {
                Type = MediaStreamType.Video,
                Index = -1,
                Codec = NullIfEmpty(video?.CodecName),
                Width = video is { Width: > 0 } ? video.Width : null,
                Height = video is { Height: > 0 } ? video.Height : null,
                Profile = NullIfEmpty(video?.Profile),
                IsInterlaced = isLive,
            },
            new MediaStream
            {
                Type = MediaStreamType.Audio,
                Index = -1,
                Codec = NullIfEmpty(audio?.CodecName),
                Channels = audio is { Channels: > 0 } ? audio.Channels : null,
                Profile = NullIfEmpty(audio?.Profile),
            },
        ],
        RequiredHttpHeaders = new Dictionary<string, string> { ["User-Agent"] = userAgent },
    };

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
