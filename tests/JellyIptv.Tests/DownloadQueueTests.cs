using JellyIptv.Downloads;
using Xunit;

namespace JellyIptv.Tests;

public sealed class DownloadQueueTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "jellyiptv-tests", Guid.NewGuid().ToString("N"));

    private string QueuePath => Path.Combine(_dir, "queue.json");

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

    private static DownloadJob Job(string dest) => new() { Kind = DownloadKind.Episode, StreamId = 1, Title = dest, DestinationPath = dest };

    [Fact]
    public void Enqueue_persists_and_skips_active_duplicates()
    {
        var queue = new DownloadQueue(QueuePath);
        Assert.Equal(2, queue.Enqueue([Job("a"), Job("b")]));
        Assert.Equal(0, queue.Enqueue([Job("a")]));

        var reloaded = new DownloadQueue(QueuePath);
        Assert.Equal(new[] { "a", "b" }, reloaded.Snapshot().Select(j => j.DestinationPath).ToArray());
        Assert.All(reloaded.Snapshot(), j => Assert.Equal(DownloadState.Pending, j.State));
    }

    [Fact]
    public void Requeueing_finished_job_replaces_it()
    {
        var queue = new DownloadQueue(QueuePath);
        queue.Enqueue([Job("a")]);
        var id = queue.Snapshot()[0].Id;
        queue.Update(id, j => j.State = DownloadState.Failed);

        Assert.Equal(1, queue.Enqueue([Job("a")]));
        var only = Assert.Single(queue.Snapshot());
        Assert.NotEqual(id, only.Id);
        Assert.Equal(DownloadState.Pending, only.State);
    }

    [Fact]
    public void Reload_resets_downloading_to_pending()
    {
        var queue = new DownloadQueue(QueuePath);
        queue.Enqueue([Job("a")]);
        Assert.NotNull(queue.TakeNextPending(DateTime.UtcNow));
        Assert.Equal(DownloadState.Downloading, queue.Snapshot()[0].State);

        Assert.Equal(DownloadState.Pending, new DownloadQueue(QueuePath).Snapshot()[0].State);
    }

    [Fact]
    public void TakeNextPending_respects_not_before()
    {
        var queue = new DownloadQueue(QueuePath);
        queue.Enqueue([Job("a")]);
        var id = queue.Snapshot()[0].Id;
        var now = DateTime.UtcNow;
        queue.Update(id, j => j.NotBeforeUtc = now.AddMinutes(1));

        Assert.Null(queue.TakeNextPending(now));
        var taken = queue.TakeNextPending(now.AddMinutes(2));
        Assert.Equal(id, taken!.Id);
        Assert.Equal(DownloadState.Downloading, taken.State);
    }

    [Fact]
    public void Cancel_retry_and_clear()
    {
        var queue = new DownloadQueue(QueuePath);
        queue.Enqueue([Job("a"), Job("b"), Job("c")]);
        var ids = queue.Snapshot().Select(j => j.Id).ToArray();

        Assert.True(queue.Cancel(ids[0]));
        Assert.False(queue.Cancel(ids[0]));
        Assert.Equal(DownloadState.Cancelled, queue.Get(ids[0])!.State);

        queue.Update(ids[1], j => { j.State = DownloadState.Failed; j.Attempts = 4; j.Error = "x"; });
        Assert.True(queue.Retry(ids[1]));
        var retried = queue.Get(ids[1])!;
        Assert.Equal(DownloadState.Pending, retried.State);
        Assert.Equal(0, retried.Attempts);
        Assert.Null(retried.Error);
        Assert.False(queue.Retry(ids[2]));

        queue.Update(ids[2], j => j.State = DownloadState.Completed);
        Assert.Equal(2, queue.ClearFinished());
        Assert.Equal(new[] { ids[1] }, queue.Snapshot().Select(j => j.Id).ToArray());
        Assert.True(queue.HasPendingWork());
    }

    [Fact]
    public void Snapshot_returns_copies()
    {
        var queue = new DownloadQueue(QueuePath);
        queue.Enqueue([Job("a")]);
        queue.Snapshot()[0].State = DownloadState.Failed;
        Assert.Equal(DownloadState.Pending, queue.Snapshot()[0].State);
    }

    [Fact]
    public void Corrupt_file_starts_empty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(QueuePath, "{not json");
        Assert.Empty(new DownloadQueue(QueuePath).Snapshot());
        Assert.True(File.Exists(QueuePath + ".corrupt"));
    }

    [Fact]
    public void Null_entries_are_skipped_on_load()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(QueuePath, "[null, {\"DestinationPath\":\"a\",\"State\":\"Pending\"}]");
        var only = Assert.Single(new DownloadQueue(QueuePath).Snapshot());
        Assert.Equal("a", only.DestinationPath);
    }

    [Fact]
    public void TakeNextPending_honors_filter_and_rolls_back_when_save_fails()
    {
        var queue = new DownloadQueue(QueuePath);
        queue.Enqueue([Job("a"), Job("b")]);
        var ids = queue.Snapshot().Select(j => j.Id).ToArray();
        Assert.Equal(ids[1], queue.TakeNextPending(DateTime.UtcNow, id => id != ids[0])!.Id);

        Directory.Delete(_dir, recursive: true);
        File.WriteAllText(_dir, "file");
        try
        {
            Assert.ThrowsAny<IOException>(() => queue.TakeNextPending(DateTime.UtcNow));
            Assert.Equal(DownloadState.Pending, queue.Get(ids[0])!.State);
        }
        finally
        {
            File.Delete(_dir);
        }
    }
}
