using Missive.Application.Common;

namespace Missive.Desktop.Bridge.Workspace;

/// <summary>Shows a native folder picker.</summary>
internal interface IFolderPicker
{
    /// <summary>Returns the chosen folder, or <c>null</c> when the user cancels.</summary>
    Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken);
}

internal sealed record PickFolderRequest
{
}

/// <param name="Path">The chosen folder, or <c>null</c> when the user cancelled.</param>
/// <param name="Name">The folder's name, for display.</param>
internal sealed record PickFolderResponse(string? Path, string? Name);

/// <summary>Lets the user choose the workspace folder.</summary>
internal sealed class PickWorkspaceFolderHandler(IFolderPicker picker) : BridgeHandler<PickFolderRequest, PickFolderResponse>
{
    public override string Type => BridgeMessageTypes.WorkspacePickFolder;

    protected override async Task<Result<PickFolderResponse>> HandleAsync(
        PickFolderRequest request,
        CancellationToken cancellationToken)
    {
        var path = await picker.PickFolderAsync("Open workspace folder", cancellationToken);
        if (string.IsNullOrEmpty(path))
        {
            return new PickFolderResponse(null, null);
        }

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return new PickFolderResponse(path, string.IsNullOrEmpty(name) ? path : name);
    }
}
