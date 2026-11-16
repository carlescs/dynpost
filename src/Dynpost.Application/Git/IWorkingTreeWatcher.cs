namespace Dynpost.Application.Git;

/// <summary>
/// Watches a workspace folder and its git repository for changes that can change what git
/// reports: files added, edited or removed in the workspace (by the app or any other program) and
/// git's own state (commits, checkouts, staging, merges) made by other git tools.
/// </summary>
public interface IWorkingTreeWatcher
{
    /// <summary>
    /// Starts watching <paramref name="workspacePath"/> and calls <paramref name="changed"/> after
    /// changes settle (a burst of changes calls it once), until the returned handle is disposed,
    /// with the workspace paths (<c>/</c>-separated, relative to the workspace) of the files and
    /// folders that changed in the burst: empty when only git's state changed, or <c>null</c> when
    /// changes may have been missed (so anything may have changed). Changes under the workspace's
    /// local <c>.dynpost</c> folder and its git folder are never listed, and <c>.dynpost</c>
    /// changes alone don't call it.
    /// </summary>
    IDisposable Watch(string workspacePath, Action<IReadOnlyCollection<string>?> changed);
}
