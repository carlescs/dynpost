namespace Dynpost.Domain.Git;

/// <summary>A changed file, with its path relative to the workspace folder using <c>/</c> separators.</summary>
/// <param name="Path">The file's current path.</param>
/// <param name="Status">How the file changed.</param>
/// <param name="OriginalPath">For a rename inside the workspace, the path the file was renamed from.</param>
public sealed record GitChange(string Path, GitFileStatus Status, string? OriginalPath = null);
