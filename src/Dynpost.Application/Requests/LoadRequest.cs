using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Application.Requests;

/// <summary>Reads one request file of the open workspace.</summary>
public sealed class LoadRequest(IWorkspaceRepository repository, CurrentWorkspace current)
{
    public async Task<Result<RequestDto>> ExecuteAsync(string path, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!WorkspacePath.TryParse(path, out var requestPath) || !WorkspaceLayout.IsRequestPath(requestPath))
        {
            return new Error(ErrorCodes.InvalidPath, $"'{path}' is not a valid request path.");
        }

        if (!repository.FileExists(root.Value, requestPath))
        {
            return new Error(ErrorCodes.NotFound, $"The request file '{path}' does not exist.");
        }

        var request = await repository.ReadRequestAsync(root.Value, requestPath, cancellationToken);
        return request.IsSuccess ? RequestMapper.ToDto(request.Value) : request.Error!;
    }
}
