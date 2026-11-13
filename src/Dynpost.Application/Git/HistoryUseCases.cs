using Dynpost.Application.Common;
using Dynpost.Application.Requests;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Git;

namespace Dynpost.Application.Git;

/// <param name="Skip">How many of the newest commits to skip, to load further pages.</param>
/// <param name="Limit">How many commits to return at most; defaults to <see cref="GetGitHistory.DefaultLimit"/>.</param>
public sealed record GetGitHistoryRequest(int Skip = 0, int? Limit = null);

/// <param name="Commits">The page of commits, newest first.</param>
/// <param name="HasMore">Whether older commits follow this page.</param>
public sealed record GitHistoryPage(IReadOnlyList<GitCommitInfo> Commits, bool HasMore);

/// <summary>Reads a page of the commits that changed files in the open workspace.</summary>
public sealed class GetGitHistory(IGitService git, CurrentWorkspace workspace)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 500;

    public async Task<Result<GitHistoryPage>> ExecuteAsync(
        GetGitHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var root = workspace.RequireRoot();
        if (root.IsFailure)
        {
            return root.Error!;
        }

        var skip = Math.Max(0, request.Skip);
        var limit = Math.Clamp(request.Limit ?? DefaultLimit, 1, MaxLimit);

        // One extra commit tells whether there is another page.
        var log = await git.GetLogAsync(root.Value, skip, limit + 1, cancellationToken);
        if (log.IsFailure)
        {
            return log.Error!;
        }

        var commits = log.Value;
        return commits.Count > limit
            ? new GitHistoryPage(commits.Take(limit).ToList(), HasMore: true)
            : new GitHistoryPage(commits, HasMore: false);
    }
}

/// <summary>Reads one commit of the open workspace, with its message and changed files.</summary>
public sealed class GetCommit(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<GitCommitDetails>> ExecuteAsync(string commitId, CancellationToken cancellationToken = default)
    {
        var root = workspace.RequireRoot();
        if (root.IsFailure)
        {
            return root.Error!;
        }

        if (!CommitIds.IsValid(commitId))
        {
            return GitErrors.CommitNotFound(commitId);
        }

        return await git.GetCommitAsync(root.Value, commitId, cancellationToken);
    }
}

/// <param name="CommitId">The commit.</param>
/// <param name="Path">A file the commit changed, relative to the workspace folder.</param>
public sealed record GetCommitFileRequest(string CommitId, string Path);

/// <summary>
/// Reads a file before and after a commit, to review what the commit changed in it. Only files the
/// commit changed can be read, so paths can't point elsewhere. Request files are also parsed on both
/// sides, so the UI can compare them field by field.
/// </summary>
public sealed class GetCommitFile(IGitService git, IRequestFileFormat requestFormat, CurrentWorkspace workspace)
{
    public async Task<Result<GitFileRevisionDto>> ExecuteAsync(
        GetCommitFileRequest request,
        CancellationToken cancellationToken = default)
    {
        var root = workspace.RequireRoot();
        if (root.IsFailure)
        {
            return root.Error!;
        }

        if (!CommitIds.IsValid(request.CommitId))
        {
            return GitErrors.CommitNotFound(request.CommitId);
        }

        var details = await git.GetCommitAsync(root.Value, request.CommitId, cancellationToken);
        if (details.IsFailure)
        {
            return details.Error!;
        }

        var change = details.Value.Files.FirstOrDefault(f => string.Equals(f.Path, request.Path, StringComparison.Ordinal));
        if (change is null)
        {
            return GitErrors.FileNotInCommit(request.CommitId, request.Path);
        }

        var commit = details.Value.Commit;
        string? before = null;
        if (change.Status != GitFileStatus.Added && commit.FirstParentId is { } parent)
        {
            var read = await git.ReadFileAtCommitAsync(root.Value, parent, change.OriginalPath ?? change.Path, cancellationToken);
            if (read.IsFailure)
            {
                return read.Error!;
            }

            before = read.Value;
        }

        string? after = null;
        if (change.Status != GitFileStatus.Deleted)
        {
            var read = await git.ReadFileAtCommitAsync(root.Value, commit.Id, change.Path, cancellationToken);
            if (read.IsFailure)
            {
                return read.Error!;
            }

            after = read.Value;
        }

        var revision = new GitFileRevision(commit.Id, change, before, after);
        var requests = revision.IsBinary
            ? GitRequestSides.NotRequest
            : GitRequestSides.Parse(requestFormat, change.Path, change.OriginalPath, before, after);

        return GitFileRevisionDto.From(revision, requests);
    }
}

/// <summary>
/// Commit ids accepted from the UI: full or abbreviated hexadecimal hashes only, so a value can never
/// be read by git as an option, a ref name or a revision expression.
/// </summary>
internal static class CommitIds
{
    public static bool IsValid(string? commitId) =>
        commitId is { Length: >= 4 and <= 64 } && commitId.All(char.IsAsciiHexDigit);
}
