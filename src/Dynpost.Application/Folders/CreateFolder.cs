using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Application.Folders;

/// <summary>Creates a folder inside the open workspace's <c>requests/</c> or <c>plans/</c> folder.</summary>
public sealed class CreateFolder(IWorkspaceRepository repository, CurrentWorkspace current)
{
    public Result<FolderDto> Execute(string parent, string name, WorkspacePath? area = null)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        area ??= FolderPaths.AreaOf(parent);
        if (!FolderPaths.TryParseFolder(repository, root.Value, parent, allowRoot: true, out var parentPath, out var error, area))
        {
            return error;
        }

        var target = FolderPaths.Child(parentPath, name);
        if (!target.IsSuccess)
        {
            return target.Error!;
        }

        if (repository.EntryExists(root.Value, target.Value))
        {
            return new Error(ErrorCodes.AlreadyExists, $"'{target.Value}' already exists.");
        }

        var created = repository.CreateFolder(root.Value, target.Value);
        return created.IsSuccess ? new FolderDto(target.Value.Value) : created.Error!;
    }
}
