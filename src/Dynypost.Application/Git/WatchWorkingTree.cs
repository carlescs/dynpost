using Dynypost.Application.Workspaces;

namespace Dynypost.Application.Git;

/// <summary>
/// Tells the UI when the open workspace's git status may have changed, so the source control view
/// stays current without the user refreshing it: follows the workspace the app has open and
/// watches it through <see cref="IWorkingTreeWatcher"/>. Registered as a singleton.
/// </summary>
public sealed class WatchWorkingTree(CurrentWorkspace workspace, IWorkingTreeWatcher watcher) : IDisposable
{
    private readonly Lock _gate = new();
    private Action? _changed;
    private IDisposable? _watch;
    private bool _disposed;

    /// <summary>Calls <paramref name="changed"/> whenever the open workspace's files or git state change.</summary>
    public void Start(Action changed)
    {
        lock (_gate)
        {
            if (_changed is not null || _disposed)
            {
                return;
            }

            _changed = changed;
        }

        workspace.Opened += Follow;
        if (workspace.Root is { } root)
        {
            Follow(root);
        }
    }

    private void Follow(string root)
    {
        lock (_gate)
        {
            if (_disposed || _changed is not { } changed)
            {
                return;
            }

            _watch?.Dispose();
            _watch = null;
            try
            {
                _watch = watcher.Watch(root, changed);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
            {
                // Best effort: without a watch the view is still refreshed on focus and after git actions.
            }
        }
    }

    public void Dispose()
    {
        workspace.Opened -= Follow;
        lock (_gate)
        {
            _disposed = true;
            _watch?.Dispose();
            _watch = null;
        }
    }
}
