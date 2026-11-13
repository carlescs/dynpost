using Dynpost.Application.Common;
using Dynpost.Application.History;
using Dynpost.Application.Sharing;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Application.Requests;

/// <summary>Deletes a request file from the open workspace.</summary>
public sealed class DeleteRequest(IWorkspaceRepository repository, ILocalOnlyFiles localFiles, IHistoryStore history, CurrentWorkspace current)
{
    public Result<Unit> Execute(string path)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!WorkspacePath.TryParse(path, out var requestPath) || requestPath.IsRoot
            || !WorkspaceLayout.IsRequestPath(requestPath))
        {
            return new Error(ErrorCodes.InvalidPath, $"'{path}' is not a valid request path.");
        }

        if (!repository.FileExists(root.Value, requestPath))
        {
            return new Error(ErrorCodes.NotFound, $"The request file '{path}' does not exist.");
        }

        var deleted = repository.DeleteFile(root.Value, requestPath);
        if (deleted.IsSuccess)
        {
            // Best effort: a leftover .gitignore line for a deleted file does no harm.
            localFiles.Remove(root.Value, requestPath);
            history.Remove(root.Value, requestPath);
        }

        return deleted;
    }
}
