using Missive.Application.Common;
using Missive.Domain.Git;

namespace Missive.Application.Git;

/// <summary>
/// Pushes the current branch. A branch without an upstream is published to the workspace's remote
/// and tracks it from then on.
/// </summary>
public sealed class PushChanges(IGitService git)
{
    public async Task<Result<GitStatus>> ExecuteAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
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

        var push = await git.PushAsync(workspacePath, setUpstream: !status.HasUpstream, cancellationToken);
        if (push.IsFailure)
        {
            return push.Error!;
        }

        return await git.GetStatusAsync(workspacePath, cancellationToken);
    }
}
