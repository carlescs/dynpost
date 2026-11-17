using Dynpost.Application.Workspaces;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Application.Git;

/// <summary>
/// Tells the UI when the open workspace changes on disk, whoever changes it (the app, another
/// editor, git in a terminal), so it stays current without the user reloading it: the git status
/// may have changed, and so may the request, environment, run plan and folder settings files the
/// UI shows. Follows the workspace the app has open and watches it through
/// <see cref="IWorkingTreeWatcher"/>. A change that brings in a system folder spelled in another
/// case (such as a pull adding <c>Requests/</c>) has it renamed by <see cref="LowerCaseSystemFolders"/>;
/// the rename is then reported like any other change. Registered as a singleton.
/// </summary>
public sealed class WatchWorkingTree(
    CurrentWorkspace workspace,
    IWorkingTreeWatcher watcher,
    LowerCaseSystemFolders lowerCase) : IDisposable
{
    private readonly Lock _gate = new();
    private Action? _gitChanged;
    private Action<IReadOnlyList<string>?>? _filesChanged;
    private IDisposable? _watch;
    private bool _disposed;

    /// <summary>
    /// Calls <paramref name="gitChanged"/> whenever the open workspace's files or git state change,
    /// and <paramref name="filesChanged"/> when files the app reads changed among them: with their
    /// workspace paths, or <see langword="null"/> when changes were lost and any file may have changed.
    /// </summary>
    public void Start(Action gitChanged, Action<IReadOnlyList<string>?> filesChanged)
    {
        lock (_gate)
        {
            if (_gitChanged is not null || _disposed)
            {
                return;
            }

            _gitChanged = gitChanged;
            _filesChanged = filesChanged;
        }

        workspace.Opened += Follow;
        if (workspace.Root is { } root)
        {
            Follow(root);
        }
    }

    /// <summary>
    /// Whether the app reads the file or folder at <paramref name="path"/> (workspace-relative):
    /// anything in the requests, plans and environments folders, those folders themselves, the
    /// workspace file and the <c>.gitignore</c> that marks files local-only.
    /// </summary>
    public static bool IsAppFile(string path)
    {
        var first = path.Split('/', 2)[0];
        return first is WorkspaceLayout.RequestsFolder or WorkspaceLayout.PlansFolder or WorkspaceLayout.EnvironmentsFolder
            || path is WorkspaceLayout.WorkspaceFile or ".gitignore";
    }

    private void Report(string root, WorkingTreeChange change)
    {
        Action? gitChanged;
        Action<IReadOnlyList<string>?>? filesChanged;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            gitChanged = _gitChanged;
            filesChanged = _filesChanged;
        }

        if (LowerCaseSystemFolders.MayBeNeeded(change.Files))
        {
            _ = LowerCaseAsync(root);
        }

        gitChanged?.Invoke();
        if (change.Files is null)
        {
            filesChanged?.Invoke(null);
            return;
        }

        var files = change.Files.Where(IsAppFile).ToList();
        if (files.Count > 0)
        {
            filesChanged?.Invoke(files);
        }
    }

    /// <summary>Best effort: a folder that can't be renamed now is tried again on the next change.</summary>
    private async Task LowerCaseAsync(string root)
    {
        try
        {
            await lowerCase.ExecuteAsync(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }
    }

    private void Follow(string root)
    {
        lock (_gate)
        {
            if (_disposed || _gitChanged is null)
            {
                return;
            }

            _watch?.Dispose();
            _watch = null;
            try
            {
                _watch = watcher.Watch(root, change => Report(root, change));
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
