namespace Dynypost.Domain.Http;

/// <summary>A file path of a request body resolved on this computer, or why it can't be used.</summary>
/// <param name="FullPath">The absolute path; <c>null</c> when <see cref="Error"/> is set.</param>
public sealed record ResolvedFilePath(string? FullPath, string? Error);

/// <summary>
/// Rules for the files request bodies send (binary bodies and multipart file fields): paths are
/// relative to the workspace and must stay inside it, or absolute.
/// </summary>
public static class RequestFilePaths
{
    /// <summary>Resolves <paramref name="path"/> against the workspace root.</summary>
    public static ResolvedFilePath Resolve(string path, string? workspaceRoot)
    {
        try
        {
            if (Path.IsPathRooted(path))
            {
                return new ResolvedFilePath(Path.GetFullPath(path), null);
            }

            if (string.IsNullOrWhiteSpace(workspaceRoot))
            {
                return new ResolvedFilePath(null, $"The file '{path}' is relative to the workspace, but no workspace is open.");
            }

            var root = Path.GetFullPath(workspaceRoot);
            var fullPath = Path.GetFullPath(Path.Combine(root, path));
            return IsOutside(Path.GetRelativePath(root, fullPath))
                ? new ResolvedFilePath(null, $"The file path '{path}' must stay inside the workspace, or be an absolute path.")
                : new ResolvedFilePath(fullPath, null);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return new ResolvedFilePath(null, $"'{path}' is not a valid file path.");
        }
    }

    /// <summary>
    /// The path to store for a file the user picked: relative to the workspace (with <c>/</c>
    /// separators) when it is inside it, otherwise absolute.
    /// </summary>
    public static (string Path, bool IsAbsolute) ForRequest(string pickedPath, string? workspaceRoot)
    {
        var absolutePath = Path.GetFullPath(pickedPath);
        if (workspaceRoot is null)
        {
            return (absolutePath, true);
        }

        var relativePath = Path.GetRelativePath(Path.GetFullPath(workspaceRoot), absolutePath);
        return IsOutside(relativePath)
            ? (absolutePath, true)
            : (relativePath.Replace('\\', '/'), false);
    }

    private static bool IsOutside(string relativePath) =>
        Path.IsPathRooted(relativePath)
        || relativePath == ".."
        || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
}
