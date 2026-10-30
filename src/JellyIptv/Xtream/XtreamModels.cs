using System.Text.Json.Serialization;

namespace JellyIptv.Xtream;

public sealed class XtreamCategory
{
    [JsonPropertyName("category_id")]
    public string CategoryId { get; set; } = string.Empty;

    [JsonPropertyName("category_name")]
    public string CategoryName { get; set; } = string.Empty;
}

public sealed class XtreamVodStream
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("stream_id")]
    public int StreamId { get; set; }

    [JsonPropertyName("stream_icon")]
    public string StreamIcon { get; set; } = string.Empty;

    [JsonPropertyName("rating")]
    public double Rating { get; set; }

    [JsonPropertyName("added")]
    public string Added { get; set; } = string.Empty;

    [JsonPropertyName("category_id")]
    public string CategoryId { get; set; } = string.Empty;

    [JsonPropertyName("container_extension")]
    public string ContainerExtension { get; set; } = string.Empty;
}

public sealed class XtreamSeries
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("series_id")]
    public int SeriesId { get; set; }

    [JsonPropertyName("cover")]
    public string Cover { get; set; } = string.Empty;

    [JsonPropertyName("plot")]
    public string Plot { get; set; } = string.Empty;

    [JsonPropertyName("genre")]
    public string Genre { get; set; } = string.Empty;

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; } = string.Empty;

    [JsonPropertyName("rating")]
    public double Rating { get; set; }

    [JsonPropertyName("category_id")]
    public string CategoryId { get; set; } = string.Empty;
}

public sealed class XtreamSeriesInfo
{
    [JsonPropertyName("seasons")]
    public List<XtreamSeason>? Seasons { get; set; }

    [JsonPropertyName("info")]
    [JsonConverter(typeof(ObjectOrNullConverter<XtreamSeriesDetails>))]
    public XtreamSeriesDetails? Info { get; set; }

    [JsonPropertyName("episodes")]
    [JsonConverter(typeof(EpisodesConverter))]
    public Dictionary<int, List<XtreamEpisode>> Episodes { get; set; } = new();
}

public sealed class XtreamSeriesDetails
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("cover")]
    public string Cover { get; set; } = string.Empty;

    [JsonPropertyName("plot")]
    public string Plot { get; set; } = string.Empty;

    [JsonPropertyName("genre")]
    public string Genre { get; set; } = string.Empty;

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; } = string.Empty;

    [JsonPropertyName("rating")]
    public double Rating { get; set; }
}

public sealed class XtreamSeason
{
    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("cover")]
    public string Cover { get; set; } = string.Empty;
}

public sealed class XtreamEpisode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("episode_num")]
    public int EpisodeNum { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("container_extension")]
    public string ContainerExtension { get; set; } = string.Empty;

    [JsonPropertyName("season")]
    public int Season { get; set; }

    [JsonPropertyName("added")]
    public string Added { get; set; } = string.Empty;

    [JsonPropertyName("info")]
    [JsonConverter(typeof(ObjectOrNullConverter<XtreamEpisodeInfo>))]
    public XtreamEpisodeInfo? Info { get; set; }

    /// <summary>Episode number, falling back to the position in the season list.</summary>
    public int NumberOr(int index) => EpisodeNum > 0 ? EpisodeNum : index + 1;
}

public sealed class XtreamEpisodeInfo
{
    [JsonPropertyName("plot")]
    public string Plot { get; set; } = string.Empty;

    [JsonPropertyName("movie_image")]
    public string MovieImage { get; set; } = string.Empty;

    [JsonPropertyName("duration_secs")]
    public int DurationSecs { get; set; }

    [JsonPropertyName("releasedate")]
    public string ReleaseDate { get; set; } = string.Empty;

    [JsonPropertyName("video")]
    [JsonConverter(typeof(ObjectOrNullConverter<XtreamStreamInfo>))]
    public XtreamStreamInfo? Video { get; set; }

    [JsonPropertyName("audio")]
    [JsonConverter(typeof(ObjectOrNullConverter<XtreamStreamInfo>))]
    public XtreamStreamInfo? Audio { get; set; }
}

public sealed class XtreamStreamInfo
{
    [JsonPropertyName("codec_name")]
    public string CodecName { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("channels")]
    public int Channels { get; set; }

    [JsonPropertyName("profile")]
    public string Profile { get; set; } = string.Empty;
}

public sealed class XtreamVodInfo
{
    [JsonPropertyName("info")]
    [JsonConverter(typeof(ObjectOrNullConverter<XtreamVodDetails>))]
    public XtreamVodDetails? Info { get; set; }

    [JsonPropertyName("movie_data")]
    [JsonConverter(typeof(ObjectOrNullConverter<XtreamMovieData>))]
    public XtreamMovieData? MovieData { get; set; }
}

public sealed class XtreamVodDetails
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("plot")]
    public string Plot { get; set; } = string.Empty;

    [JsonPropertyName("releasedate")]
    public string ReleaseDate { get; set; } = string.Empty;

    [JsonPropertyName("duration_secs")]
    public int DurationSecs { get; set; }
}

public sealed class XtreamMovieData
{
    [JsonPropertyName("stream_id")]
    public int StreamId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("container_extension")]
    public string ContainerExtension { get; set; } = string.Empty;
}

public sealed class XtreamLiveStream
{
    [JsonPropertyName("num")]
    public int Num { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("stream_id")]
    public int StreamId { get; set; }

    [JsonPropertyName("stream_icon")]
    public string StreamIcon { get; set; } = string.Empty;

    [JsonPropertyName("epg_channel_id")]
    public string EpgChannelId { get; set; } = string.Empty;

    [JsonPropertyName("category_id")]
    public string CategoryId { get; set; } = string.Empty;
}

public sealed class XtreamAuthResponse
{
    [JsonPropertyName("user_info")]
    [JsonConverter(typeof(ObjectOrNullConverter<XtreamUserInfo>))]
    public XtreamUserInfo? UserInfo { get; set; }
}

public sealed class XtreamUserInfo
{
    [JsonPropertyName("auth")]
    public int Auth { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("exp_date")]
    public string ExpDate { get; set; } = string.Empty;

    [JsonPropertyName("max_connections")]
    public int MaxConnections { get; set; }
}
