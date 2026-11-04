using Missive.Application.Common;
using Missive.Domain.Git;

namespace Missive.Application.Git;

/// <param name="Outcome">Whether anything was pulled, or the merge stopped on conflicts.</param>
/// <param name="Status">The workspace's git state after the pull; lists any conflicted files.</param>
public sealed record PullChangesResult(PullOutcome Outcome, GitStatus Status);

/// <summary>Pulls the current branch from its upstream.</summary>
public sealed class PullChanges(IGitService git)
{
    public async Task<Result<PullChangesResult>> ExecuteAsync(
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
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

        if (!status.HasUpstream)
        {
            return GitErrors.NoUpstream(branch);
        }

        if (status.HasConflicts)
        {
            return GitErrors.UnresolvedConflicts(status.Conflicts);
        }

        var pull = await git.PullAsync(workspacePath, cancellationToken);
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
