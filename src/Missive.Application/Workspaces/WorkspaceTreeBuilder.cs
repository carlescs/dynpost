using Missive.Domain.Requests;
using Missive.Domain.Workspaces;

namespace Missive.Application.Workspaces;

internal static class WorkspaceTreeBuilder
{
    public static WorkspaceDto Build(string root, IReadOnlyList<WorkspaceEntry> entries, IReadOnlySet<WorkspacePath> localOnly) =>
        new(WorkspaceName(root), root, entries.Select(entry => ToNode(entry, localOnly)).ToList());

    private static WorkspaceNodeDto ToNode(WorkspaceEntry entry, IReadOnlySet<WorkspacePath> localOnly) => entry switch
    {
        FolderEntry folder => new WorkspaceNodeDto(
            folder.Path.Value,
            folder.Path.FileName,
            "folder",
            Children: folder.Children.Select(child => ToNode(child, localOnly)).ToList()),
        RequestFileEntry { Request.IsSuccess: true } file => new WorkspaceNodeDto(
            file.Path.Value,
            file.Request.Value.Name,
            "request",
            Method: file.Request.Value.Method.ToName(),
            LocalOnly: localOnly.Contains(file.Path)),
        RequestFileEntry file => new WorkspaceNodeDto(
            file.Path.Value,
            file.Path.FileName,
            "request",
            Error: file.Request.Error!.Message,
            LocalOnly: localOnly.Contains(file.Path)),
        _ => throw new ArgumentOutOfRangeException(nameof(entry)),
    };

    internal static string WorkspaceName(string root)
    {
        var trimmed = root.TrimEnd('/', '\\');
        var index = trimmed.LastIndexOfAny(['/', '\\']);
        var name = index < 0 ? trimmed : trimmed[(index + 1)..];
        return name.Length == 0 ? root : name;
    }
}
