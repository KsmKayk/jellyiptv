using System.Text.Json;
using System.Text.Json.Serialization;

namespace JellyIptv.Downloads;

/// <summary>Thread-safe job list persisted as JSON. Callers always receive copies.</summary>
public sealed class DownloadQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _lock = new();
    private readonly List<DownloadJob> _jobs = [];

    public DownloadQueue(string path)
    {
        _path = path;
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var loaded = (JsonSerializer.Deserialize<List<DownloadJob?>>(File.ReadAllText(path), JsonOptions) ?? [])
                .OfType<DownloadJob>()
                .ToList();
            foreach (var job in loaded)
            {
                if (job.State == DownloadState.Downloading)
                {
                    job.State = DownloadState.Pending;
                }

                job.BytesPerSecond = 0;
            }

            _jobs.AddRange(loaded);
        }
        catch (JsonException)
        {
            // Corrupt queue file: keep a copy for inspection and start empty rather than failing the server start.
            try
            {
                File.Move(path, path + ".corrupt", overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable queue file: start empty.
        }
    }

    public IReadOnlyList<DownloadJob> Snapshot()
    {
        lock (_lock)
        {
            return _jobs.Select(j => j.Clone()).ToList();
        }
    }

    public DownloadJob? Get(Guid id)
    {
        lock (_lock)
        {
            return Find(id)?.Clone();
        }
    }

    /// <summary>Adds jobs, ignoring ones already pending/downloading for the same file. Returns how many were added.</summary>
    public int Enqueue(IEnumerable<DownloadJob> jobs)
    {
        lock (_lock)
        {
            var added = 0;
            foreach (var job in jobs)
            {
                var existing = _jobs.FirstOrDefault(j => string.Equals(j.DestinationPath, job.DestinationPath, StringComparison.Ordinal));
                if (existing is { State: DownloadState.Pending or DownloadState.Downloading })
                {
                    continue;
                }

                if (existing is not null)
                {
                    _jobs.Remove(existing);
                }

                _jobs.Add(job.Clone());
                added++;
            }

            if (added > 0)
            {
                Save();
            }

            return added;
        }
    }

    public DownloadJob? TakeNextPending(DateTime nowUtc, Func<Guid, bool>? canTake = null)
    {
        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.State == DownloadState.Pending
                && (j.NotBeforeUtc is null || j.NotBeforeUtc <= nowUtc)
                && (canTake is null || canTake(j.Id)));
            if (job is null)
            {
                return null;
            }

            job.State = DownloadState.Downloading;
            try
            {
                Save();
            }
            catch
            {
                job.State = DownloadState.Pending;
                throw;
            }

            return job.Clone();
        }
    }

    public void Update(Guid id, Action<DownloadJob> mutate, bool persist = true)
    {
        lock (_lock)
        {
            var job = Find(id);
            if (job is null)
            {
                return;
            }

            mutate(job);
            if (persist)
            {
                Save();
            }
        }
    }

    public bool Cancel(Guid id)
    {
        lock (_lock)
        {
            var job = Find(id);
            if (job is not { State: DownloadState.Pending or DownloadState.Downloading })
            {
                return false;
            }

            job.State = DownloadState.Cancelled;
            job.BytesPerSecond = 0;
            Save();
            return true;
        }
    }

    public bool Retry(Guid id)
    {
        lock (_lock)
        {
            var job = Find(id);
            if (job is not { State: DownloadState.Failed or DownloadState.Cancelled })
            {
                return false;
            }

            job.State = DownloadState.Pending;
            job.Attempts = 0;
            job.Error = null;
            job.NotBeforeUtc = null;
            Save();
            return true;
        }
    }

    public int ClearFinished()
    {
        lock (_lock)
        {
            var removed = _jobs.RemoveAll(j => j.State is DownloadState.Completed or DownloadState.Skipped or DownloadState.Cancelled);
            if (removed > 0)
            {
                Save();
            }

            return removed;
        }
    }

    public bool HasPendingWork()
    {
        lock (_lock)
        {
            return _jobs.Any(j => j.State is DownloadState.Pending or DownloadState.Downloading);
        }
    }

    private DownloadJob? Find(Guid id) => _jobs.FirstOrDefault(j => j.Id == id);

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_jobs, JsonOptions));
        File.Move(tmp, _path, overwrite: true);
    }
}
