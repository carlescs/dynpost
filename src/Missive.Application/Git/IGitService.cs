using Missive.Application.Common;
using Missive.Domain.Git;

namespace Missive.Application.Git;

/// <summary>
/// Git operations on the repository that contains a workspace folder. Implementations use the
/// user's own git configuration and credentials, never force-push or rewrite history, and never
/// resolve conflicts. Paths are relative to the workspace folder and use <c>/</c> separators.
/// Files under the workspace's <c>.missive/</c> folder are never reported or committed.
/// </summary>
public interface IGitService
{
    /// <summary>Reads the branch, upstream tracking and changed files of the workspace.</summary>
    Task<Result<GitStatus>> GetStatusAsync(string workspacePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages and commits changes inside the workspace: the given <paramref name="paths"/>, or every
    /// change when <paramref name="paths"/> is <c>null</c>. Returns the new commit id.
    /// </summary>
    Task<Result<string>> CommitAsync(
        string workspacePath,
        CommitMessage message,
        IReadOnlyList<string>? paths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls the current branch from its upstream with a merge (never a rebase). Conflicts are left
    /// in the working tree for the user and reported as <see cref="PullOutcome.Conflicted"/>.
    /// </summary>
    Task<Result<PullOutcome>> PullAsync(string workspacePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes the current branch without force. With <paramref name="setUpstream"/>, publishes the
    /// branch to the workspace's remote and makes it the upstream.
    /// </summary>
    Task<Result<Unit>> PushAsync(string workspacePath, bool setUpstream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the base, current-branch and incoming versions of a conflicted file, and its working
    /// tree content.
    /// </summary>
    Task<Result<GitConflict>> GetConflictAsync(string workspacePath, string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves one conflicted file as the user chose: by taking one side's version (or its deletion),
    /// or by keeping the working tree file as edited. The file is then marked resolved (staged).
    /// </summary>
    Task<Result<Unit>> ResolveConflictAsync(
        string workspacePath,
        string path,
        ConflictResolution resolution,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Abandons the merge in progress and restores the branch to how it was before it
    /// (<c>git merge --abort</c>).
    /// </summary>
    Task<Result<Unit>> AbortMergeAsync(string workspacePath, CancellationToken cancellationToken = default);
}
