using Missive.Application.Common;
using Missive.Application.Sharing;
using Missive.Application.Folders;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;

namespace Missive.Application.Requests;

public sealed record MovedRequestDto(string Path);

/// <summary>
/// Moves a request file to another folder of the open workspace. The file keeps its name; if the
/// destination already has a file with that name, nothing is moved.
/// </summary>
public sealed class MoveRequest(IWorkspaceRepository repository, ILocalOnlyFiles localFiles, CurrentWorkspace current)
{
    public Result<MovedRequestDto> Execute(string path, string folder)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!WorkspacePath.TryParse(path, out var requestPath) || requestPath.IsRoot
            || !WorkspaceLayout.IsRequestFile(requestPath.Parent, requestPath.FileName))
        {
            return new Error(ErrorCodes.InvalidPath, $"'{path}' is not a valid request path.");
        }

        if (!repository.FileExists(root.Value, requestPath))
        {
            return new Error(ErrorCodes.NotFound, $"The request file '{path}' does not exist.");
        }

        if (!FolderPaths.TryParseFolder(repository, root.Value, folder, allowRoot: true, out var target, out var error))
        {
            return error;
        }

        var destination = target.Combine(requestPath.FileName);
        if (destination == requestPath)
        {
            return new MovedRequestDto(requestPath.Value);
        }

        // missive.yaml is the workspace file at the root, so a request with that name can't go there.
        if (!WorkspaceLayout.IsRequestFile(target, requestPath.FileName))
        {
            return new Error(ErrorCodes.InvalidPath, $"'{requestPath.FileName}' can't be moved to the workspace root.");
        }

        if (repository.EntryExists(root.Value, destination))
        {
            return new Error(ErrorCodes.AlreadyExists, $"'{destination}' already exists.");
        }

        var moved = repository.MoveFile(root.Value, requestPath, destination);
        if (!moved.IsSuccess)
        {
            return moved.Error!;
        }

        var followed = localFiles.Move(root.Value, requestPath, destination);
        return followed.IsSuccess ? new MovedRequestDto(destination.Value) : followed.Error!;
    }
}
