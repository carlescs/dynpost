using Dynypost.Application.Common;
using Dynypost.Application.Workspaces;
using Dynypost.Domain.Git;

namespace Dynypost.Application.Git;

/// <summary>The workspace's git status and branches, as returned by every branch use case.</summary>
public sealed record GitBranchesResult(GitStatus Status, IReadOnlyList<GitBranch> Branches);

/// <summary>Lists the local and remote-tracking branches of the open workspace's repository.</summary>
public sealed class ListBranches(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<GitBranchesResult>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var root = workspace.RequireRoot();
        return root.IsFailure ? root.Error! : await BranchContext.ReadAsync(git, root.Value, cancellationToken);
    }
}

/// <summary>
/// Checks out another local branch. Refused while there are uncommitted changes in the workspace
/// (they are named) or a merge is in progress, so switching never carries changes over or loses
/// them; nothing is forced.
/// </summary>
public sealed class SwitchBranch(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<GitBranchesResult>> ExecuteAsync(string name, CancellationToken cancellationToken = default)
    {
        var context = await BranchContext.LoadAsync(git, workspace, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var (root, current) = context.Value;
        if (BranchContext.Local(current.Branches, name) is not { } branch)
        {
            return GitErrors.BranchNotFound(name?.Trim() ?? string.Empty);
        }

        if (branch.IsCurrent)
        {
            return current;
        }

        var clean = BranchContext.RequireClean(current.Status);
        if (clean.IsFailure)
        {
            return clean.Error!;
        }

        var switched = await git.SwitchBranchAsync(root, branch.Name, cancellationToken);
        return switched.IsFailure ? switched.Error! : await BranchContext.ReadAsync(git, root, cancellationToken);
    }
}

/// <param name="Name">The new branch's name.</param>
/// <param name="SwitchTo">Whether to check it out once created.</param>
public sealed record CreateBranchRequest(string Name, bool SwitchTo);

/// <summary>
/// Creates a branch at the current commit, optionally checking it out. Uncommitted changes come
/// along to the new branch, which points to the same commit, so nothing can be overwritten.
/// </summary>
public sealed class CreateBranch(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<GitBranchesResult>> ExecuteAsync(CreateBranchRequest request, CancellationToken cancellationToken = default)
    {
        if (!BranchName.TryCreate(request.Name, out var name, out var nameError))
        {
            return GitErrors.InvalidBranchName(nameError);
        }

        var context = await BranchContext.LoadAsync(git, workspace, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var (root, current) = context.Value;
        if (current.Branches.Any(b => !b.IsRemote && string.Equals(b.Name, name.Value, StringComparison.OrdinalIgnoreCase)))
        {
            return GitErrors.BranchExists(name.Value);
        }

        if (current.Status.IsUnborn)
        {
            return GitErrors.NoCommitsToBranchFrom();
        }

        if (request.SwitchTo && current.Status.IsMerging)
        {
            return GitErrors.BranchDuringMerge();
        }

        var created = await git.CreateBranchAsync(root, name, request.SwitchTo, cancellationToken);
        return created.IsFailure ? created.Error! : await BranchContext.ReadAsync(git, root, cancellationToken);
    }
}

/// <summary>
/// Checks out a remote-tracking branch as a new local branch of the same name that tracks it.
/// Refused like <see cref="SwitchBranch"/> while there are uncommitted changes or a merge.
/// </summary>
public sealed class CheckoutRemoteBranch(IGitService git, CurrentWorkspace workspace)
{
    /// <param name="remoteBranch">The remote-tracking branch, e.g. <c>origin/feature</c>.</param>
    public async Task<Result<GitBranchesResult>> ExecuteAsync(string remoteBranch, CancellationToken cancellationToken = default)
    {
        var context = await BranchContext.LoadAsync(git, workspace, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var (root, current) = context.Value;
        var remote = current.Branches.FirstOrDefault(b => b.IsRemote && string.Equals(b.Name, remoteBranch?.Trim(), StringComparison.Ordinal));
        if (remote is null)
        {
            return GitErrors.BranchNotFound(remoteBranch?.Trim() ?? string.Empty);
        }

        if (!BranchName.TryCreate(remote.LocalName, out var localName, out var nameError))
        {
            return GitErrors.InvalidBranchName(nameError);
        }

        if (current.Branches.Any(b => !b.IsRemote && string.Equals(b.Name, localName.Value, StringComparison.OrdinalIgnoreCase)))
        {
            return GitErrors.LocalBranchExists(localName.Value);
        }

        var clean = BranchContext.RequireClean(current.Status);
        if (clean.IsFailure)
        {
            return clean.Error!;
        }

        var checkedOut = await git.CheckoutRemoteBranchAsync(root, remote.Name, localName, cancellationToken);
        return checkedOut.IsFailure ? checkedOut.Error! : await BranchContext.ReadAsync(git, root, cancellationToken);
    }
}

/// <param name="Name">The local branch to delete.</param>
/// <param name="Force">
/// Delete it even if it has commits that aren't merged anywhere: only after the user confirmed
/// a second time that they will be lost.
/// </param>
public sealed record DeleteBranchRequest(string Name, bool Force = false);

/// <summary>
/// Deletes a local branch, never the current one; the remote is left alone (see <see cref="DeleteRemoteBranch"/>). A branch whose commits
/// would be lost is only deleted with <see cref="DeleteBranchRequest.Force"/>.
/// </summary>
public sealed class DeleteBranch(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<GitBranchesResult>> ExecuteAsync(DeleteBranchRequest request, CancellationToken cancellationToken = default)
    {
        var context = await BranchContext.LoadAsync(git, workspace, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var (root, current) = context.Value;
        if (BranchContext.Local(current.Branches, request.Name) is not { } branch)
        {
            return GitErrors.BranchNotFound(request.Name?.Trim() ?? string.Empty);
        }

        if (branch.IsCurrent)
        {
            return GitErrors.CurrentBranch(branch.Name);
        }

        var deleted = await git.DeleteBranchAsync(root, branch.Name, request.Force, cancellationToken);
        return deleted.IsFailure ? deleted.Error! : await BranchContext.ReadAsync(git, root, cancellationToken);
    }
}

/// <summary>
/// Deletes a branch on its remote after the user confirmed it, so it is gone for everyone who
/// fetches. Local branches, including one that tracked it, are kept; the current branch's
/// upstream then shows as deleted.
/// </summary>
public sealed class DeleteRemoteBranch(IGitService git, CurrentWorkspace workspace)
{
    /// <param name="remoteBranch">The remote-tracking branch, e.g. <c>origin/feature</c>.</param>
    public async Task<Result<GitBranchesResult>> ExecuteAsync(string remoteBranch, CancellationToken cancellationToken = default)
    {
        var context = await BranchContext.LoadAsync(git, workspace, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var (root, current) = context.Value;
        var trimmed = remoteBranch?.Trim() ?? string.Empty;
        var branch = current.Branches.FirstOrDefault(b => b.IsRemote && string.Equals(b.Name, trimmed, StringComparison.Ordinal));
        if (branch is null)
        {
            return GitErrors.BranchNotFound(trimmed);
        }

        var deleted = await git.DeleteRemoteBranchAsync(root, branch, cancellationToken);
        return deleted.IsFailure ? deleted.Error! : await BranchContext.ReadAsync(git, root, cancellationToken);
    }
}

/// <param name="Name">The branch to merge: <c>feature</c>, or <c>origin/feature</c>.</param>
/// <param name="Remote">
/// Whether it is a remote-tracking branch: a local branch may have the same name as a remote one
/// (a local <c>origin/feature</c>).
/// </param>
public sealed record MergeBranchRequest(string Name, bool Remote = false);

/// <param name="Outcome">Whether anything was merged, or the merge stopped on conflicts.</param>
/// <param name="Status">The workspace's git state afterwards; lists any conflicted files.</param>
/// <param name="Branches">The branches afterwards.</param>
/// <param name="ConflictsOutsideWorkspace">Conflicted files outside the workspace folder, which Dynypost can't resolve.</param>
public sealed record MergeBranchResult(
    MergeOutcome Outcome,
    GitStatus Status,
    IReadOnlyList<GitBranch> Branches,
    IReadOnlyList<string> ConflictsOutsideWorkspace);

/// <summary>
/// Merges another local or remote-tracking branch into the current branch, never rebasing. Refused
/// on a detached HEAD, a branch without commits, during a merge, and while there are uncommitted
/// changes in the workspace (they are named; the adapter also checks the rest of the repository),
/// so aborting a conflicted merge always restores the branch exactly.
/// Conflicts are left for the user to resolve like those of a pull.
/// </summary>
public sealed class MergeBranch(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<MergeBranchResult>> ExecuteAsync(MergeBranchRequest request, CancellationToken cancellationToken = default)
    {
        var context = await BranchContext.LoadAsync(git, workspace, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var (root, current) = context.Value;
        var trimmed = request.Name?.Trim() ?? string.Empty;
        var branch = current.Branches.FirstOrDefault(
            b => b.IsRemote == request.Remote && string.Equals(b.Name, trimmed, StringComparison.Ordinal));
        if (branch is null)
        {
            return GitErrors.BranchNotFound(trimmed);
        }

        var status = current.Status;
        if (status.Branch is not { } currentBranch)
        {
            return GitErrors.DetachedHead();
        }

        if (branch.IsCurrent)
        {
            return GitErrors.MergeSameBranch(currentBranch);
        }

        if (status.IsUnborn)
        {
            return GitErrors.NoCommitsToMergeInto();
        }

        if (status.IsMerging)
        {
            return GitErrors.MergeDuringMerge();
        }

        if (status.HasChanges)
        {
            return GitErrors.UncommittedChangesBeforeMerge(status.Changes.Select(c => c.Path).ToList());
        }

        var merged = await git.MergeBranchAsync(root, branch, cancellationToken);
        if (merged.IsFailure)
        {
            return merged.Error!;
        }

        var after = await BranchContext.ReadAsync(git, root, cancellationToken);
        return after.IsFailure
            ? after.Error!
            : new MergeBranchResult(
                merged.Value.Outcome,
                after.Value.Status,
                after.Value.Branches,
                merged.Value.ConflictsOutsideWorkspace);
    }
}

internal static class BranchContext
{
    public static async Task<Result<(string Root, GitBranchesResult Current)>> LoadAsync(
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
            : Result<(string, GitBranchesResult)>.Success((root.Value, current.Value));
    }

    public static async Task<Result<GitBranchesResult>> ReadAsync(IGitService git, string root, CancellationToken cancellationToken)
    {
        var status = await git.GetStatusAsync(root, cancellationToken);
        if (status.IsFailure)
        {
            return status.Error!;
        }

        var branches = await git.ListBranchesAsync(root, cancellationToken);
        return branches.IsFailure ? branches.Error! : new GitBranchesResult(status.Value, branches.Value);
    }

    /// <summary>Only existing branches are acted on, so a name can't be read as anything else.</summary>
    public static GitBranch? Local(IReadOnlyList<GitBranch> branches, string? name) =>
        branches.FirstOrDefault(b => !b.IsRemote && string.Equals(b.Name, name?.Trim(), StringComparison.Ordinal));

    /// <summary>Switching needs a clean workspace: no merge in progress and no uncommitted changes.</summary>
    public static Result<Unit> RequireClean(GitStatus status)
    {
        if (status.IsMerging)
        {
            return GitErrors.BranchDuringMerge();
        }

        return status.HasChanges
            ? GitErrors.UncommittedChanges(status.Changes.Select(c => c.Path).ToList())
            : Unit.Value;
    }
}
