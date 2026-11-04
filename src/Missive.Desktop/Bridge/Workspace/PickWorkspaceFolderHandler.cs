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
internal sealed record PickFolderResponse(string? Path);

/// <summary>
/// Lets the user choose a workspace folder. Only shows the dialog; the UI then opens the folder
/// with <see cref="BridgeMessageTypes.WorkspaceOpen"/>.
/// </summary>
internal sealed class PickWorkspaceFolderHandler(IFolderPicker picker) : BridgeHandler<PickFolderRequest, PickFolderResponse>
{
    public override string Type => BridgeMessageTypes.WorkspacePickFolder;

    protected override async Task<Result<PickFolderResponse>> HandleAsync(
        PickFolderRequest request,
        CancellationToken cancellationToken)
    {
        var path = await picker.PickFolderAsync("Open workspace folder", cancellationToken);
        return new PickFolderResponse(string.IsNullOrEmpty(path) ? null : path);
    }
}
