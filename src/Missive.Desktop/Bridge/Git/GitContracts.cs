using Missive.Application.Git;
using Missive.Domain.Git;

namespace Missive.Desktop.Bridge.Git;

// Wire format of the git messages. Keep in sync with Missive.Client/src/app/bridge/messages.ts.

internal sealed record WorkspaceRequest(string WorkspacePath);

internal sealed record GitCommitRequest(string WorkspacePath, string Message, IReadOnlyList<string>? Paths = null);

internal sealed record GitChangeDto(string Path, GitFileStatus Status, string? OriginalPath);

internal sealed record GitStatusDto(
    string? Branch,
    string? Upstream,
    int Ahead,
    int Behind,
    bool Merging,
    IReadOnlyList<GitChangeDto> Changes)
{
    public static GitStatusDto From(GitStatus status) =>
        new(
            status.Branch,
            status.Upstream,
            status.Ahead,
            status.Behind,
            status.IsMerging,
            status.Changes.Select(c => new GitChangeDto(c.Path, c.Status, c.OriginalPath)).ToList());
}

internal sealed record GitCommitResponse(string CommitId, GitStatusDto Status);

internal sealed record GitPullResponse(PullOutcome Outcome, GitStatusDto Status);
