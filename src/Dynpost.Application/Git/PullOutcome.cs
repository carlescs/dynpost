namespace Dynpost.Application.Git;

public enum PullOutcome
{
    /// <summary>The branch already contained everything on its upstream.</summary>
    UpToDate,

    /// <summary>Upstream commits were merged or fast-forwarded into the branch.</summary>
    Updated,

    /// <summary>The merge stopped on conflicts, which are left for the user to resolve.</summary>
    Conflicted,
}
