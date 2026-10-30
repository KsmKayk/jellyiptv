using System.Net;
using System.Net.Http.Headers;
using JellyIptv.Downloads;
using JellyIptv.Tests.TestSupport;
using Xunit;

namespace JellyIptv.Tests;

public sealed class HttpFileDownloaderTests : IDisposable
{
    private static readonly byte[] Content = Enumerable.Range(0, 1000).Select(i => (byte)(i % 251)).ToArray();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "jellyiptv-tests", Guid.NewGuid().ToString("N"));
    private readonly List<DownloadProgress> _reports = [];

    private string Dest => Path.Combine(_dir, "Show", "Season 01", "ep.mp4");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private Task Download(StubHandler handler, TimeSpan? readTimeout = null) =>
        new HttpFileDownloader(new FakeHttpClientFactory(handler)) { ReadTimeout = readTimeout ?? TimeSpan.FromSeconds(60) }.DownloadAsync(
            "http://x/series/u/p/1.mp4", "UA-Test", Dest, new SyncProgress<DownloadProgress>(_reports.Add), CancellationToken.None);

    private static HttpResponseMessage Full() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Content) };

    private void WritePart(int bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Dest)!);
        File.WriteAllBytes(DownloadPaths.Part(Dest), Content[..bytes]);
    }

    [Fact]
    public async Task Downloads_full_file_with_user_agent()
    {
        var handler = new StubHandler(_ => Full());

        await Download(handler);

        Assert.Equal(Content, File.ReadAllBytes(Dest));
        Assert.False(File.Exists(DownloadPaths.Part(Dest)));
        Assert.Equal("UA-Test", handler.Requests[0].Headers.UserAgent.ToString());
        Assert.Null(handler.Requests[0].Headers.Range);
        Assert.Equal(new DownloadProgress(1000, 1000, 0), _reports[^1]);
    }

    [Fact]
    public async Task Resumes_partial_file_with_range()
    {
        WritePart(400);
        var handler = new StubHandler(req =>
        {
            Assert.Equal("bytes=400-", req.Headers.Range!.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(Content[400..]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(400, 999, 1000);
            return response;
        });

        await Download(handler);

        Assert.Equal(Content, File.ReadAllBytes(Dest));
        Assert.Equal(1000, _reports[^1].TotalBytes);
    }

    [Fact]
    public async Task Restarts_when_server_ignores_range()
    {
        WritePart(400);

        await Download(new StubHandler(_ => Full()));

        Assert.Equal(Content, File.ReadAllBytes(Dest));
        Assert.Equal(new DownloadProgress(1000, 1000, 0), _reports[^1]);
    }

    [Fact]
    public async Task Wrong_content_range_start_restarts_from_scratch()
    {
        WritePart(400);
        var handler = new StubHandler(req =>
        {
            if (req.Headers.Range is null)
            {
                return Full();
            }

            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(Content[100..]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(100, 999, 1000);
            return response;
        });

        await Download(handler);

        Assert.Equal(Content, File.ReadAllBytes(Dest));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Partial_content_without_content_range_keeps_resume()
    {
        WritePart(400);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(Content[400..]) });

        await Download(handler);

        Assert.Equal(Content, File.ReadAllBytes(Dest));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Range_not_satisfiable_with_mismatched_length_restarts()
    {
        WritePart(600);
        var handler = new StubHandler(req =>
        {
            if (req.Headers.Range is null)
            {
                return Full();
            }

            var response = new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable) { Content = new ByteArrayContent([]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(1000);
            return response;
        });

        await Download(handler);

        Assert.Equal(Content, File.ReadAllBytes(Dest));
    }

    [Fact]
    public async Task Stalled_stream_times_out_and_keeps_part()
    {
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StallingContent(Content[..100]) });

        var ex = await Assert.ThrowsAsync<IOException>(() => Download(handler, TimeSpan.FromMilliseconds(200)));

        Assert.Contains("Sem dados", ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Dest));
        Assert.Equal(100, new FileInfo(DownloadPaths.Part(Dest)).Length);
    }

    private sealed class StallingContent(byte[] first) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();

        protected override bool TryComputeLength(out long length)
        {
            length = 1000;
            return true;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new StallingStream(first));
    }

    private sealed class StallingStream(byte[] first) : Stream
    {
        private bool _served;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_served)
            {
                _served = true;
                first.CopyTo(buffer);
                return first.Length;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Range_not_satisfiable_means_part_is_complete()
    {
        WritePart(1000);

        await Download(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable)));

        Assert.Equal(Content, File.ReadAllBytes(Dest));
    }

    [Fact]
    public async Task Http_error_throws_and_creates_no_file()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => Download(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));
        Assert.False(File.Exists(Dest));
    }

    [Fact]
    public async Task Short_body_throws_and_keeps_part()
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Content[..500]) };
            response.Content.Headers.ContentLength = 1000;
            return response;
        });

        await Assert.ThrowsAsync<IOException>(() => Download(handler));
        Assert.False(File.Exists(Dest));
        Assert.Equal(500, new FileInfo(DownloadPaths.Part(Dest)).Length);
    }
}
