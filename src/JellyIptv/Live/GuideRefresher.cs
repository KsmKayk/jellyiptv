using MediaBrowser.Model.Tasks;

namespace JellyIptv.Live;

/// <summary>Queues Jellyfin's "Refresh Guide" task so live channels and EPG appear without waiting for its daily run.</summary>
public static class GuideRefresher
{
    // Jellyfin.LiveTv.Guide.RefreshGuideScheduledTask lives in an assembly we don't reference; find it by key.
    private const string RefreshGuideKey = "RefreshGuide";

    public static void Queue(ITaskManager taskManager)
    {
        var worker = taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.Key == RefreshGuideKey);
        if (worker is not null)
        {
            taskManager.QueueScheduledTask(worker.ScheduledTask, new TaskOptions());
        }
    }
}
