using Missive.Application.Common;
using Missive.Application.Workspaces;
using Missive.Domain.Git;

namespace Missive.Application.Git;

/// <summary>
/// Turns the open workspace folder into a git repository, for a workspace that is not inside one yet.
/// </summary>
public sealed class InitRepository(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<GitStatus>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var root = workspace.RequireRoot();
        if (root.IsFailure)
        {
            return root.Error!;
        }

        var init = await git.InitAsync(root.Value, cancellationToken);
        if (init.IsFailure)
        {
            return init.Error!;
        }

        return await git.GetStatusAsync(root.Value, cancellationToken);
    }
}
