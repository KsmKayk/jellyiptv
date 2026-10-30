using MediaBrowser.Controller.Library;

namespace JellyIptv.Downloads;

public interface ILibraryScanTrigger
{
    void Trigger();
}

public sealed class JellyfinLibraryScanTrigger(ILibraryManager libraryManager) : ILibraryScanTrigger
{
    public void Trigger() => libraryManager.QueueLibraryScan();
}
