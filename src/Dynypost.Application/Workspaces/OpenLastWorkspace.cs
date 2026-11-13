using Dynypost.Application.Common;
using Dynypost.Application.Settings;

namespace Dynypost.Application.Workspaces;

/// <summary>
/// Reopens the workspace from the previous session at startup. Returns <c>null</c> when none is
/// remembered or its folder is gone; the remembered path is kept in that case, in case the folder
/// is on a drive that is not mounted right now.
/// </summary>
public sealed class OpenLastWorkspace(IAppSettingsStore settings, IWorkspaceRepository repository, OpenWorkspace open)
{
    public async Task<Result<WorkspaceDto?>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var folder = (await settings.LoadAsync(cancellationToken)).LastWorkspacePath;
        if (string.IsNullOrWhiteSpace(folder) || !repository.FolderExists(folder))
        {
            return Result<WorkspaceDto?>.Success(null);
        }

        var opened = await open.ExecuteAsync(folder, cancellationToken);
        return opened.IsSuccess ? Result<WorkspaceDto?>.Success(opened.Value) : Result<WorkspaceDto?>.Failure(opened.Error!);
    }
}
