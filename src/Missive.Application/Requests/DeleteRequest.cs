using Missive.Application.Common;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;

namespace Missive.Application.Requests;

/// <summary>Deletes a request file from the open workspace.</summary>
public sealed class DeleteRequest(IWorkspaceRepository repository, CurrentWorkspace current)
{
    public Result<Unit> Execute(string path)
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

        return repository.DeleteFile(root.Value, requestPath);
    }
}
