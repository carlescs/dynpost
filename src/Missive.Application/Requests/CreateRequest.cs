using Missive.Application.Common;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;

namespace Missive.Application.Requests;

/// <summary>
/// Saves a new request in a folder of the open workspace, as a file named after the request
/// (<c>create-user.yaml</c>, or <c>create-user-2.yaml</c> if that name is taken).
/// </summary>
public sealed class CreateRequest(IWorkspaceRepository repository, CurrentWorkspace current)
{
    private const int MaxAttempts = 1000;

    public async Task<Result<SavedRequestDto>> ExecuteAsync(string folder, RequestDto request, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!WorkspacePath.TryParse(folder, out var folderPath))
        {
            return new Error(ErrorCodes.InvalidPath, $"'{folder}' is not a valid folder path.");
        }

        var definition = RequestMapper.ToDomain(request);
        if (!definition.IsSuccess)
        {
            return definition.Error!;
        }

        var path = FreePath(repository, root.Value, folderPath, definition.Value.Name, current: null);
        if (path is null)
        {
            return new Error(ErrorCodes.Io, $"Could not find a free file name for '{definition.Value.Name}'.");
        }

        var written = await repository.WriteRequestAsync(root.Value, path, definition.Value, cancellationToken);
        return written.IsSuccess
            ? new SavedRequestDto(path.Value, RequestMapper.ToDto(definition.Value))
            : written.Error!;
    }

    /// <summary>
    /// The first file name for <paramref name="name"/> in <paramref name="folder"/> that is free
    /// (or is <paramref name="current"/> itself).
    /// </summary>
    internal static WorkspacePath? FreePath(
        IWorkspaceRepository repository,
        string root,
        WorkspacePath folder,
        string name,
        WorkspacePath? current)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var candidate = folder.Combine(WorkspaceLayout.RequestFileName(name, attempt));
            if (candidate == current || !repository.FileExists(root, candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
