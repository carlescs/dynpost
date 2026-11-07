using Missive.Application.Common;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;

namespace Missive.Application.Requests;

/// <summary>
/// Copies a request file next to the original. The copy is named <c>… copy</c> and gets a new
/// file named after it (<c>get-user-copy.yaml</c>, or <c>get-user-copy-2.yaml</c> if that is
/// taken); the original is left untouched.
/// </summary>
public sealed class DuplicateRequest(IWorkspaceRepository repository, CurrentWorkspace current)
{
    public const string CopySuffix = " copy";

    /// <summary>
    /// Bridge messages are handled concurrently; one duplicate at a time means two copies of the
    /// same request never pick, and overwrite, the same file name.
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<Result<SavedRequestDto>> ExecuteAsync(string path, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await DuplicateAsync(path, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<Result<SavedRequestDto>> DuplicateAsync(string path, CancellationToken cancellationToken)
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

        var original = await repository.ReadRequestAsync(root.Value, requestPath, cancellationToken);
        if (!original.IsSuccess)
        {
            return original.Error!;
        }

        var copy = original.Value with { Name = original.Value.Name + CopySuffix };
        var target = CreateRequest.FreePath(repository, root.Value, requestPath.Parent, copy.Name, current: null);
        if (target is null)
        {
            return new Error(ErrorCodes.Io, $"Could not find a free file name for '{copy.Name}'.");
        }

        var written = await repository.WriteRequestAsync(root.Value, target, copy, cancellationToken);
        return written.IsSuccess
            ? new SavedRequestDto(target.Value, RequestMapper.ToDto(copy))
            : written.Error!;
    }
}
