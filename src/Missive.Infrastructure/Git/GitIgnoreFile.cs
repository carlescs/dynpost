using System.Text;

namespace Missive.Infrastructure.Git;

/// <summary>Keeps the workspace's local-only <c>.missive/</c> folder in its <c>.gitignore</c>.</summary>
internal static class GitIgnoreFile
{
    public const string FileName = ".gitignore";
    public const string LocalFolderEntry = ".missive/";

    private static readonly string[] LocalFolderPatterns = [".missive", ".missive/", "/.missive", "/.missive/"];

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Adds <see cref="LocalFolderEntry"/> to the workspace's <c>.gitignore</c>, creating it if needed.
    /// The file is left untouched when it already ignores the folder.
    /// </summary>
    public static async Task EnsureIgnoredAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var path = Path.Combine(workspacePath, FileName);
        var content = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : string.Empty;

        var updated = AddLocalFolder(content);
        if (updated != content)
        {
            await File.WriteAllTextAsync(path, updated, Utf8NoBom, cancellationToken);
        }
    }

    /// <summary>The <c>.gitignore</c> content with <see cref="LocalFolderEntry"/> appended if no line ignores it yet.</summary>
    public static string AddLocalFolder(string content)
    {
        var ignored = content
            .Split('\n')
            .Select(line => line.Trim())
            .Any(line => LocalFolderPatterns.Contains(line, StringComparer.Ordinal));
        if (ignored)
        {
            return content;
        }

        var separator = content.Length == 0 || content.EndsWith('\n') ? string.Empty : "\n";
        return content + separator + LocalFolderEntry + "\n";
    }
}
