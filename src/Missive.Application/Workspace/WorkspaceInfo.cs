namespace Missive.Application.Workspace;

/// <param name="Path">The workspace folder's absolute path.</param>
/// <param name="Name">The folder's name, for display.</param>
public sealed record WorkspaceInfo(string Path, string Name)
{
    public static WorkspaceInfo FromPath(string path)
    {
        var name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));
        return new WorkspaceInfo(path, string.IsNullOrEmpty(name) ? path : name);
    }
}
