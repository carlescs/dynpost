using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Git;

namespace Dynpost.Application.Git;

public enum StashApplyOutcome
{
    /// <summary>The stash's changes are back in the working tree.</summary>
    Applied,

    /// <summary>
    /// Some changes conflicted with the current files; the conflicts are left for the user to
    /// resolve, and the stash entry is kept.
    /// </summary>
    Conflicted,
}

/// <param name="Outcome">Whether the stash applied cleanly or stopped on conflicts.</param>
/// <param name="ConflictsOutsideWorkspace">
/// Conflicted files outside the workspace folder, relative to the repository root, which Dynpost
/// can't resolve.
/// </param>
public sealed record StashApplyResult(StashApplyOutcome Outcome, IReadOnlyList<string> ConflictsOutsideWorkspace)
{
    public static StashApplyResult Of(StashApplyOutcome outcome) => new(outcome, []);
}

/// <summary>The workspace's git status and the stash list, as returned by every stash use case.</summary>
public sealed record GitStashesResult(GitStatus Status, IReadOnlyList<GitStash> Stashes);

/// <param name="Outcome">Whether the stash applied cleanly or stopped on conflicts.</param>
/// <param name="Status">The workspace's git state afterwards; lists any conflicted files.</param>
/// <param name="Stashes">The stash list afterwards.</param>
/// <param name="ConflictsOutsideWorkspace">Conflicted files outside the workspace folder.</param>
public sealed record ApplyStashResult(
    StashApplyOutcome Outcome,
    GitStatus Status,
    IReadOnlyList<GitStash> Stashes,
    IReadOnlyList<string> ConflictsOutsideWorkspace);

/// <summary>Lists the stash entries of the open workspace's repository.</summary>
public sealed class ListStashes(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<GitStashesResult>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var root = workspace.RequireRoot();
        return root.IsFailure ? root.Error! : await StashContext.ReadAsync(git, root.Value, cancellationToken);
    }
}

/// <param name="Message">What the changes are; blank lets git describe them by the last commit.</param>
public sealed record StashChangesRequest(string? Message = null);

/// <summary>
/// Sets every uncommitted change in the workspace aside, new files included, so the workspace is
/// back to its last commit. Refused when there is nothing to stash, before the first commit, during
/// a merge and while files are conflicted.
/// </summary>
public sealed class StashChanges(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<GitStashesResult>> ExecuteAsync(StashChangesRequest request, CancellationToken cancellationToken = default)
    {
        var context = await StashContext.LoadAsync(git, workspace, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var (root, current) = context.Value;
        var status = current.Status;
        if (status.IsUnborn)
        {
            return GitErrors.NoCommitsToStash();
        }

        if (status.IsMerging)
        {
            return GitErrors.StashDuringMerge();
        }

        if (status.HasConflicts)
        {
            return GitErrors.UnresolvedConflicts(status.Conflicts);
        }

        if (!status.HasChanges)
        {
            return GitErrors.NothingToStash();
        }

        var stashed = await git.StashAsync(root, GitStash.NormalizeMessage(request.Message), cancellationToken);
        return stashed.IsFailure ? stashed.Error! : await StashContext.ReadAsync(git, root, cancellationToken);
    }
}

/// <param name="Id">The stash entry's commit id.</param>
/// <param name="Pop">Whether to drop the entry once it applied without conflicts.</param>
public sealed record ApplyStashRequest(string Id, bool Pop = false);

/// <summary>
/// Brings a stash entry's changes back into the workspace, and with <see cref="ApplyStashRequest.Pop"/>
/// drops the entry. Refused during a merge, while files are conflicted and while the workspace has
/// uncommitted changes (they are named), so a stash never mixes with other edits and can't be left
/// half applied. Conflicts with the current commit are left for the user to resolve, and the entry
/// is kept.
/// </summary>
public sealed class ApplyStash(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<ApplyStashResult>> ExecuteAsync(ApplyStashRequest request, CancellationToken cancellationToken = default)
    {
        var context = await StashContext.LoadAsync(git, workspace, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var (root, current) = context.Value;
        if (StashContext.Find(current.Stashes, request.Id) is not { } stash)
        {
            return GitErrors.StashNotFound();
        }

        var status = current.Status;
        if (status.IsMerging)
        {
            return GitErrors.StashDuringMerge();
        }

        if (status.HasConflicts)
        {
            return GitErrors.UnresolvedConflicts(status.Conflicts);
        }

        if (status.HasChanges)
        {
            return GitErrors.UncommittedChangesBeforeStashApply(status.Changes.Select(c => c.Path).ToList());
        }

        var applied = await git.ApplyStashAsync(root, stash, request.Pop, cancellationToken);
        if (applied.IsFailure)
        {
            return applied.Error!;
        }

        var after = await StashContext.ReadAsync(git, root, cancellationToken);
        return after.IsFailure
            ? after.Error!
            : new ApplyStashResult(applied.Value.Outcome, after.Value.Status, after.Value.Stashes, applied.Value.ConflictsOutsideWorkspace);
    }
}

/// <summary>Deletes a stash entry, after the user confirmed that its changes will be lost.</summary>
public sealed class DropStash(IGitService git, CurrentWorkspace workspace)
{
    /// <param name="id">The stash entry's commit id.</param>
    public async Task<Result<GitStashesResult>> ExecuteAsync(string id, CancellationToken cancellationToken = default)
    {
        var context = await StashContext.LoadAsync(git, workspace, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var (root, current) = context.Value;
        if (StashContext.Find(current.Stashes, id) is not { } stash)
        {
            return GitErrors.StashNotFound();
        }

        var dropped = await git.DropStashAsync(root, stash, cancellationToken);
        return dropped.IsFailure ? dropped.Error! : await StashContext.ReadAsync(git, root, cancellationToken);
    }
}

internal static class StashContext
{
    public static async Task<Result<(string Root, GitStashesResult Current)>> LoadAsync(
        IGitService git,
        CurrentWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var root = workspace.RequireRoot();
        if (root.IsFailure)
        {
            return root.Error!;
        }

        var current = await ReadAsync(git, root.Value, cancellationToken);
        return current.IsFailure
            ? current.Error!
            : Result<(string, GitStashesResult)>.Success((root.Value, current.Value));
    }

    public static async Task<Result<GitStashesResult>> ReadAsync(IGitService git, string root, CancellationToken cancellationToken)
    {
        var status = await git.GetStatusAsync(root, cancellationToken);
        if (status.IsFailure)
        {
            return status.Error!;
        }

        var stashes = await git.ListStashesAsync(root, cancellationToken);
        return stashes.IsFailure ? stashes.Error! : new GitStashesResult(status.Value, stashes.Value);
    }

    /// <summary>Entries are named by commit id, so one that moved in the list is still the one acted on.</summary>
    public static GitStash? Find(IReadOnlyList<GitStash> stashes, string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : stashes.FirstOrDefault(s => string.Equals(s.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
}
