using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Application.Requests;

/// <summary>
/// Shows the text of a request's file: the file as it is on disk when <c>path</c> is given (a saved
/// request without unsaved changes), otherwise the text saving <c>request</c> would write.
/// </summary>
public sealed class ViewRequestFile(IWorkspaceRepository repository, IRequestFileFormat format, CurrentWorkspace current)
{
    public async Task<Result<string>> ExecuteAsync(RequestDto request, string? path, CancellationToken cancellationToken = default)
    {
        if (path is null)
        {
            var definition = RequestMapper.ToDomain(
                string.IsNullOrWhiteSpace(request.Name) ? request with { Name = "Untitled" } : request);
            return definition.IsSuccess ? format.Format(definition.Value) : definition.Error!;
        }

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

        return await repository.ReadTextAsync(root.Value, requestPath, cancellationToken);
    }
}
