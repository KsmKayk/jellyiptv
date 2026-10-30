using JellyIptv.Api;
using JellyIptv.Downloads;
using Xunit;

namespace JellyIptv.Tests;

public class DtosTests
{
    [Fact]
    public void Queue_item_uses_string_enums_and_hides_internal_fields()
    {
        var job = new DownloadJob { Kind = DownloadKind.Episode, State = DownloadState.Downloading, Title = "T", BytesDownloaded = 5, TotalBytes = 10, BytesPerSecond = 2.5, Attempts = 1, Error = "e", DestinationPath = "/tv/x.mp4" };

        var dto = QueueItemDto.From(job);

        Assert.Equal(job.Id, dto.Id);
        Assert.Equal("Episode", dto.Kind);
        Assert.Equal("Downloading", dto.State);
        Assert.Equal(5, dto.BytesDownloaded);
        Assert.Equal(10, dto.TotalBytes);
        Assert.Equal("/tv/x.mp4", dto.DestinationPath);
        Assert.Null(typeof(QueueItemDto).GetProperty("StreamId"));
    }

    [Fact]
    public void Test_connection_message_formats_expiry()
    {
        var response = TestConnectionResponse.FromUserInfo(new JellyIptv.Xtream.XtreamUserInfo { Auth = 1, MaxConnections = 1, ExpDate = "1793933940", Status = "Active" });
        Assert.True(response.Ok);
        Assert.Equal(1, response.MaxConnections);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793933940).UtcDateTime, response.ExpiresUtc);
        Assert.Contains("1 conexão", response.Message, StringComparison.Ordinal);

        Assert.False(TestConnectionResponse.FromUserInfo(new JellyIptv.Xtream.XtreamUserInfo { Auth = 0 }).Ok);
        Assert.False(TestConnectionResponse.FromUserInfo(null).Ok);
    }
}
