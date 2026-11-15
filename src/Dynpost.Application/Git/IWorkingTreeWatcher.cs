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
    /// changes settle (a burst of changes calls it once, with all of them), until the returned
    /// handle is disposed. Changes under the workspace's local <c>.dynpost</c> folder are ignored.
    /// </summary>
    IDisposable Watch(string workspacePath, Action<WorkingTreeChange> changed);
}

/// <summary>A settled burst of changes seen by an <see cref="IWorkingTreeWatcher"/>.</summary>
/// <param name="Files">
/// The workspace-relative paths (<c>/</c>-separated) of the files and folders that were created,
/// changed, removed or renamed (both names) in the workspace, outside its git folder; empty when
/// only git's own state changed, and <see langword="null"/> when changes were lost (the system's
/// buffer of notifications overflowed), so anything may have changed.
/// </param>
public sealed record WorkingTreeChange(IReadOnlyList<string>? Files);
