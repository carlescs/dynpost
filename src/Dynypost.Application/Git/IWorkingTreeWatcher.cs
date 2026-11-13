namespace Dynypost.Application.Git;

/// <summary>
/// Watches a workspace folder and its git repository for changes that can change what git
/// reports: files added, edited or removed in the workspace (by the app or any other program) and
/// git's own state (commits, checkouts, staging, merges) made by other git tools.
/// </summary>
public interface IWorkingTreeWatcher
{
    /// <summary>
    /// Starts watching <paramref name="workspacePath"/> and calls <paramref name="changed"/> after
    /// changes settle (a burst of changes calls it once), until the returned handle is disposed.
    /// Changes under the workspace's local <c>.dynypost</c> folder are ignored.
    /// </summary>
    IDisposable Watch(string workspacePath, Action changed);
}
