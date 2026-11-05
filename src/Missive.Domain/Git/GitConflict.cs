namespace Missive.Domain.Git;

/// <summary>The versions of a conflicted file. A version is <c>null</c> when that side deleted the file.</summary>
/// <param name="Path">Path relative to the workspace folder.</param>
/// <param name="Base">The common ancestor's version, or <c>null</c> when both sides added the file.</param>
/// <param name="Ours">The current branch's version.</param>
/// <param name="Theirs">The incoming version.</param>
/// <param name="Working">The working tree file, usually with conflict markers; <c>null</c> when it was deleted.</param>
public sealed record GitConflict(string Path, string? Base, string? Ours, string? Theirs, string? Working)
{
    /// <summary>Whether the working tree file still has conflict markers git wrote into it.</summary>
    public bool HasConflictMarkers => Working is not null && ConflictMarkers.Contains(Working);
}
