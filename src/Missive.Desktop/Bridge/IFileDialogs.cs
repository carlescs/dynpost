namespace Missive.Desktop.Bridge;

/// <summary>A file type offered in a file dialog, e.g. <c>("JSON", ["*.json"])</c>.</summary>
internal sealed record FileFilter(string Name, IReadOnlyList<string> Patterns);

/// <summary>The native dialogs that let the user choose folders and files. Each returns null when cancelled.</summary>
internal interface IFileDialogs
{
    Task<string?> PickFolderAsync(string title);

    Task<string?> PickFileAsync(string title, IReadOnlyList<FileFilter> filters);

    Task<string?> PickSaveFileAsync(string title, IReadOnlyList<FileFilter> filters);
}
