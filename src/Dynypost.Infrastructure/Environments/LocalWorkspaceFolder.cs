using Dynypost.Domain.Workspaces;
using Dynypost.Infrastructure.Git;
using static Dynypost.Infrastructure.Workspaces.WorkspaceFiles;

namespace Dynypost.Infrastructure.Environments;

/// <summary>
/// The workspace's <c>.dynypost</c> folder, for files that stay on this computer (secrets, UI
/// state). It must never be committed, so writing to it makes sure the workspace's
/// <c>.gitignore</c> excludes it.
/// </summary>
internal static class LocalWorkspaceFolder
{
    public static string FilePath(string root, string fileName) =>
        FullPath(root, WorkspacePath.Root.Combine(WorkspaceLayout.LocalFolder).Combine(fileName));

    /// <summary>Writes a file in <c>.dynypost</c> unless it already has this content.</summary>
    public static async Task WriteAsync(string root, string fileName, string content, CancellationToken cancellationToken)
    {
        await GitIgnoreFile.EnsureIgnoredAsync(root, cancellationToken);
        await WriteIfChangedAsync(FilePath(root, fileName), content, cancellationToken);
    }

    /// <summary>Deletes a file in <c>.dynypost</c>, if it exists.</summary>
    public static void Delete(string root, string fileName)
    {
        var path = FilePath(root, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
