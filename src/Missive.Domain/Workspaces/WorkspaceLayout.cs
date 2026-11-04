namespace Missive.Domain.Workspaces;

/// <summary>Rules for which files and folders of a workspace hold requests.</summary>
public static class WorkspaceLayout
{
    public const string RequestExtension = ".yaml";
    public const string WorkspaceFile = "missive.yaml";
    public const string FolderDefaultsFile = "_folder.yaml";
    public const string EnvironmentsFolder = "environments";
    public const string LocalFolder = ".missive";

    /// <summary>Whether a file directly inside <paramref name="folder"/> is a request file.</summary>
    public static bool IsRequestFile(WorkspacePath folder, string fileName)
    {
        if (fileName.StartsWith('.') || !HasRequestExtension(fileName))
        {
            return false;
        }

        return !(folder.IsRoot && string.Equals(fileName, WorkspaceFile, StringComparison.OrdinalIgnoreCase))
            && !string.Equals(fileName, FolderDefaultsFile, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a sub-folder of <paramref name="parent"/> is a collection shown in the explorer.</summary>
    public static bool IsCollectionFolder(WorkspacePath parent, string folderName) =>
        !folderName.StartsWith('.')
        && !(parent.IsRoot && string.Equals(folderName, EnvironmentsFolder, StringComparison.OrdinalIgnoreCase));

    public static bool HasRequestExtension(string fileName) =>
        fileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The file name to try for a request named <paramref name="requestName"/>: the slug on the
    /// first attempt, then <c>slug-2</c>, <c>slug-3</c>, … when earlier names are taken.
    /// </summary>
    public static string RequestFileName(string requestName, int attempt = 1)
    {
        var slug = Slug.From(requestName);
        return attempt <= 1 ? slug + RequestExtension : $"{slug}-{attempt}{RequestExtension}";
    }

    /// <summary>The file name without its extension, e.g. <c>get-user</c>.</summary>
    public static string FileStem(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot <= 0 ? fileName : fileName[..dot];
    }
}
