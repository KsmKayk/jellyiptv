using System.Text;
using JellyIptv.Tests.TestSupport;
using JellyIptv.Xtream;
using Xunit;

namespace JellyIptv.Tests;

public class XmlTvParserTests
{
    [Fact]
    public void Parses_fixture_programmes()
    {
        using var stream = Fixtures.Open("epg.xml");
        var programmes = XmlTvParser.Parse(stream);

        Assert.Equal(4, programmes.Count);
        var first = programmes[0];
        Assert.Equal("Ben 10 24H", first.ChannelId);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.FromHours(-3)), first.Start);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 23, 59, 0, TimeSpan.FromHours(-3)), first.Stop);
        Assert.Equal("Canal 24H - Seu Servidor Favorito.", first.Title);
        Assert.Equal("Esse canal passa todos os dias o mesmo conteudo.", first.Description);
    }

    [Fact]
    public void Skips_out_of_range_offsets_and_accepts_glued_offset()
    {
        const string xml = """
            <tv>
            <programme start="20261029000000 +1500" stop="20261029010000 +0000" channel="a"><title>x</title></programme>
            <programme start="20261029000000 +0075" stop="20261029010000 +0000" channel="a"><title>x</title></programme>
            <programme start="20261029000000+0300" stop="20261029010000+0300" channel="b"><title>ok</title></programme>
            </tv>
            """;
        var programmes = XmlTvParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

        var p = Assert.Single(programmes);
        Assert.Equal("b", p.ChannelId);
        Assert.Equal(TimeSpan.FromHours(3), p.Start.Offset);
    }

    [Fact]
    public void Malformed_document_keeps_earlier_programmes()
    {
        const string xml = """
            <tv>
            <programme start="20261029000000 +0000" stop="20261029010000 +0000" channel="a"><title>one</title></programme>
            <programme start="20261029010000 +0000" stop="20261029020000 +0000" channel="a"><title>Tom & Jerry</title></programme>
            </tv>
            """;
        var programmes = XmlTvParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

        var p = Assert.Single(programmes);
        Assert.Equal("one", p.Title);
    }

    [Fact]
    public void Keeps_first_title()
    {
        const string xml = """
            <tv><programme start="20261029000000 +0000" stop="20261029010000 +0000" channel="a"><title>first</title><title lang="en">second</title></programme></tv>
            """;
        var p = Assert.Single(XmlTvParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
        Assert.Equal("first", p.Title);
    }

    [Fact]
    public void Ignores_doctype_and_skips_invalid_times()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8" ?><!DOCTYPE tv SYSTEM "xmltv.dtd"><tv>
            <programme start="bad" stop="20261029010000 +0000" channel="a"><title>x</title></programme>
            <programme start="20261029000000 +0130" stop="20261029010000 +0130" channel="b"><title>ok</title></programme>
            </tv>
            """;
        var programmes = XmlTvParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

        var p = Assert.Single(programmes);
        Assert.Equal("b", p.ChannelId);
        Assert.Equal(TimeSpan.FromMinutes(90), p.Start.Offset);
        Assert.Null(p.Description);
    }
}
