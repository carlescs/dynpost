using Dynpost.Application.Git;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Application.Workspaces;

/// <summary>
/// Keeps the folders Dynpost reads by name (<see cref="WorkspaceLayout.SystemFolders"/>:
/// <c>requests/</c>, <c>plans/</c>, <c>environments/</c>, <c>.dynpost/</c>) spelled in lower case,
/// so the app always uses those folders: one spelled otherwise, such as <c>Requests</c>, is
/// renamed. On Windows and macOS the app would otherwise read and write such a folder under its
/// lower-case name, so git would record new files under the other spelling, and on Linux it
/// wouldn't find the folder at all. Runs when a workspace is opened and whenever the watched
/// workspace changes in such a folder (a pull, a branch switch, a rename in a file manager).
/// Registered as a singleton.
/// </summary>
public sealed class LowerCaseSystemFolders(IWorkspaceLayoutStore layout, IGitService git)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Whether a change to <paramref name="files"/> (workspace paths as spelled on disk, or
    /// <see langword="null"/> when any file may have changed) may have brought in a system folder
    /// spelled in another case.
    /// </summary>
    public static bool MayBeNeeded(IReadOnlyList<string>? files) =>
        files is null
        || files.Any(file =>
        {
            var first = file.Split('/', 2)[0];
            return WorkspaceLayout.SystemFolders.Any(folder =>
                string.Equals(first, folder, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(first, folder, StringComparison.Ordinal));
        });

    /// <summary>
    /// Renames the system folders of the workspace at <paramref name="root"/> that are spelled in
    /// another case, unless one spelled right also exists, and returns the names it renamed. A
    /// folder git tracks is renamed with git, which shows the rename as a change to commit; when
    /// git can't rename it (say, during a merge) it is left for a later try, as renaming it on disk
    /// alone would hide the rename from git on a case-insensitive file system. Best effort.
    /// </summary>
    public async Task<IReadOnlyList<string>> ExecuteAsync(string root, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var found = layout.FindMiscasedSystemFolders(root);
            var renamed = new List<string>();
            foreach (var folder in found.IsSuccess ? found.Value ?? [] : [])
            {
                if (await RenameAsync(root, folder, cancellationToken))
                {
                    renamed.Add(folder.Name);
                }
            }

            return renamed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> RenameAsync(string root, MiscasedSystemFolder folder, CancellationToken cancellationToken)
    {
        // The local folder is git-ignored, so git never tracks it.
        if (folder.Expected != WorkspaceLayout.LocalFolder)
        {
            var moved = await git.MoveTrackedFolderAsync(root, folder.Name, folder.Expected, cancellationToken);
            if (moved.IsSuccess && moved.Value)
            {
                return true;
            }

            if (moved.IsFailure && moved.Error!.Code != GitErrors.GitNotInstalledCode)
            {
                return false;
            }
        }

        return layout.RenameRootFolder(root, folder.Name, folder.Expected).IsSuccess;
    }
}
