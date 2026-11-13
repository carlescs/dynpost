using System.Text;
using Dynypost.Domain.Workspaces;

namespace Dynypost.Infrastructure.Git;

/// <summary>Keeps the workspace's local-only <c>.dynypost/</c> folder in its <c>.gitignore</c>.</summary>
internal static class GitIgnoreFile
{
    public const string FileName = ".gitignore";
    public const string LocalFolderEntry = ".dynypost/";

    private static readonly string[] LocalFolderPatterns = [".dynypost", ".dynypost/", "/.dynypost", "/.dynypost/"];

    private static readonly string[] LegacyFolderPatterns = [".missive", ".missive/", "/.missive", "/.missive/"];

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

    /// <summary>
    /// Points the workspace's <c>.gitignore</c> lines that ignore the local folder of the app's
    /// former name (<c>.missive/</c>) at the current one. Other lines and the file's line endings
    /// are kept; a missing file is left missing.
    /// </summary>
    public static async Task RenameLegacyFolderAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var path = Path.Combine(workspacePath, FileName);
        if (!File.Exists(path))
        {
            return;
        }

        var content = await File.ReadAllTextAsync(path, cancellationToken);
        var updated = RenameLegacyFolder(content);
        if (updated != content)
        {
            await File.WriteAllTextAsync(path, updated, Utf8NoBom, cancellationToken);
        }
    }

    /// <summary>The <c>.gitignore</c> content with each line ignoring <c>.missive</c> made to ignore <see cref="LocalFolderEntry"/>'s folder instead.</summary>
    public static string RenameLegacyFolder(string content)
    {
        var lines = content.Split('\n');
        var changed = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (LegacyFolderPatterns.Contains(line.Trim(), StringComparer.Ordinal))
            {
                var ending = lines[i][line.Length..];
                lines[i] = line.Replace(WorkspaceLayout.LegacyLocalFolder, WorkspaceLayout.LocalFolder, StringComparison.Ordinal) + ending;
                changed = true;
            }
        }

        return changed ? string.Join('\n', lines) : content;
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

        // Match the file's line endings, so the change is one added line.
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var separator = content.Length == 0 || content.EndsWith('\n') ? string.Empty : newline;
        return content + separator + LocalFolderEntry + newline;
    }
}
