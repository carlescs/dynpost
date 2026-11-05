using Missive.Domain.Workspaces;
using static Missive.Infrastructure.Workspaces.WorkspaceFiles;

namespace Missive.Infrastructure.Environments;

/// <summary>
/// The workspace's <c>.missive</c> folder, for files that stay on this computer (secrets, UI
/// state). It must never be committed, so writing to it makes sure the workspace's
/// <c>.gitignore</c> excludes it.
/// </summary>
internal static class LocalWorkspaceFolder
{
    private const string GitIgnoreFile = ".gitignore";
    private const string GitIgnoreEntry = WorkspaceLayout.LocalFolder + "/";

    public static string FilePath(string root, string fileName) =>
        FullPath(root, WorkspacePath.Root.Combine(WorkspaceLayout.LocalFolder).Combine(fileName));

    /// <summary>Writes a file in <c>.missive</c> unless it already has this content.</summary>
    public static async Task WriteAsync(string root, string fileName, string content, CancellationToken cancellationToken)
    {
        await EnsureGitIgnoredAsync(root, cancellationToken);
        await WriteIfChangedAsync(FilePath(root, fileName), content, cancellationToken);
    }

    /// <summary>Deletes a file in <c>.missive</c>, if it exists.</summary>
    public static void Delete(string root, string fileName)
    {
        var path = FilePath(root, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Adds <c>.missive/</c> to the <c>.gitignore</c> in the workspace folder (creating it if
    /// needed) unless an entry for it is already there. Other lines are left as they are.
    /// </summary>
    public static async Task EnsureGitIgnoredAsync(string root, CancellationToken cancellationToken)
    {
        var path = FullPath(root, WorkspacePath.Root.Combine(GitIgnoreFile));
        var existing = await ReadIfExistsAsync(path, cancellationToken);
        if (existing is null)
        {
            await File.WriteAllTextAsync(path, GitIgnoreEntry + "\n", Utf8NoBom, cancellationToken);
            return;
        }

        if (existing.Split('\n').Any(line => IsLocalFolderEntry(line.Trim())))
        {
            return;
        }

        var newline = existing.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var separator = existing.Length == 0 || existing.EndsWith('\n') ? string.Empty : newline;
        await File.AppendAllTextAsync(path, separator + GitIgnoreEntry + newline, Utf8NoBom, cancellationToken);
    }

    private static bool IsLocalFolderEntry(string line) =>
        line.TrimStart('/').TrimEnd('/') == WorkspaceLayout.LocalFolder;
}
