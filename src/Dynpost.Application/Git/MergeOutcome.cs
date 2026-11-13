namespace Dynpost.Application.Git;

public enum MergeOutcome
{
    /// <summary>The current branch already contained every commit of the merged branch.</summary>
    UpToDate,

    /// <summary>The branch's commits were merged (or fast-forwarded) into the current branch.</summary>
    Merged,

    /// <summary>The merge stopped on conflicts, which are left for the user to resolve.</summary>
    Conflicted,
}

/// <param name="Outcome">Whether anything was merged, or the merge stopped on conflicts.</param>
/// <param name="ConflictsOutsideWorkspace">
/// Conflicted files outside the workspace folder (when it is a subfolder of the repository), relative
/// to the repository root. They are not in the workspace's status, so Dynpost can't resolve them;
/// the user resolves them with other git tools or aborts the merge.
/// </param>
public sealed record BranchMergeResult(MergeOutcome Outcome, IReadOnlyList<string> ConflictsOutsideWorkspace)
{
    public static BranchMergeResult Of(MergeOutcome outcome) => new(outcome, []);
}
