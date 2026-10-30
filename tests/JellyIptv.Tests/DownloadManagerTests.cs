using JellyIptv.Downloads;
using JellyIptv.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyIptv.Tests;

public sealed class DownloadManagerTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "jellyiptv-tests", Guid.NewGuid().ToString("N"));
    private readonly FixedConfig _config = new();
    private readonly FakeDownloader _downloader = new();
    private readonly FakeScanTrigger _scan = new();
    private DownloadQueue _queue = null!;
    private DownloadManager _manager = null!;

    public async Task InitializeAsync()
    {
        _queue = new DownloadQueue(Path.Combine(_dir, "queue.json"));
        _manager = new DownloadManager(_queue, _downloader, new FakeXtreamApi(), _config, _scan, NullLogger<DownloadManager>.Instance)
        {
            RetryDelays = [TimeSpan.Zero, TimeSpan.Zero],
        };
        await _manager.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _manager.StopAsync(CancellationToken.None);
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private DownloadJob Job(string name, DownloadKind kind = DownloadKind.Episode) =>
        new() { Kind = kind, StreamId = 71999, Extension = "mp4", Title = name, DestinationPath = Path.Combine(_dir, "out", name + ".mp4") };

    private DownloadState StateOf(Guid id) => _queue.Get(id)!.State;

    [Fact]
    public async Task Completes_job_and_triggers_one_scan()
    {
        _manager.Enqueue([Job("a"), Job("b", DownloadKind.Movie)]);
        var ids = _queue.Snapshot().Select(j => j.Id).ToArray();

        await Eventually.True(() => ids.All(id => StateOf(id) == DownloadState.Completed), "both completed");
        await Eventually.True(() => _scan.Count == 1, "scan triggered");
        Assert.Contains("http://example.com:8880/series/user/pass/71999.mp4", _downloader.Urls);
        Assert.Contains("http://example.com:8880/movie/user/pass/71999.mp4", _downloader.Urls);
        await Task.Delay(100);
        Assert.Equal(1, _scan.Count);
    }

    [Fact]
    public async Task Skips_existing_files()
    {
        var job = Job("exists");
        Directory.CreateDirectory(Path.GetDirectoryName(job.DestinationPath)!);
        File.WriteAllText(job.DestinationPath, "old");

        _manager.Enqueue([job]);

        await Eventually.True(() => StateOf(job.Id) == DownloadState.Skipped, "skipped");
        Assert.Equal(0, _downloader.Calls);
    }

    [Fact]
    public async Task Retries_then_fails()
    {
        _downloader.Behavior = (_, _, _) => throw new InvalidOperationException("boom");
        var job = Job("bad");

        _manager.Enqueue([job]);

        await Eventually.True(() => StateOf(job.Id) == DownloadState.Failed, "failed");
        var final = _queue.Get(job.Id)!;
        Assert.Equal(3, final.Attempts);
        Assert.Equal("boom", final.Error);
        Assert.Equal(3, _downloader.Calls);
    }

    [Fact]
    public async Task Pause_interrupts_without_counting_attempt_and_resume_restarts()
    {
        _downloader.Behavior = async (call, dest, ct) =>
        {
            if (call == 1)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            File.WriteAllText(dest, "x");
        };
        var job = Job("paused");
        _manager.Enqueue([job]);
        await Eventually.True(() => _downloader.Calls == 1 && StateOf(job.Id) == DownloadState.Downloading, "downloading");

        _manager.SetPaused(true);

        await Eventually.True(() => StateOf(job.Id) == DownloadState.Pending, "back to pending");
        Assert.True(_manager.IsPaused);
        Assert.Equal(0, _queue.Get(job.Id)!.Attempts);
        await Task.Delay(150);
        Assert.Equal(1, _downloader.Calls);

        _manager.SetPaused(false);

        await Eventually.True(() => StateOf(job.Id) == DownloadState.Completed, "completed after resume");
        Assert.Equal(2, _downloader.Calls);
    }

    [Fact]
    public async Task Cancel_stops_active_download()
    {
        _downloader.Behavior = (_, _, ct) => Task.Delay(Timeout.Infinite, ct);
        var job = Job("cancel");
        _manager.Enqueue([job]);
        await Eventually.True(() => StateOf(job.Id) == DownloadState.Downloading, "downloading");

        Assert.True(_manager.Cancel(job.Id));

        await Eventually.True(() => StateOf(job.Id) == DownloadState.Cancelled, "cancelled");
        await Task.Delay(150);
        Assert.Equal(DownloadState.Cancelled, StateOf(job.Id));
        Assert.Equal(1, _downloader.Calls);
    }

    [Fact]
    public async Task Respects_max_concurrency()
    {
        var gate = new TaskCompletionSource();
        _downloader.Behavior = async (_, dest, _) =>
        {
            await gate.Task;
            File.WriteAllText(dest, "x");
        };
        _manager.Enqueue([Job("one"), Job("two")]);

        await Eventually.True(() => _queue.Snapshot().Count(j => j.State == DownloadState.Downloading) == 1, "one downloading");
        await Task.Delay(150);
        Assert.Equal(1, _queue.Snapshot().Count(j => j.State == DownloadState.Downloading));
        Assert.Equal(1, _queue.Snapshot().Count(j => j.State == DownloadState.Pending));

        gate.SetResult();

        await Eventually.True(() => _queue.Snapshot().All(j => j.State == DownloadState.Completed), "all completed");
    }

    [Fact]
    public async Task Interrupted_job_is_not_started_twice_and_stays_cancellable()
    {
        _config.Current.MaxConcurrentDownloads = 2;
        var concurrent = 0;
        var maxConcurrent = 0;
        _downloader.Behavior = async (_, _, ct) =>
        {
            var now = Interlocked.Increment(ref concurrent);
            int seen;
            while (now > (seen = Volatile.Read(ref maxConcurrent)) && Interlocked.CompareExchange(ref maxConcurrent, now, seen) != seen)
            {
            }

            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            finally
            {
                await Task.Delay(2);
                Interlocked.Decrement(ref concurrent);
            }
        };
        var first = Job("one");
        var second = Job("two");
        _manager.Enqueue([first, second]);
        await Eventually.True(() => Volatile.Read(ref concurrent) == 2, "two running");

        for (var i = 0; i < 100; i++)
        {
            _manager.SetPaused(true);
            await Task.Delay(1);
            _manager.SetPaused(false);
            await Task.Delay(1);
        }

        var third = Job("three");
        _manager.Enqueue([third]);
        await Task.Delay(200);
        Assert.True(_queue.Snapshot().Count(j => j.State == DownloadState.Downloading) <= 2);
        Assert.True(Volatile.Read(ref maxConcurrent) <= 2, "max concurrency " + maxConcurrent);

        foreach (var id in new[] { first.Id, second.Id, third.Id })
        {
            _manager.Cancel(id);
        }

        await Eventually.True(() => _queue.Snapshot().All(j => j.State == DownloadState.Cancelled), "all cancelled");
        await Eventually.True(() => Volatile.Read(ref concurrent) == 0, "no downloader left running");
    }

    [Fact]
    public async Task Queue_save_failure_does_not_kill_the_loop()
    {
        var dir = Path.Combine(_dir, "q");
        var queue = new DownloadQueue(Path.Combine(dir, "queue.json"));
        await using var scope = new ManagerScope(queue, _downloader, _config, _scan);
        scope.Manager.SetPaused(true);
        var job = Job("later");
        scope.Manager.Enqueue([job]);
        Directory.Delete(dir, recursive: true);
        File.WriteAllText(dir, "not a directory");

        scope.Manager.SetPaused(false);
        await Task.Delay(300);
        Assert.Equal(DownloadState.Pending, queue.Get(job.Id)!.State);
        Assert.Equal(0, _downloader.Calls);

        File.Delete(dir);
        Directory.CreateDirectory(dir);
        scope.Manager.Retry(Guid.Empty);

        await Eventually.True(() => queue.Get(job.Id)!.State == DownloadState.Completed, "completed after save recovers");
    }

    [Fact]
    public async Task Cancel_wins_over_a_download_that_finishes_anyway()
    {
        var gate = new TaskCompletionSource();
        var finished = new TaskCompletionSource();
        _downloader.Behavior = async (_, dest, _) =>
        {
            await gate.Task;
            File.WriteAllText(dest, "x");
            finished.SetResult();
        };
        var job = Job("race");
        _manager.Enqueue([job]);
        await Eventually.True(() => StateOf(job.Id) == DownloadState.Downloading, "downloading");

        Assert.True(_manager.Cancel(job.Id));
        gate.SetResult();
        await finished.Task;
        await Task.Delay(200);

        Assert.Equal(DownloadState.Cancelled, StateOf(job.Id));
        Assert.Equal(0, _scan.Count);
    }

    [Fact]
    public async Task Cancel_deletes_part_of_active_job()
    {
        _downloader.Behavior = async (_, dest, ct) =>
        {
            File.WriteAllText(DownloadPaths.Part(dest), "partial");
            await Task.Delay(Timeout.Infinite, ct);
        };
        var job = Job("activepart");
        _manager.Enqueue([job]);
        await Eventually.True(() => File.Exists(DownloadPaths.Part(job.DestinationPath)), "part written");

        _manager.Cancel(job.Id);

        await Eventually.True(() => StateOf(job.Id) == DownloadState.Cancelled && !File.Exists(DownloadPaths.Part(job.DestinationPath)), "part deleted");
    }

    [Fact]
    public void Cancel_deletes_part_of_non_active_job()
    {
        _manager.SetPaused(true);
        var job = Job("pendingpart");
        _manager.Enqueue([job]);
        Directory.CreateDirectory(Path.GetDirectoryName(job.DestinationPath)!);
        File.WriteAllText(DownloadPaths.Part(job.DestinationPath), "partial");

        Assert.True(_manager.Cancel(job.Id));

        Assert.False(File.Exists(DownloadPaths.Part(job.DestinationPath)));
    }

    [Fact]
    public async Task Shutdown_waits_for_running_jobs()
    {
        var exited = false;
        _downloader.Behavior = async (_, _, _) =>
        {
            await Task.Delay(300);
            exited = true;
        };
        _manager.Enqueue([Job("shutdown")]);
        await Eventually.True(() => _downloader.Calls == 1, "started");

        await _manager.StopAsync(CancellationToken.None);

        Assert.True(Volatile.Read(ref exited));
    }

    private sealed class ManagerScope : IAsyncDisposable
    {
        public ManagerScope(DownloadQueue queue, FakeDownloader downloader, FixedConfig config, FakeScanTrigger scan)
        {
            Manager = new DownloadManager(queue, downloader, new FakeXtreamApi(), config, scan, NullLogger<DownloadManager>.Instance)
            {
                RetryDelays = [TimeSpan.Zero],
            };
            Manager.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public DownloadManager Manager { get; }

        public async ValueTask DisposeAsync() => await Manager.StopAsync(CancellationToken.None);
    }
}
