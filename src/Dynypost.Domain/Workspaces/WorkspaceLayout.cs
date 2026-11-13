using System.Text;

namespace Dynypost.Domain.Workspaces;

/// <summary>
/// Rules for which files and folders of a workspace hold requests and run plans. Requests and
/// their collection folders live in <c>requests/</c>, run plans in <c>plans/</c> and environments
/// in <c>environments/</c>; every other file belongs to the user.
/// </summary>
public static class WorkspaceLayout
{
    public const string RequestExtension = ".yaml";
    public const string WorkspaceFile = "dynypost.yaml";
    public const string FolderDefaultsFile = "_folder.yaml";
    public const string EnvironmentsFolder = "environments";
    public const string LocalFolder = ".dynypost";

    /// <summary><see cref="WorkspaceFile"/> under the app's former name, Missive, renamed when a workspace is opened.</summary>
    public const string LegacyWorkspaceFile = "missive.yaml";

    /// <summary><see cref="LocalFolder"/> under the app's former name, Missive, renamed when a workspace is opened.</summary>
    public const string LegacyLocalFolder = ".missive";

    public const string RequestsFolder = "requests";
    public const string PlansFolder = "plans";

    /// <summary>The folder holding every request and collection folder: <c>requests/</c>.</summary>
    public static WorkspacePath RequestsRoot { get; } = WorkspacePath.Root.Combine(RequestsFolder);

    /// <summary>The folder holding every run plan and the folders organizing them: <c>plans/</c>.</summary>
    public static WorkspacePath PlansRoot { get; } = WorkspacePath.Root.Combine(PlansFolder);

    /// <summary>The current layout of a workspace, recorded as <c>layout</c> in <see cref="WorkspaceFile"/>.</summary>
    public const int CurrentLayoutVersion = 2;

    /// <summary>Whether a file directly inside <paramref name="folder"/> is a request file.</summary>
    public static bool IsRequestFile(WorkspacePath folder, string fileName)
    {
        if (fileName.StartsWith('.') || !HasRequestExtension(fileName) || IsRunPlanFileName(fileName))
        {
            return false;
        }

        return !(folder.IsRoot && string.Equals(fileName, WorkspaceFile, StringComparison.OrdinalIgnoreCase))
            && !string.Equals(fileName, FolderDefaultsFile, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether <paramref name="path"/> is a request file shown in the explorer: a request file
    /// inside <c>requests/</c>, not in a hidden folder.
    /// </summary>
    public static bool IsRequestPath(WorkspacePath path) =>
        !path.IsRoot
        && IsRequestFile(path.Parent, path.FileName)
        && IsAreaFolder(RequestsRoot, path.Parent);

    /// <summary>Whether <paramref name="path"/> is a run plan file shown in the Load runs view: a <c>*.run.yaml</c> inside <c>plans/</c>.</summary>
    public static bool IsRunPlanPath(WorkspacePath path) =>
        !path.IsRoot
        && IsRunPlanFileName(path.FileName)
        && IsAreaFolder(PlansRoot, path.Parent);

    /// <summary>
    /// Whether <paramref name="folder"/> is <paramref name="area"/> (<see cref="RequestsRoot"/> or
    /// <see cref="PlansRoot"/>) or a folder inside it that isn't hidden.
    /// </summary>
    public static bool IsAreaFolder(WorkspacePath area, WorkspacePath folder)
    {
        if (folder == area)
        {
            return true;
        }

        if (!folder.Value.StartsWith(area.Value + "/", StringComparison.Ordinal))
        {
            return false;
        }

        var parent = area;
        foreach (var segment in folder.Segments.Skip(area.Segments.Count))
        {
            if (!IsCollectionFolder(parent, segment))
            {
                return false;
            }

            parent = parent.Combine(segment);
        }

        return true;
    }

    /// <summary>Whether a sub-folder of <paramref name="parent"/> is a folder shown in the explorer or the Load runs view.</summary>
    public static bool IsCollectionFolder(WorkspacePath parent, string folderName) => !folderName.StartsWith('.');

    /// <summary>Whether a file name is a load run plan (<c>*.run.yaml</c>), which isn't a request.</summary>
    public static bool IsRunPlanFileName(string fileName) =>
        !fileName.StartsWith('.')
        && (fileName.EndsWith(".run.yaml", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".run.yml", StringComparison.OrdinalIgnoreCase));

    /// <summary>The run plan extension of <paramref name="fileName"/> (<c>.run.yaml</c> or <c>.run.yml</c>), or <c>null</c>.</summary>
    public static string? RunPlanExtension(string fileName)
    {
        if (!IsRunPlanFileName(fileName))
        {
            return null;
        }

        return fileName[(fileName.Length - (fileName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ? 8 : 9))..];
    }

    /// <summary>The file name of a run plan without its run plan extension, e.g. <c>checkout-smoke</c>.</summary>
    public static string RunPlanStem(string fileName)
    {
        var extension = RunPlanExtension(fileName);
        return extension is null ? FileStem(fileName) : fileName[..^extension.Length];
    }

    /// <summary>
    /// The file name to try for a run plan named <paramref name="planName"/>, like
    /// <see cref="RequestFileName"/>: <c>checkout-smoke.run.yaml</c>, then <c>checkout-smoke-2.run.yaml</c>, …
    /// </summary>
    public static string RunPlanFileName(string planName, int attempt = 1, string extension = RunPlanExtensionDefault)
    {
        var slug = Slug.From(planName, "plan");
        return attempt <= 1 ? slug + extension : $"{slug}-{attempt}{extension}";
    }

    public const string RunPlanExtensionDefault = ".run.yaml";

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

    /// <summary>Whether a file in the <c>environments</c> folder is an environment file.</summary>
    public static bool IsEnvironmentFile(string fileName) =>
        !fileName.StartsWith('.') && HasRequestExtension(fileName) && !fileName.Contains('/') && !fileName.Contains('\\');

    /// <summary>
    /// The file name to try for an environment named <paramref name="environmentName"/>, like
    /// <see cref="RequestFileName"/>: <c>staging.yaml</c>, then <c>staging-2.yaml</c>, …
    /// </summary>
    public static string EnvironmentFileName(string environmentName, int attempt = 1)
    {
        var slug = Slug.From(environmentName, "environment");
        return attempt <= 1 ? slug + RequestExtension : $"{slug}-{attempt}{RequestExtension}";
    }

    /// <summary>The file name without its extension, e.g. <c>get-user</c>.</summary>
    public static string FileStem(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot <= 0 ? fileName : fileName[..dot];
    }

    /// <summary>Files Dynypost may delete along with a folder: its own files and OS clutter.</summary>
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

        return null;
    }

    /// <summary>
    /// Turns any name (such as a folder imported from another app) into a folder name that passes
    /// <see cref="ValidateFolderName"/> for a sub-folder: characters that aren't allowed become
    /// <c>-</c>, leading and trailing dots and spaces are removed, Windows device names get a
    /// <c>_</c> suffix and long names are shortened. Returns <paramref name="fallback"/> when
    /// nothing usable is left.
    /// </summary>
    public static string ToFolderName(string? name, string fallback = "Folder")
    {
        var builder = new StringBuilder();
        foreach (var c in name ?? string.Empty)
        {
            if (char.IsControl(c))
            {
                builder.Append(' ');
            }
            else
            {
                builder.Append("/\\<>:\"|?*".Contains(c) ? '-' : c);
            }
        }

        var cleaned = builder.ToString().Trim().Trim('.').Trim();
        if (cleaned.Length > SuggestedNameLength)
        {
            cleaned = cleaned[..SuggestedNameLength].TrimEnd().TrimEnd('.').TrimEnd();
        }

        if (cleaned.Length == 0)
        {
            return fallback;
        }

        return ReservedDeviceNames.Contains(cleaned.Split('.')[0]) ? cleaned + "_" : cleaned;
    }

    /// <summary>The longest folder name <see cref="ToFolderName"/> makes, to keep paths short.</summary>
    private const int SuggestedNameLength = 100;

    /// <summary>
    /// Whether a file can be deleted together with the collection folder holding it: request files
    /// and Dynypost's own or OS-generated files. Anything else belongs to the user and blocks the
    /// deletion.
    /// </summary>
    /// <summary>
    /// Whether a file is OS clutter or a <c>.gitkeep</c>: files a folder can hold and still count
    /// as empty.
    /// </summary>
    public static bool IsClutterFile(string fileName) =>
        DisposableFiles.Contains(fileName) && !string.Equals(fileName, FolderDefaultsFile, StringComparison.OrdinalIgnoreCase);

    public static bool IsDisposableFile(string fileName) =>
        DisposableFiles.Contains(fileName) || (!fileName.StartsWith('.') && HasRequestExtension(fileName));
}
