using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Git;

namespace Dynpost.Application.Git;

/// <summary>Reads both sides of a conflicted file so the user can compare them.</summary>
public sealed class GetConflict(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<GitConflict>> ExecuteAsync(string path, CancellationToken cancellationToken = default)
    {
        var context = await MergeContext.LoadAsync(git, workspace, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var (root, status) = context.Value;
        if (!MergeContext.IsConflicted(status, path))
        {
            return GitErrors.NotConflicted(path);
        }

        return await git.GetConflictAsync(root, path, cancellationToken);
    }
}

/// <param name="Path">The conflicted file, relative to the workspace folder.</param>
/// <param name="Resolution">The user's choice for it.</param>
public sealed record ResolveConflictRequest(string Path, ConflictResolution Resolution);

/// <summary>
/// Resolves one conflicted file the way the user chose. Keeping an edited file is refused while
/// it still has conflict markers, so a half-resolved file can't be committed by accident.
/// </summary>
public sealed class ResolveConflict(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<GitStatus>> ExecuteAsync(ResolveConflictRequest request, CancellationToken cancellationToken = default)
    {
        var context = await MergeContext.LoadAsync(git, workspace, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var (root, status) = context.Value;
        if (!MergeContext.IsConflicted(status, request.Path))
        {
            return GitErrors.NotConflicted(request.Path);
        }

        if (request.Resolution == ConflictResolution.Edited)
        {
            var conflict = await git.GetConflictAsync(root, request.Path, cancellationToken);
            if (conflict.IsFailure)
            {
                return conflict.Error!;
            }

            if (conflict.Value.HasConflictMarkers)
            {
                return GitErrors.ConflictMarkersRemain(request.Path);
            }
        }

        var resolved = await git.ResolveConflictAsync(root, request.Path, request.Resolution, cancellationToken);
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        return await git.GetStatusAsync(root, cancellationToken);
    }
}

/// <summary>Discards the user's uncommitted changes to the given files.</summary>
public sealed class DiscardChanges(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<GitStatus>> ExecuteAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        if (paths.Count == 0)
        {
            return GitErrors.NothingToDiscard();
        }

        var root = workspace.RequireRoot();
        if (root.IsFailure)
        {
            return root.Error!;
        }

        var before = await git.GetStatusAsync(root.Value, cancellationToken);
        if (before.IsFailure)
        {
            return before.Error!;
        }

        if (before.Value.IsMerging)
        {
            return GitErrors.DiscardDuringMerge();
        }

        // Only files git reports as changed can be discarded, so a path can't point elsewhere.
        var changes = before.Value.Changes
            .GroupBy(c => c.Path, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.FirstOrDefault(c => c.Status != GitFileStatus.Untracked) ?? group.First(),
                StringComparer.Ordinal);
        var distinct = paths.Distinct(StringComparer.Ordinal).ToList();
        var unchanged = distinct.Where(p => !changes.ContainsKey(p)).ToList();
        if (unchanged.Count > 0)
        {
            return GitErrors.PathsNotChanged(unchanged);
        }

        var discarded = await git.DiscardChangesAsync(root.Value, distinct.Select(p => changes[p]).ToList(), cancellationToken);
        if (discarded.IsFailure)
        {
            return discarded.Error!;
        }

        return await git.GetStatusAsync(root.Value, cancellationToken);
    }
}

/// <summary>Abandons the merge in progress, at the user's request.</summary>
public sealed class AbortMerge(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<GitStatus>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var context = await MergeContext.LoadAsync(git, workspace, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var aborted = await git.AbortMergeAsync(context.Value.Root, cancellationToken);
        if (aborted.IsFailure)
        {
            return aborted.Error!;
        }

        return await git.GetStatusAsync(context.Value.Root, cancellationToken);
    }
}

/// <summary>The open workspace and its status, required to be in the middle of a merge.</summary>
internal static class MergeContext
{
    public static async Task<Result<(string Root, GitStatus Status)>> LoadAsync(
        IGitService git,
        CurrentWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var root = workspace.RequireRoot();
        if (root.IsFailure)
        {
            return root.Error!;
        }

        var status = await git.GetStatusAsync(root.Value, cancellationToken);
        if (status.IsFailure)
        {
            return status.Error!;
        }

        if (!status.Value.IsMerging)
        {
            return GitErrors.NotMerging();
        }

        return Result<(string Root, GitStatus Status)>.Success((root.Value, status.Value));
    }

    /// <summary>Only files git reports as conflicted can be resolved, so paths can't point elsewhere.</summary>
    public static bool IsConflicted(GitStatus status, string path) =>
        status.Conflicts.Any(c => string.Equals(c.Path, path, StringComparison.Ordinal));
}
