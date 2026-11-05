using Missive.Domain.Git;

namespace Missive.Application.Git;

// Wire format of the git messages, sent to the UI by the bridge. Keep in sync with
// src/Missive.Client/src/app/bridge/messages.ts.

/// <param name="Status">One of <c>modified</c>, <c>added</c>, <c>deleted</c>, <c>renamed</c>, <c>untracked</c>, <c>conflicted</c>.</param>
public sealed record GitChangeDto(string Path, string Status, string? OriginalPath);

public sealed record GitStatusDto(
    string? Branch,
    string? Upstream,
    int Ahead,
    int Behind,
    bool Merging,
    IReadOnlyList<GitChangeDto> Changes,
    string? MergeMessage = null)
{
    public static GitStatusDto From(GitStatus status) =>
        new(
            status.Branch,
            status.Upstream,
            status.Ahead,
            status.Behind,
            status.IsMerging,
            status.Changes.Select(c => new GitChangeDto(c.Path, StatusName(c.Status), c.OriginalPath)).ToList(),
            status.MergeMessage);

    private static string StatusName(GitFileStatus status) =>
        status switch
        {
            GitFileStatus.Modified => "modified",
            GitFileStatus.Added => "added",
            GitFileStatus.Deleted => "deleted",
            GitFileStatus.Renamed => "renamed",
            GitFileStatus.Untracked => "untracked",
            GitFileStatus.Conflicted => "conflicted",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
}

public sealed record GitCommitDto(string CommitId, GitStatusDto Status)
{
    public static GitCommitDto From(CommitChangesResult result) => new(result.CommitId, GitStatusDto.From(result.Status));
}

/// <param name="Outcome">One of <c>upToDate</c>, <c>updated</c>, <c>conflicted</c>.</param>
public sealed record GitPullDto(string Outcome, GitStatusDto Status)
{
    public static GitPullDto From(PullChangesResult result) =>
        new(
            result.Outcome switch
            {
                PullOutcome.UpToDate => "upToDate",
                PullOutcome.Updated => "updated",
                _ => "conflicted",
            },
            GitStatusDto.From(result.Status));
}

/// <summary>Both sides of a conflicted file; a side is <c>null</c> when it deleted the file.</summary>
public sealed record GitConflictDto(
    string Path,
    string? Base,
    string? Ours,
    string? Theirs,
    string? Working,
    bool HasConflictMarkers)
{
    public static GitConflictDto From(GitConflict conflict) =>
        new(conflict.Path, conflict.Base, conflict.Ours, conflict.Theirs, conflict.Working, conflict.HasConflictMarkers);
}
