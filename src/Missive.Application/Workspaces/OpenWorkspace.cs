using Missive.Application.Common;

namespace Missive.Application.Workspaces;

/// <summary>Opens a folder as the current workspace and lists its requests.</summary>
public sealed class OpenWorkspace(IWorkspaceRepository repository, CurrentWorkspace current)
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
        return WorkspaceTreeBuilder.Build(folder, entries.Value);
    }
}
