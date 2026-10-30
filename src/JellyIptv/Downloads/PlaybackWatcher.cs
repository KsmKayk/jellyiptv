using JellyIptv.Browse;
using JellyIptv.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellyIptv.Downloads;

/// <summary>Xtream accounts often allow a single connection: pause downloads while IPTV is being watched.</summary>
public sealed class PlaybackWatcher(
    ISessionManager sessionManager,
    IptvItemResolver resolver,
    DownloadManager downloads,
    IConfigProvider config,
    ILogger<PlaybackWatcher> logger) : IHostedService, IDisposable
{
    private Timer? timer;

    /// <summary>
    /// True when any session is currently playing an IPTV item. FullNowPlayingItem is never cleared by Jellyfin,
    /// so it only counts while NowPlayingItem points at the same item.
    /// </summary>
    internal static bool IsStreaming(IEnumerable<(Guid? NowPlayingId, BaseItem? FullItem)> sessions, Func<BaseItem, bool> isIptv) =>
        sessions.Any(s => s.NowPlayingId is not null && s.FullItem is not null && s.FullItem.Id == s.NowPlayingId && isIptv(s.FullItem));

    public Task StartAsync(CancellationToken cancellationToken)
    {
        sessionManager.PlaybackStart += OnSessionChanged;
        sessionManager.PlaybackStopped += OnSessionChanged;
        sessionManager.SessionEnded += OnSessionChanged;
        timer = new Timer(_ => Evaluate(), null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        sessionManager.PlaybackStart -= OnSessionChanged;
        sessionManager.PlaybackStopped -= OnSessionChanged;
        sessionManager.SessionEnded -= OnSessionChanged;
        timer?.Dispose();
        timer = null;
        return Task.CompletedTask;
    }

    public void Dispose() => timer?.Dispose();

    private void OnSessionChanged(object? sender, EventArgs e) => Evaluate();

    private void Evaluate()
    {
        try
        {
            var streaming = IsStreaming(
                sessionManager.Sessions.Select(s => ((Guid?)s.NowPlayingItem?.Id, (BaseItem?)s.FullNowPlayingItem)),
                resolver.IsIptvPlayback);
            downloads.SetPaused(streaming && config.Current.PauseWhileStreaming);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "JellyIPTV: falha ao avaliar reprodução ativa");
        }
    }
}
