namespace Missive.Application.Git;

public enum MergeOutcome
{
    /// <summary>The current branch already contained every commit of the merged branch.</summary>
    UpToDate,

    /// <summary>The branch's commits were merged (or fast-forwarded) into the current branch.</summary>
    Merged,

    /// <summary>The merge stopped on conflicts, which are left for the user to resolve.</summary>
    Conflicted,
}
