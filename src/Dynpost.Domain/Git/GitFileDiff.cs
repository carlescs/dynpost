namespace Dynpost.Domain.Git;

/// <summary>
/// The two versions of a changed file: as of the last commit and as it is in the working tree.
/// A version is <c>null</c> when the file doesn't exist on that side (added or deleted), or when
/// the file is binary.
/// </summary>
/// <param name="Path">The file's current path, relative to the workspace folder.</param>
/// <param name="Status">How the file changed.</param>
/// <param name="OriginalPath">For a rename, the path the file had in the last commit.</param>
/// <param name="Original">The committed version (of <paramref name="OriginalPath"/> for a rename).</param>
/// <param name="Modified">The working tree version.</param>
/// <param name="IsBinary">Whether either version is binary, so no text is shown.</param>
public sealed record GitFileDiff(
    string Path,
    GitFileStatus Status,
    string? OriginalPath,
    string? Original,
    string? Modified,
    bool IsBinary = false)
{
    /// <summary>A NUL character in the first 8000 characters, as git checks for binary files.</summary>
    public static bool LooksBinary(string text) => text.AsSpan(0, Math.Min(text.Length, 8000)).Contains('\0');
}
