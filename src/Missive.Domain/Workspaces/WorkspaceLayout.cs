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

    /// <summary>Files Missive may delete along with a folder: its own files and OS clutter.</summary>
    private static readonly HashSet<string> DisposableFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        FolderDefaultsFile,
        ".gitkeep",
        ".DS_Store",
        "Thumbs.db",
        "desktop.ini",
    };

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private const int MaxNameLength = 255;

    /// <summary>
    /// Checks a new collection folder name for <paramref name="parent"/>. Names must work on
    /// Windows, macOS and Linux so the workspace can be shared through git. Returns <c>null</c>
    /// when the name is valid, otherwise why it is not.
    /// </summary>
    public static string? ValidateFolderName(WorkspacePath parent, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "The folder needs a name.";
        }

        if (name != name.Trim())
        {
            return "Folder names cannot start or end with spaces.";
        }

        if (name.Length > MaxNameLength)
        {
            return $"Folder names can have at most {MaxNameLength} characters.";
        }

        if (name.IndexOfAny(['/', '\\', '<', '>', ':', '"', '|', '?', '*']) >= 0 || name.Any(char.IsControl))
        {
            return "Folder names cannot contain / \\ < > : \" | ? * or control characters.";
        }

        if (name.StartsWith('.'))
        {
            return "Folder names cannot start with a dot; such folders are hidden.";
        }

        if (name.EndsWith('.'))
        {
            return "Folder names cannot end with a dot.";
        }

        var stem = name.Split('.')[0];
        if (ReservedDeviceNames.Contains(stem))
        {
            return $"'{name}' is a reserved name on Windows.";
        }

        if (!IsCollectionFolder(parent, name))
        {
            return $"'{name}' is reserved for workspace settings.";
        }

        return null;
    }

    /// <summary>
    /// Whether a file can be deleted together with the collection folder holding it: request files
    /// and Missive's own or OS-generated files. Anything else belongs to the user and blocks the
    /// deletion.
    /// </summary>
    public static bool IsDisposableFile(string fileName) =>
        DisposableFiles.Contains(fileName) || (!fileName.StartsWith('.') && HasRequestExtension(fileName));
}
