using Dynypost.Application.Common;
using Dynypost.Application.Workspaces;
using Dynypost.Domain.Git;

namespace Dynypost.Application.Git;

/// <summary>
/// Pushes the current branch. A branch without an upstream is published to the workspace's remote
/// and tracks it from then on.
/// </summary>
public sealed class PushChanges(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<GitStatus>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var root = workspace.RequireRoot();
        if (root.IsFailure)
        {
            return root.Error!;
        }

        var workspacePath = root.Value;
        var before = await git.GetStatusAsync(workspacePath, cancellationToken);
        if (before.IsFailure)
        {
            return before.Error!;
        }

        var status = before.Value;
        if (status.IsDetached)
        {
            return GitErrors.DetachedHead();
        }

        if (status.HasConflicts)
        {
            return GitErrors.UnresolvedConflicts(status.Conflicts);
        }

        if (status.IsUnborn)
        {
            return GitErrors.NoCommits();
        }

        var push = await git.PushAsync(workspacePath, setUpstream: !status.HasUpstream, cancellationToken);
        if (push.IsFailure)
        {
            return push.Error!;
        }

        return await git.GetStatusAsync(workspacePath, cancellationToken);
    }
}
