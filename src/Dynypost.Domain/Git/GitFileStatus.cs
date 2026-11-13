namespace Dynypost.Domain.Git;

/// <summary>How a file in the workspace differs from the last commit.</summary>
public enum GitFileStatus
{
    Modified,
    Added,
    Deleted,
    Renamed,
    Untracked,
    Conflicted,
}
