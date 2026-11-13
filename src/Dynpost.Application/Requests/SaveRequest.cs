using Dynpost.Application.Common;
using Dynpost.Application.History;
using Dynpost.Application.Sharing;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Application.Requests;

/// <summary>
/// Saves changes to an existing request file. When the request is renamed and its file name is
/// still the slug of the old name, the file is renamed to match the new name; files with
/// hand-picked names keep them.
/// </summary>
public sealed class SaveRequest(IWorkspaceRepository repository, ILocalOnlyFiles localFiles, IHistoryStore history, CurrentWorkspace current)
{
    public async Task<Result<SavedRequestDto>> ExecuteAsync(string path, RequestDto request, CancellationToken cancellationToken = default)
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

        var definition = RequestMapper.ToDomain(request);
        if (!definition.IsSuccess)
        {
            return definition.Error!;
        }

        var target = await RenameTargetAsync(root.Value, requestPath, definition.Value.Name, cancellationToken);

        var written = await repository.WriteRequestAsync(root.Value, requestPath, definition.Value, cancellationToken);
        if (!written.IsSuccess)
        {
            return written.Error!;
        }

        var savedPath = requestPath;
        if (target is not null && target != requestPath)
        {
            var moved = repository.MoveFile(root.Value, requestPath, target);
            if (!moved.IsSuccess)
            {
                return moved.Error!;
            }

            savedPath = target;
            history.Move(root.Value, requestPath, target);

            // A local-only request must stay out of git under its new file name.
            var followed = localFiles.Move(root.Value, requestPath, target);
            if (!followed.IsSuccess)
            {
                return followed.Error!;
            }
        }

        return new SavedRequestDto(savedPath.Value, RequestMapper.ToDto(definition.Value));
    }

    private async Task<WorkspacePath?> RenameTargetAsync(
        string root,
        WorkspacePath path,
        string newName,
        CancellationToken cancellationToken)
    {
        if (!repository.FileExists(root, path))
        {
            return null;
        }

        var existing = await repository.ReadRequestAsync(root, path, cancellationToken);
        if (!existing.IsSuccess || existing.Value.Name == newName)
        {
            return null;
        }

        var stem = WorkspaceLayout.FileStem(path.FileName);
        if (stem != Slug.From(existing.Value.Name) || Slug.From(newName) == Slug.From(existing.Value.Name))
        {
            return null;
        }

        return CreateRequest.FreePath(repository, root, path.Parent, newName, path);
    }
}
