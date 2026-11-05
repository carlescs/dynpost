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

    /// <summary>Reopens the workspace from the previous session. Payload: none. Result: workspace, or null if there is none.</summary>
    public const string WorkspaceOpenLast = "workspace.openLast";

    /// <summary>Re-reads the open workspace. Payload: none. Result: workspace.</summary>
    public const string WorkspaceTree = "workspace.tree";

    /// <summary>Reads a request file. Payload: <see cref="LoadRequestPayload"/>. Result: request.</summary>
    public const string RequestLoad = "request.load";

    /// <summary>Saves a new request in a folder. Payload: <see cref="CreateRequestPayload"/>. Result: saved request.</summary>
    public const string RequestCreate = "request.create";

    /// <summary>Saves an existing request file. Payload: <see cref="SaveRequestPayload"/>. Result: saved request.</summary>
    public const string RequestSave = "request.save";

    /// <summary>Moves a request file into another folder. Payload: <see cref="MoveRequestPayload"/>. Result: its new path.</summary>
    public const string RequestMove = "request.move";

    /// <summary>
    /// Sends a request (saved or not) and waits for the response. Payload: <see cref="SendRequestPayload"/>.
    /// Result: response.
    /// </summary>
    public const string RequestSend = "request.send";

    /// <summary>Cancels a request being sent. Payload: <see cref="CancelRequestPayload"/>. Result: whether it was being sent.</summary>
    public const string RequestCancel = "request.cancel";

    /// <summary>Creates a folder. Payload: <see cref="CreateFolderPayload"/>. Result: folder.</summary>
    public const string FolderCreate = "folder.create";

    /// <summary>Renames a folder. Payload: <see cref="RenameFolderPayload"/>. Result: folder (new path).</summary>
    public const string FolderRename = "folder.rename";

    /// <summary>Moves a folder into another folder. Payload: <see cref="MoveFolderPayload"/>. Result: folder (new path).</summary>
    public const string FolderMove = "folder.move";

    /// <summary>Deletes a folder with its requests. Payload: <see cref="DeleteFolderPayload"/>. Result: none.</summary>
    public const string FolderDelete = "folder.delete";

    /// <summary>Reads the open workspace's git status. Payload: none. Result: git status.</summary>
    public const string GitStatus = "git.status";

    /// <summary>Commits changes. Payload: <see cref="GitCommitPayload"/>. Result: commit id and new status.</summary>
    public const string GitCommit = "git.commit";

    /// <summary>Pulls the current branch (merge, never rebase). Payload: none. Result: outcome and new status.</summary>
    public const string GitPull = "git.pull";

    /// <summary>Pushes (or publishes) the current branch, never forcing. Payload: none. Result: new status.</summary>
    public const string GitPush = "git.push";

    /// <summary>Reads both sides of a conflicted file. Payload: <see cref="GitConflictPayload"/>. Result: the versions.</summary>
    public const string GitConflict = "git.conflict";

    /// <summary>Resolves one conflicted file as the user chose. Payload: <see cref="GitResolvePayload"/>. Result: new status.</summary>
    public const string GitResolve = "git.resolve";

    /// <summary>Abandons the merge in progress. Payload: none. Result: new status.</summary>
    public const string GitAbortMerge = "git.abortMerge";

    /// <summary>
    /// Looks for a newer version of the app and downloads it. Payload: none. Result: update status.
    /// </summary>
    public const string UpdateCheck = "update.check";

    /// <summary>Installs the downloaded update and restarts the app. Payload: none. Result: none (the app exits).</summary>
    public const string UpdateRestart = "update.restart";
}

/// <summary>A message from the UI. <see cref="Id"/> correlates the response.</summary>
internal sealed record BridgeRequest(string Id, string Type, JsonElement? Payload);

/// <summary>The answer to a <see cref="BridgeRequest"/>: either <see cref="Payload"/> or <see cref="Error"/>.</summary>
internal sealed record BridgeResponse(string Id, bool Ok, object? Payload = null, Error? Error = null);

internal sealed record OpenWorkspacePayload(string Path);

internal sealed record LoadRequestPayload(string Path);

internal sealed record CreateRequestPayload(string Folder, RequestDto Request);

internal sealed record SaveRequestPayload(string Path, RequestDto Request);

/// <param name="Id">Chosen by the UI; pass it to <see cref="BridgeMessageTypes.RequestCancel"/> to cancel.</param>
internal sealed record SendRequestPayload(string Id, RequestDto Request);

internal sealed record CancelRequestPayload(string Id);

/// <param name="Folder">The destination folder; <c>""</c> is the workspace root.</param>
internal sealed record MoveRequestPayload(string Path, string Folder);

/// <param name="Parent">The destination folder; <c>""</c> is the workspace root.</param>
internal sealed record MoveFolderPayload(string Path, string Parent);

/// <param name="Parent">The containing folder; <c>""</c> is the workspace root.</param>
internal sealed record CreateFolderPayload(string Parent, string Name);

internal sealed record RenameFolderPayload(string Path, string Name);

internal sealed record DeleteFolderPayload(string Path);

/// <param name="Paths">Workspace-relative paths to commit; omitted or empty commits every change.</param>
internal sealed record GitCommitPayload(string Message, IReadOnlyList<string>? Paths = null);

internal sealed record GitConflictPayload(string Path);

/// <param name="Resolution"><c>ours</c>, <c>theirs</c> or <c>edited</c>.</param>
internal sealed record GitResolvePayload(string Path, string Resolution);

internal static class BridgeErrorCodes
{
    public const string UnknownType = "unknown_message";
    public const string BadPayload = "bad_payload";
    public const string Internal = "internal_error";
}
