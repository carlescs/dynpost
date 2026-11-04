using Missive.Application.Common;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;

namespace Missive.Application.Folders;

/// <summary>
/// Deletes a collection folder with its requests. Refuses when the folder holds anything else —
/// notes, scripts, links — so Missive never deletes files it does not own.
/// </summary>
public sealed class DeleteFolder(IWorkspaceRepository repository, CurrentWorkspace current)
{
    private const int MaxListed = 3;

    public Result<Unit> Execute(string path)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!FolderPaths.TryParseFolder(repository, root.Value, path, allowRoot: false, out var folder, out var error))
        {
            return error;
        }

        var contents = repository.ReadFolderContents(root.Value, folder);
        if (!contents.IsSuccess)
        {
            return contents.Error!;
        }

        var blocking = contents.Value.Links
            .Concat(contents.Value.Files.Where(f => !WorkspaceLayout.IsDisposableFile(f.FileName)))
            .Select(p => p.Value)
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

        return repository.DeleteFolder(root.Value, folder);
    }
}
