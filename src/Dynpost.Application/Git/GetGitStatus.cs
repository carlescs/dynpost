using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Git;

namespace Dynpost.Application.Git;

/// <summary>Reads the git state of the open workspace for the source control view.</summary>
public sealed class GetGitStatus(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<GitStatus>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var root = workspace.RequireRoot();
        if (root.IsFailure)
        {
            return root.Error!;
        }

        return await git.GetStatusAsync(root.Value, cancellationToken);
    }
}
