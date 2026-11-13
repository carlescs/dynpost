namespace Dynpost.Domain.Git;

/// <summary>How the user chose to resolve one conflicted file. Dynpost never picks one itself.</summary>
public enum ConflictResolution
{
    /// <summary>Keep the version from the current branch.</summary>
    Ours,

    /// <summary>Keep the incoming version (the branch being merged in).</summary>
    Theirs,

    /// <summary>Keep the file as the user edited it in the working tree.</summary>
    Edited,
}
