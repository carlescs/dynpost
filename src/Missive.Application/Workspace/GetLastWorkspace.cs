using Missive.Application.Common;
using Missive.Application.Settings;

namespace Missive.Application.Workspace;

/// <param name="Workspace">The workspace to reopen, or <c>null</c> when there is none.</param>
public sealed record LastWorkspace(WorkspaceInfo? Workspace);

/// <summary>
/// Finds the workspace opened last, to reopen it at startup. A remembered folder that no longer
/// exists is ignored (but kept, in case it is on a drive that is not mounted right now).
/// </summary>
public sealed class GetLastWorkspace(IAppSettingsStore settings, IWorkspaceRepository workspaces)
{
    public async Task<Result<LastWorkspace>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var path = (await settings.LoadAsync(cancellationToken)).LastWorkspacePath;
        if (string.IsNullOrWhiteSpace(path) || !await workspaces.ExistsAsync(path, cancellationToken))
        {
            return new LastWorkspace(null);
        }

        return new LastWorkspace(WorkspaceInfo.FromPath(path));
    }
}
