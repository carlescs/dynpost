using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Git;

namespace Dynpost.Application.Git;

/// <param name="Outcome">Whether anything was pulled, or the merge stopped on conflicts.</param>
/// <param name="Status">The workspace's git state after the pull; lists any conflicted files.</param>
public sealed record PullChangesResult(PullOutcome Outcome, GitStatus Status);

/// <summary>
/// Pulls the current branch from its upstream. A branch without one is linked to the branch of the
/// same name on the workspace's remote when it exists there, as after creating a repository locally
/// and adding the remote of one that already has commits.
/// </summary>
public sealed class PullChanges(IGitService git, CurrentWorkspace workspace)
{
    /// <param name="signIn">Credentials the user entered after a first try couldn't sign in to the remote.</param>
    public async Task<Result<PullChangesResult>> ExecuteAsync(GitSignIn? signIn = null, CancellationToken cancellationToken = default)
    {
        var root = workspace.RequireRoot();
        if (root.IsFailure)
        {
            return root.Error!;
        }

        var credentials = GitSignIn.Check(signIn);
        if (credentials.IsFailure)
        {
            return credentials.Error!;
        }

        var workspacePath = root.Value;
        var before = await git.GetStatusAsync(workspacePath, cancellationToken);
        if (before.IsFailure)
        {
            return before.Error!;
        }

        var status = before.Value;
        if (status.Branch is not { } branch)
        {
            return GitErrors.DetachedHead();
        }

        if (status.HasConflicts)
        {
            return GitErrors.UnresolvedConflicts(status.Conflicts);
        }

        if (status.IsUpstreamGone)
        {
            return GitErrors.UpstreamGone(status.Upstream!);
        }

        // The first pull from a branch that was never linked may join two separate histories (a
        // local repository and a remote one created with, say, a README).
        var linking = !status.HasUpstream;
        if (linking)
        {
            var tracked = await git.TrackRemoteBranchAsync(workspacePath, branch, credentials.Value, cancellationToken);
            if (tracked.IsFailure)
            {
                return tracked.Error!;
            }

            if (!tracked.Value)
            {
                return GitErrors.NoUpstream(branch);
            }
        }

        var pull = await git.PullAsync(workspacePath, allowUnrelatedHistories: linking, credentials.Value, cancellationToken);
        if (pull.IsFailure)
        {
            return pull.Error!;
        }

        var after = await git.GetStatusAsync(workspacePath, cancellationToken);
        if (after.IsFailure)
        {
            return after.Error!;
        }

        return new PullChangesResult(pull.Value, after.Value);
    }
}
