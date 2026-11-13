using Dynypost.Domain.Workspaces;

namespace Dynypost.Application.Sharing;

internal static class LocalOnlyPaths
{
    /// <summary>The workspace path of an environment file, from its id.</summary>
    public static WorkspacePath Environment(string id) =>
        WorkspacePath.Root.Combine(WorkspaceLayout.EnvironmentsFolder).Combine(id);

    /// <summary>The local-only files, or none when they can't be read: that must not keep a list from showing.</summary>
    public static IReadOnlySet<WorkspacePath> ListOrEmpty(ILocalOnlyFiles files, string root)
    {
        var listed = files.List(root);
        return listed.IsSuccess ? listed.Value : new HashSet<WorkspacePath>();
    }
}
