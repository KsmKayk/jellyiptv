using System.Collections.Concurrent;

namespace JellyIptv.Xtream;

public sealed class TtlCache
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset Expires, Task<object> Value)> _entries = new();
    private readonly TimeProvider _time;

    public TtlCache()
        : this(TimeProvider.System)
    {
    }

    internal TtlCache(TimeProvider time)
    {
        _time = time;
    }

    /// <remarks>A key must always be used with the same <typeparamref name="T"/>.</remarks>
    public async Task<T> GetOrAddAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
    {
        var now = _time.GetUtcNow();
        if (_entries.TryGetValue(key, out var entry) && entry.Expires > now)
        {
            return (T)await entry.Value.ConfigureAwait(false);
        }

        var task = Box(factory);
        var created = (Expires: now + ttl, Value: task);
        _entries[key] = created;
        if (_entries.Count > 256)
        {
            foreach (var pair in _entries)
            {
                if (pair.Value.Expires <= now)
                {
                    _entries.TryRemove(pair);
                }
            }
        }

        try
        {
            return (T)await task.ConfigureAwait(false);
        }
        catch
        {
            _entries.TryRemove(KeyValuePair.Create(key, created));
            throw;
        }
    }

    public void Clear() => _entries.Clear();

    private static async Task<object> Box<T>(Func<Task<T>> factory) => (await factory().ConfigureAwait(false))!;
}
