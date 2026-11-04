using Missive.Application.Common;
using Missive.Domain.Git;

namespace Missive.Application.Git;

/// <param name="WorkspacePath">The workspace folder.</param>
/// <param name="Message">The commit message entered by the user.</param>
/// <param name="Paths">
/// Workspace-relative paths of the changed files to commit, or <c>null</c>/empty to commit every change.
/// </param>
public sealed record CommitChangesRequest(string WorkspacePath, string Message, IReadOnlyList<string>? Paths = null);

/// <param name="CommitId">The id of the new commit.</param>
/// <param name="Status">The workspace's git state after the commit.</param>
public sealed record CommitChangesResult(string CommitId, GitStatus Status);

/// <summary>Commits the user's changes in the workspace with a message.</summary>
public sealed class CommitChanges(IGitService git)
{
    public async Task<Result<CommitChangesResult>> ExecuteAsync(
        CommitChangesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!CommitMessage.TryCreate(request.Message, out var message))
        {
            return GitErrors.InvalidCommitMessage();
        }

        var before = await git.GetStatusAsync(request.WorkspacePath, cancellationToken);
        if (before.IsFailure)
        {
            return before.Error!;
        }

        var status = before.Value;
        if (status.HasConflicts)
        {
            return GitErrors.UnresolvedConflicts(status.Conflicts);
        }

        var paths = SelectPaths(request.Paths, status, out var error);
        if (error is not null)
        {
            return error;
        }

        var commit = await git.CommitAsync(request.WorkspacePath, message, paths, cancellationToken);
        if (commit.IsFailure)
        {
            return commit.Error!;
        }

        var after = await git.GetStatusAsync(request.WorkspacePath, cancellationToken);
        if (after.IsFailure)
        {
            return after.Error!;
        }

        return new CommitChangesResult(commit.Value, after.Value);
    }

    /// <summary>
    /// Validates the requested paths against the current changes. Returns <c>null</c> to commit
    /// everything, or the pathspecs to commit (including the source of a rename, so it is not left
    /// behind as a staged deletion).
    /// </summary>
    private static IReadOnlyList<string>? SelectPaths(IReadOnlyList<string>? requested, GitStatus status, out Error? error)
    {
        error = null;

        if (requested is null || requested.Count == 0)
        {
            if (!status.HasChanges)
            {
                error = GitErrors.NothingToCommit();
            }

            return null;
        }

        if (status.IsMerging)
        {
            error = GitErrors.PartialCommitDuringMerge();
            return null;
        }

        var changes = status.Changes.ToDictionary(c => c.Path, StringComparer.Ordinal);
        var unchanged = requested.Where(p => !changes.ContainsKey(p)).Distinct(StringComparer.Ordinal).ToList();
        if (unchanged.Count > 0)
        {
            error = GitErrors.PathsNotChanged(unchanged);
            return null;
        }

        var paths = new List<string>();
        foreach (var path in requested.Distinct(StringComparer.Ordinal))
        {
            paths.Add(path);
            if (changes[path].OriginalPath is { } original)
            {
                paths.Add(original);
            }
        }

        return paths.Distinct(StringComparer.Ordinal).ToList();
    }
}
