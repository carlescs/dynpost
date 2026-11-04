using Missive.Application.Common;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;

namespace Missive.Application.Folders;

/// <summary>Creates a collection folder inside the open workspace.</summary>
public sealed class CreateFolder(IWorkspaceRepository repository, CurrentWorkspace current)
{
    public Result<FolderDto> Execute(string parent, string name)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!FolderPaths.TryParseFolder(repository, root.Value, parent, allowRoot: true, out var parentPath, out var error))
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
