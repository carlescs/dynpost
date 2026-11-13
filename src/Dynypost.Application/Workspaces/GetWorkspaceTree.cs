using Dynypost.Application.Common;
using Dynypost.Application.Sharing;

namespace Dynypost.Application.Workspaces;

/// <summary>Re-reads the open workspace from disk.</summary>
public sealed class GetWorkspaceTree(IWorkspaceRepository repository, ILocalOnlyFiles localFiles, CurrentWorkspace current)
{
    public async Task<Result<WorkspaceDto>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        var entries = await repository.ListAsync(root.Value, cancellationToken);
        return entries.IsSuccess ? WorkspaceTreeBuilder.Build(root.Value, entries.Value, LocalOnlyPaths.ListOrEmpty(localFiles, root.Value)) : entries.Error!;
    }
}
