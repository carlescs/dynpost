using System.Text;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Infrastructure.Workspaces;

/// <summary>Helpers shared by the stores that read and write files inside a workspace folder.</summary>
internal static class WorkspaceFiles
{
    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Resolves a workspace path to a full path, guarding against escaping the root.</summary>
    public static string FullPath(string root, WorkspacePath path)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine([fullRoot, .. path.Segments]));
        var rootWithSeparator = Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;

        if (fullPath != fullRoot && !fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException($"'{path}' is outside the workspace.");
        }

        return fullPath;
    }

    /// <summary>Reads a text file, or returns <c>null</c> when it doesn't exist.</summary>
    public static async Task<string?> ReadIfExistsAsync(string fullPath, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(fullPath, Utf8NoBom, cancellationToken);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes a text file atomically (a temporary file replaces the old one) unless it already has
    /// this content. Returns whether the file was written.
    /// </summary>
    public static async Task<bool> WriteIfChangedAsync(string fullPath, string content, CancellationToken cancellationToken)
    {
        if (await ReadIfExistsAsync(fullPath, cancellationToken) == content)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + ".tmp";
        await File.WriteAllTextAsync(temporary, content, Utf8NoBom, cancellationToken);
        File.Move(temporary, fullPath, overwrite: true);
        return true;
    }
}
