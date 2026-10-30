using Jellyfin.Data.Events;
using JellyIptv.Live;
using MediaBrowser.Model.Tasks;
using Xunit;

namespace JellyIptv.Tests;

public class GuideRefresherTests
{
    private sealed class FakeTask(string key) : IScheduledTask
    {
        public string Name => key;

        public string Key => key;

        public string Description => string.Empty;

        public string Category => string.Empty;

        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) => Task.CompletedTask;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
    }

    private sealed class FakeWorker(IScheduledTask task) : IScheduledTaskWorker
    {
#pragma warning disable CS0067
        public event EventHandler<GenericEventArgs<double>>? TaskProgress;
#pragma warning restore CS0067

        public IScheduledTask ScheduledTask => task;

        public TaskResult LastExecutionResult => null!;

        public string Name => task.Name;

        public string Description => string.Empty;

        public string Category => string.Empty;

        public TaskState State => TaskState.Idle;

        public double? CurrentProgress => null;

        public IReadOnlyList<TaskTriggerInfo> Triggers { get; set; } = [];

        public string Id => task.Key;

        public void ReloadTriggerEvents()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeTaskManager(params IScheduledTask[] tasks) : ITaskManager
    {
        public List<IScheduledTask> Queued { get; } = [];

#pragma warning disable CS0067
        public event EventHandler<GenericEventArgs<IScheduledTaskWorker>>? TaskExecuting;

        public event EventHandler<TaskCompletionEventArgs>? TaskCompleted;
#pragma warning restore CS0067

        public IReadOnlyList<IScheduledTaskWorker> ScheduledTasks { get; } = tasks.Select(t => (IScheduledTaskWorker)new FakeWorker(t)).ToList();

        public void QueueScheduledTask(IScheduledTask task, TaskOptions options) => Queued.Add(task);

        public void CancelIfRunningAndQueue<T>(TaskOptions options) where T : IScheduledTask => throw new NotSupportedException();

        public void CancelIfRunningAndQueue<T>() where T : IScheduledTask => throw new NotSupportedException();

        public void CancelIfRunning<T>() where T : IScheduledTask => throw new NotSupportedException();

        public void QueueScheduledTask<T>(TaskOptions options) where T : IScheduledTask => throw new NotSupportedException();

        public void QueueScheduledTask<T>() where T : IScheduledTask => throw new NotSupportedException();

        public void QueueIfNotRunning<T>() where T : IScheduledTask => throw new NotSupportedException();

        public void AddTasks(IEnumerable<IScheduledTask> tasks) => throw new NotSupportedException();

        public void Cancel(IScheduledTaskWorker task) => throw new NotSupportedException();

        public Task Execute(IScheduledTaskWorker task, TaskOptions options) => throw new NotSupportedException();

        public void Execute<T>() where T : IScheduledTask => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    [Fact]
    public void Queues_jellyfin_refresh_guide_task_by_key()
    {
        var guide = new FakeTask("RefreshGuide");
        var tasks = new FakeTaskManager(new FakeTask("RefreshChannels"), guide);

        GuideRefresher.Queue(tasks);

        Assert.Equal(new[] { guide }, tasks.Queued);
    }

    [Fact]
    public void Does_nothing_when_task_missing()
    {
        var tasks = new FakeTaskManager(new FakeTask("Other"));

        GuideRefresher.Queue(tasks);

        Assert.Empty(tasks.Queued);
    }
}
