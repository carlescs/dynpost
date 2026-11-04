using Missive.Application.Common;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;

namespace Missive.Application.Folders;

/// <summary>
/// Moves a collection folder, with everything in it, into another folder of the open workspace.
/// The folder keeps its name.
/// </summary>
public sealed class MoveFolder(IWorkspaceRepository repository, CurrentWorkspace current)
{
    public Result<FolderDto> Execute(string path, string parent)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!FolderPaths.TryParseFolder(repository, root.Value, path, allowRoot: false, out var folder, out var error)
            || !FolderPaths.TryParseFolder(repository, root.Value, parent, allowRoot: true, out var target, out error))
        {
            return error;
        }

        if (target == folder.Parent)
        {
            return new FolderDto(folder.Value);
        }

        if (FolderPaths.IsWithin(target, folder))
        {
            return new Error(ErrorCodes.InvalidPath, $"'{folder}' can't be moved into itself.");
        }

        // The name stays as it is; only check it isn't reserved in its new place.
        if (!WorkspaceLayout.IsCollectionFolder(target, folder.FileName))
        {
            return new Error(ErrorCodes.InvalidPath, $"'{folder.FileName}' is reserved in that location.");
        }

        var destination = target.Combine(folder.FileName);
        if (repository.EntryExists(root.Value, destination))
        {
            return new Error(ErrorCodes.AlreadyExists, $"'{destination}' already exists.");
        }

        var moved = repository.MoveFolder(root.Value, folder, destination);
        return moved.IsSuccess ? new FolderDto(destination.Value) : moved.Error!;
    }
}
