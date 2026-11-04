namespace Missive.Desktop.Bridge;

/// <summary>Asks the user to choose a folder. Returns null when the dialog is cancelled.</summary>
internal interface IFolderPicker
{
    Task<string?> PickFolderAsync(string title);
}
