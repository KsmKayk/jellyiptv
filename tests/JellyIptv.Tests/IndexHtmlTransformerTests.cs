using JellyIptv.Web;
using Xunit;

namespace JellyIptv.Tests;

public class IndexHtmlTransformerTests
{
    [Fact]
    public void Inserts_script_before_body_end_once()
    {
        var html = "<html><body><div></div></body></html>";

        var once = IndexHtmlTransformer.Transform(new TransformPayload { Contents = html });
        var twice = IndexHtmlTransformer.Transform(new TransformPayload { Contents = once });

        Assert.Equal("<html><body><div></div>" + IndexHtmlTransformer.ScriptTag + "</body></html>", once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Appends_when_no_body_tag()
    {
        Assert.Equal("x" + IndexHtmlTransformer.ScriptTag, IndexHtmlTransformer.Transform(new TransformPayload { Contents = "x" }));
        Assert.Equal(IndexHtmlTransformer.ScriptTag, IndexHtmlTransformer.Transform(new TransformPayload()));
    }

    [Fact]
    public void Client_script_is_embedded()
    {
        using var stream = typeof(IndexHtmlTransformer).Assembly.GetManifestResourceStream("JellyIptv.Web.jellyiptv.js");
        Assert.NotNull(stream);
    }
}
