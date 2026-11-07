using Missive.Application.Common;
using Missive.Application.Settings;

namespace Missive.Application.Workspaces;

/// <summary>
/// Opens a folder as the current workspace and lists its requests. The folder is remembered as
/// the one to reopen at the next launch and added to the top of the recent workspaces.
/// </summary>
public sealed class OpenWorkspace(IWorkspaceRepository repository, CurrentWorkspace current, IAppSettingsStore settings)
{
    public async Task<Result<WorkspaceDto>> ExecuteAsync(string folder, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folder) || !repository.FolderExists(folder))
        {
            return new Error(ErrorCodes.NotFound, $"The folder '{folder}' does not exist.");
        }

        var entries = await repository.ListAsync(folder, cancellationToken);
        if (!entries.IsSuccess)
        {
            return entries.Error!;
        }

        current.Open(folder);
        await RememberAsync(folder, cancellationToken);
        return WorkspaceTreeBuilder.Build(folder, entries.Value);
    }

    /// <summary>Best effort: failing to remember the folder must not stop the user from opening it.</summary>
    private async Task RememberAsync(string folder, CancellationToken cancellationToken)
    {
        await settings.UpdateAsync(
            saved => saved with
            {
                LastWorkspacePath = folder,
                RecentWorkspaces = RecentWorkspaces.Add(saved.RecentWorkspaces, folder),
            },
            cancellationToken);
    }
}
