using Dynypost.Application.Common;
using Dynypost.Application.History;
using Dynypost.Application.Sharing;
using Dynypost.Application.Workspaces;
using Dynypost.Domain.Workspaces;

namespace Dynypost.Application.Folders;

/// <summary>
/// Deletes a collection folder with its requests. Refuses when the folder holds anything else —
/// notes, scripts, links, or names that aren't valid workspace paths — so Dynypost never deletes
/// files it does not own.
/// </summary>
public sealed class DeleteFolder(IWorkspaceRepository repository, ILocalOnlyFiles localFiles, IHistoryStore history, CurrentWorkspace current)
{
    private const int MaxListed = 3;

    public Result<Unit> Execute(string path, WorkspacePath? area = null)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!FolderPaths.TryParseFolder(repository, root.Value, path, allowRoot: false, out var folder, out var error, area))
        {
            return error;
        }

        var contents = repository.ReadFolderContents(root.Value, folder);
        if (!contents.IsSuccess)
        {
            return contents.Error!;
        }

        // Names that aren't workspace paths (such as "a:b.txt" on Linux) can't be checked, and
        // the recursive delete would remove them along with anything under them.
        var blocking = contents.Value.Links
            .Concat(contents.Value.Files.Where(f => !WorkspaceLayout.IsDisposableFile(f.FileName)))
            .Select(p => p.Value)
            .Concat((contents.Value.Unsupported ?? []).Select(u => $"{folder}/{u}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        if (blocking.Count > 0)
        {
            var listed = string.Join(", ", blocking.Take(MaxListed));
            var more = blocking.Count > MaxListed ? $" and {blocking.Count - MaxListed} more" : string.Empty;
            return new Error(
                ErrorCodes.FolderNotEmpty,
                $"'{folder}' contains files that are not requests ({listed}{more}). Move or delete them first.");
        }

        var deleted = repository.DeleteFolder(root.Value, folder);
        if (deleted.IsSuccess)
        {
            // Best effort: leftover .gitignore lines for deleted files do no harm.
            localFiles.Remove(root.Value, folder);
            history.Remove(root.Value, folder);
        }

        return deleted;
    }
}
