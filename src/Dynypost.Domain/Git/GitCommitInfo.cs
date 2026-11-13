namespace Dynypost.Domain.Git;

/// <summary>A commit in the workspace's history, as shown in a list.</summary>
/// <param name="Id">The full commit id (hash).</param>
/// <param name="ParentIds">The parent commits: none for a root commit, two or more for a merge.</param>
/// <param name="AuthorName">The author's name.</param>
/// <param name="AuthorEmail">The author's email address.</param>
/// <param name="AuthoredAt">When the commit was authored.</param>
/// <param name="Subject">The first line of the commit message.</param>
public sealed record GitCommitInfo(
    string Id,
    IReadOnlyList<string> ParentIds,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthoredAt,
    string Subject)
{
    public bool IsMerge => ParentIds.Count > 1;

    /// <summary>The commit the changes are compared with: the first parent, or <c>null</c> for a root commit.</summary>
    public string? FirstParentId => ParentIds.Count > 0 ? ParentIds[0] : null;
}

/// <summary>A commit with its full message and the workspace files it changed.</summary>
/// <param name="Commit">The commit.</param>
/// <param name="Body">The commit message after the subject line; empty when there is none.</param>
/// <param name="Files">
/// Files inside the workspace folder that changed, compared with the first parent (everything is
/// added in a root commit). Never includes <c>.dynypost/</c>.
/// </param>
public sealed record GitCommitDetails(GitCommitInfo Commit, string Body, IReadOnlyList<GitChange> Files);

/// <summary>A file before and after a commit. A version is <c>null</c> when the file didn't exist there.</summary>
/// <param name="CommitId">The commit.</param>
/// <param name="Change">How the file changed in it.</param>
/// <param name="Before">The file in the commit's first parent.</param>
/// <param name="After">The file in the commit.</param>
public sealed record GitFileRevision(string CommitId, GitChange Change, string? Before, string? After)
{
    /// <summary>Whether either version looks binary, so it shouldn't be shown as text.</summary>
    public bool IsBinary =>
        (Before is not null && GitFileDiff.LooksBinary(Before)) || (After is not null && GitFileDiff.LooksBinary(After));
}
