using JellyIptv.Configuration;
using JellyIptv.Xtream;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellyIptv.Downloads;

public sealed class DownloadManager(
    DownloadQueue queue,
    IFileDownloader downloader,
    IXtreamApi api,
    IConfigProvider config,
    ILibraryScanTrigger scanTrigger,
    ILogger<DownloadManager> logger) : BackgroundService
{
    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _lock = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _active = [];
    private readonly Dictionary<Guid, Task> _running = [];
    private bool _paused;
    private bool _completedSinceScan;

    internal TimeSpan[] RetryDelays { get; init; } = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90)];

    public bool IsPaused
    {
        get
        {
            lock (_lock)
            {
                return _paused;
            }
        }
    }

    public int Enqueue(IEnumerable<DownloadJob> jobs)
    {
        var added = queue.Enqueue(jobs);
        Wake();
        return added;
    }

    public IReadOnlyList<DownloadJob> Snapshot() => queue.Snapshot();

    public bool Cancel(Guid id)
    {
        lock (_lock)
        {
            var cancelled = queue.Cancel(id);
            if (_active.TryGetValue(id, out var cts))
            {
                // The running task deletes the .part once the downloader has stopped.
                cts.Cancel();
            }
            else if (cancelled && queue.Get(id) is { } job)
            {
                TryDelete(DownloadPaths.Part(job.DestinationPath));
            }

            return cancelled;
        }
    }

    public bool Retry(Guid id)
    {
        var retried = queue.Retry(id);
        Wake();
        return retried;
    }

    public int ClearFinished() => queue.ClearFinished();

    /// <summary>Paused jobs are interrupted (their .part is kept) and go back to Pending.</summary>
    public void SetPaused(bool paused)
    {
        lock (_lock)
        {
            if (_paused == paused)
            {
                return;
            }

            _paused = paused;
            if (paused)
            {
                foreach (var cts in _active.Values)
                {
                    cts.Cancel();
                }
            }
        }

        logger.LogInformation("JellyIPTV: downloads {State}", paused ? "pausados (reprodução IPTV ativa)" : "retomados");
        Wake();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                StartAvailable(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never let this escape: an unhandled BackgroundService exception stops the whole Jellyfin host.
                logger.LogError(ex, "JellyIPTV: falha ao iniciar downloads");
            }

            try
            {
                await _signal.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Task[] running;
        lock (_lock)
        {
            foreach (var cts in _active.Values)
            {
                cts.Cancel();
            }

            running = [.. _running.Values];
        }

        try
        {
            await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Timeouts and job failures are already handled/logged per job.
        }
    }

    private void Wake() => _signal.Release();

    private void StartAvailable(CancellationToken stoppingToken)
    {
        lock (_lock)
        {
            var max = Math.Max(1, config.Current.MaxConcurrentDownloads);
            while (!_paused && _active.Count < max)
            {
                var job = queue.TakeNextPending(DateTime.UtcNow, id => !_active.ContainsKey(id));
                if (job is null)
                {
                    break;
                }

                var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                _active[job.Id] = cts;
                _running[job.Id] = Task.Run(() => RunAsync(job, cts), CancellationToken.None);
            }
        }
    }

    private async Task RunAsync(DownloadJob job, CancellationTokenSource cts)
    {
        try
        {
            if (File.Exists(job.DestinationPath))
            {
                SafeUpdate(job.Id, j =>
                {
                    if (j.State != DownloadState.Cancelled)
                    {
                        j.State = DownloadState.Skipped;
                    }
                });
                return;
            }

            var urls = api.Urls;
            var url = job.Kind == DownloadKind.Movie ? urls.Movie(job.StreamId, job.Extension) : urls.Episode(job.StreamId, job.Extension);
            var progress = new SyncProgress<DownloadProgress>(p => SafeUpdate(
                job.Id,
                j =>
                {
                    j.BytesDownloaded = p.BytesDownloaded;
                    j.TotalBytes = p.TotalBytes;
                    j.BytesPerSecond = p.BytesPerSecond;
                },
                persist: false));

            var userAgent = XtreamCredentials.From(config.Current).UserAgent;
            await downloader.DownloadAsync(url, userAgent, job.DestinationPath, progress, cts.Token).ConfigureAwait(false);

            var completed = false;
            SafeUpdate(job.Id, j =>
            {
                if (j.State == DownloadState.Cancelled)
                {
                    return;
                }

                completed = true;
                j.State = DownloadState.Completed;
                j.Error = null;
                j.BytesPerSecond = 0;
            });
            if (completed)
            {
                lock (_lock)
                {
                    _completedSinceScan = true;
                }

                logger.LogInformation("JellyIPTV: concluído {Title}", job.Title);
            }
        }
        catch (Exception) when (cts.IsCancellationRequested)
        {
            // Cancelled by the user (state already Cancelled), by a pause, or by shutdown.
            SafeUpdate(job.Id, j =>
            {
                if (j.State != DownloadState.Cancelled)
                {
                    j.State = DownloadState.Pending;
                }

                j.BytesPerSecond = 0;
            });

            if (queue.Get(job.Id)?.State == DownloadState.Cancelled)
            {
                TryDelete(DownloadPaths.Part(job.DestinationPath));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "JellyIPTV: falha ao baixar {Title}", job.Title);
            SafeUpdate(job.Id, j =>
            {
                if (j.State == DownloadState.Cancelled)
                {
                    return;
                }

                j.Attempts++;
                j.Error = ex.Message;
                j.BytesPerSecond = 0;
                if (j.Attempts <= RetryDelays.Length)
                {
                    j.State = DownloadState.Pending;
                    j.NotBeforeUtc = DateTime.UtcNow + RetryDelays[j.Attempts - 1];
                }
                else
                {
                    j.State = DownloadState.Failed;
                }
            });
        }
        finally
        {
            lock (_lock)
            {
                // Only remove our own entry: an interrupted job may already have been restarted under a new token.
                if (_active.TryGetValue(job.Id, out var current) && ReferenceEquals(current, cts))
                {
                    _active.Remove(job.Id);
                    _running.Remove(job.Id);
                    if (queue.Get(job.Id)?.State == DownloadState.Cancelled)
                    {
                        TryDelete(DownloadPaths.Part(job.DestinationPath));
                    }
                }
            }

            cts.Dispose();
            try
            {
                MaybeTriggerScan();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "JellyIPTV: falha ao iniciar a varredura da biblioteca");
            }

            Wake();
        }
    }

    private void SafeUpdate(Guid id, Action<DownloadJob> mutate, bool persist = true)
    {
        try
        {
            queue.Update(id, mutate, persist);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "JellyIPTV: não foi possível atualizar a fila de downloads");
        }
    }

    /// <summary>One library scan once the queue drains, instead of one per episode.</summary>
    private void MaybeTriggerScan()
    {
        bool trigger;
        lock (_lock)
        {
            trigger = _completedSinceScan && _active.Count == 0 && !queue.HasPendingWork();
            if (trigger)
            {
                _completedSinceScan = false;
            }
        }

        if (trigger)
        {
            scanTrigger.Trigger();
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "JellyIPTV: não foi possível apagar {Path}", path);
        }
    }
}
