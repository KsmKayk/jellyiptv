using System.Globalization;
using System.Xml;

namespace JellyIptv.Xtream;

public sealed record XmlTvProgramme(string ChannelId, DateTimeOffset Start, DateTimeOffset Stop, string Title, string? Description);

public static class XmlTvParser
{
    public static IReadOnlyList<XmlTvProgramme> Parse(Stream stream)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreWhitespace = true };
        var result = new List<XmlTvProgramme>();
        try
        {
            using var reader = XmlReader.Create(stream, settings);
            while (reader.ReadToFollowing("programme"))
            {
                var channel = reader.GetAttribute("channel");
                if (string.IsNullOrEmpty(channel)
                    || !TryParseTime(reader.GetAttribute("start"), out var start)
                    || !TryParseTime(reader.GetAttribute("stop"), out var stop))
                {
                    continue;
                }

                string title = string.Empty;
                string? description = null;
                try
                {
                    using var sub = reader.ReadSubtree();
                    sub.Read();
                    while (!sub.EOF)
                    {
                        if (sub.NodeType == XmlNodeType.Element && sub.Name == "title")
                        {
                            var text = sub.ReadElementContentAsString();
                            if (title.Length == 0)
                            {
                                title = text;
                            }
                        }
                        else if (sub.NodeType == XmlNodeType.Element && sub.Name == "desc")
                        {
                            description = sub.ReadElementContentAsString();
                        }
                        else
                        {
                            sub.Read();
                        }
                    }
                }
                catch (XmlException)
                {
                    // Malformed programme: skip it; if the document itself is broken the outer read fails next.
                    continue;
                }

                result.Add(new XmlTvProgramme(channel, start, stop, title, description));
            }
        }
        catch (XmlException)
        {
            // Document-level malformation: keep what was parsed so far.
        }

        return result;
    }

    /// <summary>Parses "20261029000000 -0300" (offset optional).</summary>
    internal static bool TryParseTime(string? value, out DateTimeOffset result)
    {
        result = default;
        var parts = (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0].Length < 14
            || !DateTime.TryParseExact(parts[0][..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return false;
        }

        var offsetText = parts[0].Length > 14 ? parts[0][14..] : (parts.Length > 1 ? parts[1] : null);
        var offset = TimeSpan.Zero;
        if (offsetText is not null)
        {
            var o = offsetText;
            if (o.Length != 5 || (o[0] != '+' && o[0] != '-')
                || !int.TryParse(o.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hh)
                || !int.TryParse(o.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var mm)
                || mm >= 60)
            {
                return false;
            }

            offset = new TimeSpan(hh, mm, 0);
            if (offset > TimeSpan.FromHours(14))
            {
                return false;
            }

            if (o[0] == '-')
            {
                offset = -offset;
            }
        }

        result = new DateTimeOffset(local, offset);
        return true;
    }
}
