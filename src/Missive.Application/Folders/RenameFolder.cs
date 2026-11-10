using Missive.Application.Common;
using Missive.Application.Sharing;
using Missive.Application.Workspaces;

namespace Missive.Application.Folders;

/// <summary>Renames a collection folder; everything inside it moves along.</summary>
public sealed class RenameFolder(IWorkspaceRepository repository, ILocalOnlyFiles localFiles, CurrentWorkspace current)
{
    public Result<FolderDto> Execute(string path, string newName)
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

        var target = FolderPaths.Child(folder.Parent, newName);
        if (!target.IsSuccess)
        {
            return target.Error!;
        }

        if (target.Value == folder)
        {
            return new FolderDto(folder.Value);
        }

        // A change of case only ("Users" → "users") finds the folder itself on case-insensitive
        // file systems, so it does not count as a clash.
        var caseOnly = string.Equals(target.Value.Value, folder.Value, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && repository.EntryExists(root.Value, target.Value))
        {
            return new Error(ErrorCodes.AlreadyExists, $"'{target.Value}' already exists.");
        }

        var moved = repository.MoveFolder(root.Value, folder, target.Value);
        if (!moved.IsSuccess)
        {
            return moved.Error!;
        }

        var followed = localFiles.Move(root.Value, folder, target.Value);
        return followed.IsSuccess ? new FolderDto(target.Value.Value) : followed.Error!;
    }
}
