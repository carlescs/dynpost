using Missive.Application.Common;
using Missive.Application.Workspaces;
using Missive.Domain.Git;

namespace Missive.Application.Git;

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
