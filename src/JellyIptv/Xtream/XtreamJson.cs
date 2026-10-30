using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JellyIptv.Xtream;

/// <summary>Xtream panels are inconsistent: numbers come as strings, objects as [] and so on.</summary>
public static class XtreamJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new LenientIntConverter(), new LenientDoubleConverter(), new LenientStringConverter() },
    };
}

public sealed class LenientIntConverter : JsonConverter<int>
{
    public override bool HandleNull => true;

    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetInt32(out var i) ? i : (int)Math.Clamp(reader.GetDouble(), int.MinValue, int.MaxValue);
            case JsonTokenType.String:
                var s = reader.GetString();
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                {
                    return v;
                }

                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Clamp(d, int.MinValue, int.MaxValue) : 0;
            case JsonTokenType.True:
                return 1;
            case JsonTokenType.StartObject or JsonTokenType.StartArray:
                reader.TrySkip();
                return 0;
            default:
                return 0;
        }
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

public sealed class LenientDoubleConverter : JsonConverter<double>
{
    public override bool HandleNull => true;

    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetDouble();
            case JsonTokenType.String:
                return double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
            case JsonTokenType.StartObject or JsonTokenType.StartArray:
                reader.TrySkip();
                return 0;
            default:
                return 0;
        }
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

public sealed class LenientStringConverter : JsonConverter<string>
{
    public override bool HandleNull => true;

    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString() ?? string.Empty;
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var l)
                    ? l.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString(CultureInfo.InvariantCulture);
            case JsonTokenType.True:
                return "true";
            case JsonTokenType.False:
                return "false";
            case JsonTokenType.StartObject or JsonTokenType.StartArray:
                reader.TrySkip();
                return string.Empty;
            default:
                return string.Empty;
        }
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

/// <summary>Reads an object; anything else (e.g. an empty array) becomes null.</summary>
public sealed class ObjectOrNullConverter<T> : JsonConverter<T?>
    where T : class
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            return JsonSerializer.Deserialize<T>(ref reader, options);
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            reader.TrySkip();
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options) => JsonSerializer.Serialize(writer, value, options);
}

/// <summary>Accepts {"1":[...]} or [[...]] / [...] and groups episodes by season.</summary>
public sealed class EpisodesConverter : JsonConverter<Dictionary<int, List<XtreamEpisode>>>
{
    public override bool HandleNull => true;

    public override Dictionary<int, List<XtreamEpisode>> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new Dictionary<int, List<XtreamEpisode>>();
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var season))
                {
                    AddAll(result, prop.Value, season, options);
                }
            }
        }
        else if (reader.TokenType == JsonTokenType.StartArray)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            AddAll(result, doc.RootElement, null, options);
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<int, List<XtreamEpisode>> value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, options);

    private static void AddAll(Dictionary<int, List<XtreamEpisode>> result, JsonElement element, int? season, JsonSerializerOptions options)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                AddAll(result, child, season, options);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var episode = element.Deserialize<XtreamEpisode>(options);
        if (episode is null)
        {
            return;
        }

        var key = season ?? episode.Season;
        if (!result.TryGetValue(key, out var list))
        {
            result[key] = list = [];
        }

        list.Add(episode);
    }
}
