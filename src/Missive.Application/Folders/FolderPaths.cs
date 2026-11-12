using System.Diagnostics.CodeAnalysis;
using Missive.Application.Common;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;

namespace Missive.Application.Folders;

public sealed record FolderDto(string Path);

internal static class FolderPaths
{
    /// <summary>
    /// Parses the path of an existing folder in <paramref name="area"/> (the requests folder unless
    /// given). <paramref name="allowRoot"/> accepts the area folder itself, which doesn't have to
    /// exist yet.
    /// </summary>
    public static bool TryParseFolder(
        IWorkspaceRepository repository,
        string root,
        string? value,
        bool allowRoot,
        out WorkspacePath path,
        [NotNullWhen(false)] out Error? error,
        WorkspacePath? area = null)
    {
        error = null;
        area ??= WorkspaceLayout.RequestsRoot;
        if (!WorkspacePath.TryParse(value, out path) || (path == area && !allowRoot) || !WorkspaceLayout.IsAreaFolder(area, path))
        {
            error = new Error(ErrorCodes.InvalidPath, $"'{value}' is not a valid folder path.");
            return false;
        }

        if (path != area && !repository.DirectoryExists(root, path))
        {
            error = new Error(ErrorCodes.NotFound, $"The folder '{path}' does not exist.");
            return false;
        }

        return true;
    }

    /// <summary>The area (<c>requests/</c> or <c>plans/</c>) a folder path is in; the requests folder when it is in neither.</summary>
    public static WorkspacePath AreaOf(string? value) =>
        WorkspacePath.TryParse(value, out var path) && WorkspaceLayout.IsAreaFolder(WorkspaceLayout.PlansRoot, path)
            ? WorkspaceLayout.PlansRoot
            : WorkspaceLayout.RequestsRoot;

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

}
