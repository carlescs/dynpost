using Missive.Domain.Requests;

namespace Missive.Application.Workspaces;

internal static class WorkspaceTreeBuilder
{
    public static WorkspaceDto Build(string root, IReadOnlyList<WorkspaceEntry> entries) =>
        new(WorkspaceName(root), root, entries.Select(ToNode).ToList());

    private static WorkspaceNodeDto ToNode(WorkspaceEntry entry) => entry switch
    {
        FolderEntry folder => new WorkspaceNodeDto(
            folder.Path.Value,
            folder.Path.FileName,
            "folder",
            Children: folder.Children.Select(ToNode).ToList()),
        RequestFileEntry { Request.IsSuccess: true } file => new WorkspaceNodeDto(
            file.Path.Value,
            file.Request.Value.Name,
            "request",
            Method: file.Request.Value.Method.ToName()),
        RequestFileEntry file => new WorkspaceNodeDto(
            file.Path.Value,
            file.Path.FileName,
            "request",
            Error: file.Request.Error!.Message),
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
