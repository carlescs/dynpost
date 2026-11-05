using Missive.Application.Common;
using Missive.Domain.Git;

namespace Missive.Application.Git;

/// <summary>Error codes and messages for git operations.</summary>
public static class GitErrors
{
    public const string WorkspaceNotFoundCode = "git.workspace_not_found";
    public const string GitNotInstalledCode = "git.not_installed";
    public const string NotARepositoryCode = "git.not_a_repository";
    public const string InvalidCommitMessageCode = "git.invalid_commit_message";
    public const string NothingToCommitCode = "git.nothing_to_commit";
    public const string PathsNotChangedCode = "git.paths_not_changed";
    public const string PartialCommitDuringMergeCode = "git.partial_commit_during_merge";
    public const string UnresolvedConflictsCode = "git.unresolved_conflicts";
    public const string DetachedHeadCode = "git.detached_head";
    public const string NoUpstreamCode = "git.no_upstream";
    public const string NoRemoteCode = "git.no_remote";
    public const string PushRejectedCode = "git.push_rejected";
    public const string CommandFailedCode = "git.command_failed";
    public const string NotMergingCode = "git.not_merging";
    public const string NotConflictedCode = "git.not_conflicted";
    public const string ConflictMarkersRemainCode = "git.conflict_markers_remain";

    public static Error WorkspaceNotFound(string path) =>
        new(WorkspaceNotFoundCode, $"The workspace folder '{path}' does not exist.");

    public static Error GitNotInstalled() =>
        new(GitNotInstalledCode, "Git was not found. Install git and make sure it is on the PATH.");

    public static Error NotARepository(string path) =>
        new(NotARepositoryCode, $"The workspace folder '{path}' is not inside a git repository.");

    public static Error InvalidCommitMessage() =>
        new(InvalidCommitMessageCode, "Enter a commit message.");

    public static Error NothingToCommit() =>
        new(NothingToCommitCode, "There are no changes to commit.");

    public static Error PathsNotChanged(IEnumerable<string> paths) =>
        new(PathsNotChangedCode, $"These files have no changes to commit: {string.Join(", ", paths)}.");

    public static Error PartialCommitDuringMerge() =>
        new(PartialCommitDuringMergeCode, "A merge is in progress; commit all changes to complete it.");

    public static Error UnresolvedConflicts(IEnumerable<GitChange> conflicts) =>
        new(
            UnresolvedConflictsCode,
            "Resolve the merge conflicts and mark them as resolved (git add) first: "
                + string.Join(", ", conflicts.Select(c => c.Path))
                + ".");

    public static Error DetachedHead() =>
        new(DetachedHeadCode, "No branch is checked out (detached HEAD). Check out a branch first.");

    public static Error NoUpstream(string branch) =>
        new(NoUpstreamCode, $"The branch '{branch}' has no upstream branch to pull from. Push it first.");

    public static Error NoRemote() =>
        new(NoRemoteCode, "The repository has no remote to push to, or more than one and none named 'origin'.");

    public static Error PushRejected(string details) =>
        new(PushRejectedCode, WithDetails("The remote rejected the push. Pull the latest changes and try again.", details));

    public static Error NotMerging() =>
        new(NotMergingCode, "No merge is in progress.");

    public static Error NotConflicted(string path) =>
        new(NotConflictedCode, $"'{path}' has no merge conflict to resolve.");

    public static Error ConflictMarkersRemain(string path) =>
        new(
            ConflictMarkersRemainCode,
            $"'{path}' still contains conflict markers (<<<<<<<, =======, >>>>>>>). Edit them out, or keep one side's version.");

    public static Error CommandFailed(string command, string details) =>
        new(CommandFailedCode, WithDetails($"git {command} failed.", details));

    private static string WithDetails(string message, string details) =>
        string.IsNullOrWhiteSpace(details) ? message : $"{message}\n{details.Trim()}";
}
