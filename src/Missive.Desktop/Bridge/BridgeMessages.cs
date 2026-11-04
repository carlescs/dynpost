using System.Text.Json;
using Missive.Application.Common;
using Missive.Application.Requests;

namespace Missive.Desktop.Bridge;

/// <summary>
/// The message types the UI can send. Keep in sync with
/// <c>src/Missive.Client/src/app/bridge/messages.ts</c>.
/// </summary>
internal static class BridgeMessageTypes
{
    /// <summary>Shows a folder picker and opens the chosen folder. Payload: none. Result: workspace or null if cancelled.</summary>
    public const string WorkspacePick = "workspace.pick";

    /// <summary>Opens a folder by path. Payload: <see cref="OpenWorkspacePayload"/>. Result: workspace.</summary>
    public const string WorkspaceOpen = "workspace.open";

    /// <summary>Re-reads the open workspace. Payload: none. Result: workspace.</summary>
    public const string WorkspaceTree = "workspace.tree";

    /// <summary>Reads a request file. Payload: <see cref="LoadRequestPayload"/>. Result: request.</summary>
    public const string RequestLoad = "request.load";

    /// <summary>Saves a new request in a folder. Payload: <see cref="CreateRequestPayload"/>. Result: saved request.</summary>
    public const string RequestCreate = "request.create";

    /// <summary>Saves an existing request file. Payload: <see cref="SaveRequestPayload"/>. Result: saved request.</summary>
    public const string RequestSave = "request.save";

    /// <summary>Creates a folder. Payload: <see cref="CreateFolderPayload"/>. Result: folder.</summary>
    public const string FolderCreate = "folder.create";

    /// <summary>Renames a folder. Payload: <see cref="RenameFolderPayload"/>. Result: folder (new path).</summary>
    public const string FolderRename = "folder.rename";

    /// <summary>Deletes a folder with its requests. Payload: <see cref="DeleteFolderPayload"/>. Result: none.</summary>
    public const string FolderDelete = "folder.delete";
}

/// <summary>A message from the UI. <see cref="Id"/> correlates the response.</summary>
internal sealed record BridgeRequest(string Id, string Type, JsonElement? Payload);

/// <summary>The answer to a <see cref="BridgeRequest"/>: either <see cref="Payload"/> or <see cref="Error"/>.</summary>
internal sealed record BridgeResponse(string Id, bool Ok, object? Payload = null, Error? Error = null);

internal sealed record OpenWorkspacePayload(string Path);

internal sealed record LoadRequestPayload(string Path);

internal sealed record CreateRequestPayload(string Folder, RequestDto Request);

internal sealed record SaveRequestPayload(string Path, RequestDto Request);

/// <param name="Parent">The containing folder; <c>""</c> is the workspace root.</param>
internal sealed record CreateFolderPayload(string Parent, string Name);

internal sealed record RenameFolderPayload(string Path, string Name);

internal sealed record DeleteFolderPayload(string Path);

internal static class BridgeErrorCodes
{
    public const string UnknownType = "unknown_message";
    public const string BadPayload = "bad_payload";
    public const string Internal = "internal_error";
}
