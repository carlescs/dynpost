using System.Diagnostics.CodeAnalysis;
using Missive.Application.Common;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;

namespace Missive.Application.Folders;

public sealed record FolderDto(string Path);

internal static class FolderPaths
{
    /// <summary>Parses the path of an existing collection folder.</summary>
    public static bool TryParseFolder(
        IWorkspaceRepository repository,
        string root,
        string? value,
        bool allowRoot,
        out WorkspacePath path,
        [NotNullWhen(false)] out Error? error)
    {
        error = null;
        if (!WorkspacePath.TryParse(value, out path) || (path.IsRoot && !allowRoot) || !IsCollectionPath(path))
        {
            error = new Error(ErrorCodes.InvalidPath, $"'{value}' is not a valid folder path.");
            return false;
        }

        if (!path.IsRoot && !repository.DirectoryExists(root, path))
        {
            error = new Error(ErrorCodes.NotFound, $"The folder '{path}' does not exist.");
            return false;
        }

        return true;
    }

    /// <summary>The path of a new folder named <paramref name="name"/> inside <paramref name="parent"/>.</summary>
    public static Result<WorkspacePath> Child(WorkspacePath parent, string? name)
    {
        var trimmed = name?.Trim();
        var problem = WorkspaceLayout.ValidateFolderName(parent, trimmed);
        return problem is null ? parent.Combine(trimmed!) : new Error(ErrorCodes.InvalidPath, problem);
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> itself or inside it.</summary>
    public static bool IsWithin(WorkspacePath path, WorkspacePath folder) =>
        path == folder || path.Value.StartsWith(folder.Value + "/", StringComparison.Ordinal);

    /// <summary>Whether every segment of the path is a folder the explorer shows.</summary>
    private static bool IsCollectionPath(WorkspacePath path)
    {
        var parent = WorkspacePath.Root;
        foreach (var segment in path.Segments)
        {
            if (!WorkspaceLayout.IsCollectionFolder(parent, segment))
            {
                return false;
            }

            parent = parent.Combine(segment);
        }

        return true;
    }
}
