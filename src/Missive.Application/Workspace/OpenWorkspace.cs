using Missive.Application.Common;
using Missive.Application.Settings;

namespace Missive.Application.Workspace;

/// <summary>Opens a workspace folder and remembers it as the one to reopen at the next launch.</summary>
public sealed class OpenWorkspace(IWorkspaceRepository workspaces, IAppSettingsStore settings)
{
    public async Task<Result<WorkspaceInfo>> ExecuteAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || !await workspaces.ExistsAsync(workspacePath, cancellationToken))
        {
            return WorkspaceErrors.NotFound(workspacePath);
        }

        var current = await settings.LoadAsync(cancellationToken);
        if (current.LastWorkspacePath != workspacePath)
        {
            // Best effort: failing to remember the workspace must not stop the user from opening it.
            await settings.SaveAsync(current with { LastWorkspacePath = workspacePath }, cancellationToken);
        }

        return WorkspaceInfo.FromPath(workspacePath);
    }
}
