using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace JellyIptv.Downloads;

public readonly record struct DownloadProgress(long BytesDownloaded, long? TotalBytes, double BytesPerSecond);

public interface IFileDownloader
{
    Task DownloadAsync(string url, string userAgent, string destinationPath, IProgress<DownloadProgress> progress, CancellationToken cancellationToken);
}

/// <summary>Synchronous IProgress (System.Progress posts asynchronously, which reorders reports).</summary>
public sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

/// <summary>Downloads to "file.part", resuming with Range when possible, then renames.</summary>
public sealed class HttpFileDownloader(IHttpClientFactory httpClientFactory) : IFileDownloader
{
    internal TimeSpan ReadTimeout { get; init; } = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(500);

    public async Task DownloadAsync(string url, string userAgent, string destinationPath, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
    {
        var part = DownloadPaths.Part(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        while (true)
        {
            long existing = File.Exists(part) ? new FileInfo(part).Length : 0;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            if (existing > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existing, null);
            }

            var client = httpClientFactory.CreateClient("JellyIptv");
            client.Timeout = Timeout.InfiniteTimeSpan;
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existing > 0)
            {
                // "bytes */N": the part is complete only if it already has N bytes. Without a length we trust the server.
                var complete = response.Content.Headers.ContentRange?.Length;
                if (complete is null || complete == existing)
                {
                    File.Move(part, destinationPath);
                    return;
                }

                File.Delete(part);
                continue;
            }

            response.EnsureSuccessStatusCode();

            var resumed = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (resumed && response.Content.Headers.ContentRange is { } range && range.From != existing)
            {
                // The server answered a different range than we asked for: appending would corrupt the file.
                File.Delete(part);
                continue;
            }

            if (!resumed)
            {
                existing = 0;
            }

            long? total = response.Content.Headers.ContentRange?.Length
                ?? (response.Content.Headers.ContentLength is long length ? length + existing : null);

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                var done = existing;
                long windowBytes = 0;
                var clock = Stopwatch.StartNew();
                var lastReport = TimeSpan.Zero;
                while (true)
                {
                    var read = await ReadWithTimeoutAsync(input, buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    done += read;
                    windowBytes += read;
                    var elapsed = clock.Elapsed;
                    if (elapsed - lastReport >= ReportInterval)
                    {
                        progress.Report(new DownloadProgress(done, total, windowBytes / (elapsed - lastReport).TotalSeconds));
                        lastReport = elapsed;
                        windowBytes = 0;
                    }
                }

                progress.Report(new DownloadProgress(done, total, 0));
                if (total is long expected && done < expected)
                {
                    throw new IOException($"Download incompleto: {done} de {expected} bytes.");
                }
            }

            File.Move(part, destinationPath);
            return;
        }
    }

    private async Task<int> ReadWithTimeoutAsync(Stream input, byte[] buffer, CancellationToken cancellationToken)
    {
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readCts.CancelAfter(ReadTimeout);
        try
        {
            return await input.ReadAsync(buffer, readCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException($"Sem dados do servidor por {ReadTimeout.TotalSeconds:0}s");
        }
    }
}
