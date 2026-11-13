using System.Text.Json;
using Dynpost.Application.Common;
using Dynpost.Application.Environments;
using Dynpost.Application.Folders;
using Dynpost.Application.Requests;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Desktop.Bridge;

/// <summary>
/// The message types the UI can send. Keep in sync with
/// <c>src/Dynpost.Client/src/app/models/messages.ts</c>.
/// </summary>
internal static class BridgeMessageTypes
{
    /// <summary>Shows a folder picker and opens the chosen folder. Payload: none. Result: workspace or null if cancelled.</summary>
    public const string WorkspacePick = "workspace.pick";

    /// <summary>Opens a folder by path. Payload: <see cref="OpenWorkspacePayload"/>. Result: workspace.</summary>
    public const string WorkspaceOpen = "workspace.open";

    /// <summary>Reopens the workspace from the previous session. Payload: none. Result: workspace, or null if there is none.</summary>
    public const string WorkspaceOpenLast = "workspace.openLast";

    /// <summary>
    /// Lists the workspace folders opened recently, the most recent first; folders that no longer
    /// exist are dropped. Payload: none. Result: recent workspaces.
    /// </summary>
    public const string WorkspaceRecent = "workspace.recent";

    /// <summary>Removes a folder from recently opened workspaces. Payload: <see cref="RemoveRecentWorkspacePayload"/>. Result: none.</summary>
    public const string WorkspaceRecentRemove = "workspace.recent.remove";

    /// <summary>Re-reads the open workspace. Payload: none. Result: workspace.</summary>
    public const string WorkspaceTree = "workspace.tree";

    /// <summary>Shows the open workspace folder in the system file manager. Payload: none. Result: none.</summary>
    public const string WorkspaceReveal = "workspace.reveal";

    /// <summary>Lists the formats the workspace can be exported to. Payload: none. Result: formats.</summary>
    public const string WorkspaceExportFormats = "workspace.exportFormats";

    /// <summary>
    /// Asks where to save, then exports the workspace for other apps. Payload:
    /// <see cref="ExportWorkspacePayload"/>. Result: what was written, or null if cancelled.
    /// </summary>
    public const string WorkspaceExport = "workspace.export";

    /// <summary>
    /// Asks for a file exported by another app and imports it into a folder. Payload:
    /// <see cref="ImportCollectionPayload"/>. Result: what was imported, or null if cancelled.
    /// </summary>
    public const string WorkspaceImport = "workspace.import";

    /// <summary>Reads a request file. Payload: <see cref="LoadRequestPayload"/>. Result: request.</summary>
    public const string RequestLoad = "request.load";

    /// <summary>Shows a native file picker for a request body. Payload: none. Result: file path or null if cancelled.</summary>
    public const string RequestPickFile = "request.pickFile";

    /// <summary>
    /// Shows a native file picker for the file fields of a form body. Payload:
    /// <see cref="PickRequestFilesPayload"/>. Result: the picked files (empty if cancelled).
    /// </summary>
    public const string RequestPickFiles = "request.pickFiles";

    /// <summary>
    /// Describes the files a request body sends (size, missing, outside the workspace). Payload:
    /// <see cref="InspectRequestFilesPayload"/>. Result: one entry per distinct path.
    /// </summary>
    public const string RequestInspectFiles = "request.inspectFiles";

    /// <summary>Saves a new request in a folder. Payload: <see cref="CreateRequestPayload"/>. Result: saved request.</summary>
    public const string RequestCreate = "request.create";

    /// <summary>Saves an existing request file. Payload: <see cref="SaveRequestPayload"/>. Result: saved request.</summary>
    public const string RequestSave = "request.save";

    /// <summary>Copies a request as "… copy" next to it. Payload: <see cref="DuplicateRequestPayload"/>. Result: the saved copy.</summary>
    public const string RequestDuplicate = "request.duplicate";

    /// <summary>Moves a request file into another folder. Payload: <see cref="MoveRequestPayload"/>. Result: its new path.</summary>
    public const string RequestMove = "request.move";

    /// <summary>Deletes a request file. Payload: <see cref="DeleteRequestPayload"/>. Result: none.</summary>
    public const string RequestDelete = "request.delete";

    /// <summary>
    /// Shares a request, environment or run plan file through git, or keeps it on this computer
    /// only. Payload: <see cref="SetSharingPayload"/>. Result: the file's sharing.
    /// </summary>
    public const string SharingSet = "sharing.set";

    /// <summary>
    /// Sends a request (saved or not) and waits for the response. Payload: <see cref="SendRequestPayload"/>.
    /// Result: response.
    /// </summary>
    public const string RequestSend = "request.send";

    /// <summary>Cancels a request being sent. Payload: <see cref="CancelRequestPayload"/>. Result: whether it was being sent.</summary>
    public const string RequestCancel = "request.cancel";

    /// <summary>
    /// Asks where to save, then writes the body of a recent response exactly as received.
    /// Payload: <see cref="SaveResponseBodyPayload"/>. Result: <see cref="SavedFileDto"/>, or null if cancelled.
    /// </summary>
    public const string ResponseSaveBody = "response.saveBody";

    /// <summary>
    /// Lists the responses kept in a request's history, newest first. Payload:
    /// <see cref="HistoryPathPayload"/>. Result: history entries.
    /// </summary>
    public const string HistoryList = "history.list";

    /// <summary>Reads one response of a request's history. Payload: <see cref="HistoryEntryPayload"/>. Result: the entry with its response.</summary>
    public const string HistoryGet = "history.get";

    /// <summary>
    /// Deletes one response from a request's history, or all of them when no id is given.
    /// Payload: <see cref="DeleteHistoryPayload"/>. Result: the entries left.
    /// </summary>
    public const string HistoryDelete = "history.delete";

    /// <summary>Lists the cookies kept for an environment. Payload: <see cref="CookieJarPayload"/>. Result: cookies.</summary>
    public const string CookieList = "cookie.list";

    /// <summary>Deletes one cookie from an environment's jar. Payload: <see cref="DeleteCookiePayload"/>. Result: the cookies left.</summary>
    public const string CookieDelete = "cookie.delete";

    /// <summary>Deletes every cookie kept for an environment. Payload: <see cref="CookieJarPayload"/>. Result: none.</summary>
    public const string CookieClear = "cookie.clear";

    /// <summary>Lists the values requests captured for an environment. Payload: <see cref="CapturedValuesPayload"/>. Result: captured values.</summary>
    public const string CaptureList = "capture.list";

    /// <summary>
    /// Forgets one captured value of an environment, or all of them when no name is given.
    /// Payload: <see cref="ClearCapturedValuesPayload"/>. Result: the captured values left.
    /// </summary>
    public const string CaptureClear = "capture.clear";

    /// <summary>
    /// Obtains a new OAuth 2.0 token for the auth in the editor and the selected environment
    /// ('Get new token'); the authorization code grant opens the system browser and waits for the
    /// user to sign in. Payload: <see cref="OAuth2TokenPayload"/>. Result: the token's status.
    /// </summary>
    public const string OAuth2TokenGet = "oauth2.token.get";

    /// <summary>
    /// The cached OAuth 2.0 token for the auth in the editor and the selected environment.
    /// Payload: <see cref="OAuth2TokenPayload"/>. Result: the token's status, or <c>null</c>.
    /// </summary>
    public const string OAuth2TokenStatus = "oauth2.token.status";

    /// <summary>Forgets the cached OAuth 2.0 token. Payload: <see cref="OAuth2TokenPayload"/>. Result: none.</summary>
    public const string OAuth2TokenClear = "oauth2.token.clear";

    /// <summary>Stops waiting for a browser sign-in. Payload: none. Result: whether one was in progress.</summary>
    public const string OAuth2SignInCancel = "oauth2.signIn.cancel";

    /// <summary>Creates a folder. Payload: <see cref="CreateFolderPayload"/>. Result: folder.</summary>
    public const string FolderCreate = "folder.create";

    /// <summary>Renames a folder. Payload: <see cref="RenameFolderPayload"/>. Result: folder (new path).</summary>
    public const string FolderRename = "folder.rename";

    /// <summary>Moves a folder into another folder. Payload: <see cref="MoveFolderPayload"/>. Result: folder (new path).</summary>
    public const string FolderMove = "folder.move";

    /// <summary>Copies a folder with its contents as "… copy" next to it. Payload: <see cref="DuplicateFolderPayload"/>. Result: the copy.</summary>
    public const string FolderDuplicate = "folder.duplicate";

    /// <summary>Deletes a folder with its requests. Payload: <see cref="DeleteFolderPayload"/>. Result: none.</summary>
    public const string FolderDelete = "folder.delete";

    /// <summary>Reads the headers and auth a folder sets (its <c>_folder.yaml</c>). Payload: <see cref="FolderSettingsPathPayload"/>. Result: folder settings.</summary>
    public const string FolderSettingsLoad = "folder.settings.load";

    /// <summary>Saves the headers and auth a folder sets. Payload: <see cref="SaveFolderSettingsPayload"/>. Result: folder settings as saved.</summary>
    public const string FolderSettingsSave = "folder.settings.save";

    /// <summary>
    /// What requests and folders directly inside a folder inherit from it and the folders above.
    /// Payload: <see cref="FolderSettingsPathPayload"/>. Result: inherited settings.
    /// </summary>
    public const string FolderSettingsInherited = "folder.settings.inherited";

    /// <summary>Lists the environments and the selected one. Payload: none. Result: environment list.</summary>
    public const string EnvironmentList = "environment.list";

    /// <summary>Reads an environment with its secret values. Payload: <see cref="EnvironmentIdPayload"/>. Result: environment.</summary>
    public const string EnvironmentLoad = "environment.load";

    /// <summary>Saves a new environment. Payload: <see cref="CreateEnvironmentPayload"/>. Result: saved environment.</summary>
    public const string EnvironmentCreate = "environment.create";

    /// <summary>Saves an existing environment. Payload: <see cref="SaveEnvironmentPayload"/>. Result: saved environment.</summary>
    public const string EnvironmentSave = "environment.save";

    /// <summary>Deletes an environment. Payload: <see cref="EnvironmentIdPayload"/>. Result: none.</summary>
    public const string EnvironmentDelete = "environment.delete";

    /// <summary>Selects the environment used to send requests. Payload: <see cref="SelectEnvironmentPayload"/>. Result: none.</summary>
    public const string EnvironmentSelect = "environment.select";

    /// <summary>Lists the run plans (<c>*.run.yaml</c>) of the workspace. Payload: none. Result: plan files.</summary>
    public const string RunPlanList = "runPlan.list";

    /// <summary>Opens a run plan for editing. Payload: <see cref="RunPlanPathPayload"/>. Result: plan document.</summary>
    public const string RunPlanOpen = "runPlan.open";

    /// <summary>Saves the text of a run plan, creating the file if needed. Payload: <see cref="SaveRunPlanPayload"/>. Result: plan document.</summary>
    public const string RunPlanSave = "runPlan.save";

    /// <summary>Deletes a run plan file and its saved runs. Payload: <see cref="RunPlanPathPayload"/>. Result: none.</summary>
    public const string RunPlanDelete = "runPlan.delete";

    /// <summary>Moves a run plan file into another folder. Payload: <see cref="MoveRunPlanPayload"/>. Result: its new path.</summary>
    public const string RunPlanMove = "runPlan.move";

    /// <summary>
    /// The folders shown in the Load runs view: those holding run plans, or no requests at all.
    /// Payload: none. Result: folder paths, parents first.
    /// </summary>
    public const string RunFolderList = "runFolder.list";

    /// <summary>Renames a folder of run plans; their runs follow. Payload: <see cref="RenameFolderPayload"/>. Result: folder (new path).</summary>
    public const string RunFolderRename = "runFolder.rename";

    /// <summary>Moves a folder of run plans into another folder. Payload: <see cref="MoveFolderPayload"/>. Result: folder (new path).</summary>
    public const string RunFolderMove = "runFolder.move";

    /// <summary>Copies a folder of run plans as "… copy" next to it. Payload: <see cref="DuplicateFolderPayload"/>. Result: the copy.</summary>
    public const string RunFolderDuplicate = "runFolder.duplicate";

    /// <summary>Deletes a folder of run plans with their saved runs. Payload: <see cref="DeleteFolderPayload"/>. Result: none.</summary>
    public const string RunFolderDelete = "runFolder.delete";

    /// <summary>
    /// Starts a load run in the background. Payload: <see cref="StartRunPayload"/>. Result: run id.
    /// Fails with <c>confirmation_required</c> for very large runs until <c>confirmLarge</c> is set.
    /// </summary>
    public const string RunStart = "run.start";

    /// <summary>Stops a run; the partial results are kept. Payload: <see cref="RunIdPayload"/>. Result: none.</summary>
    public const string RunCancel = "run.cancel";

    /// <summary>The live (or saved) numbers of a run, polled by the dashboard. Payload: <see cref="RunIdPayload"/>. Result: run.</summary>
    public const string RunProgress = "run.progress";

    /// <summary>
    /// A page of the request log of a run, live or finished. Payload: <see cref="RunSamplesPayload"/>.
    /// Result: samples page.
    /// </summary>
    public const string RunSamples = "run.samples";

    /// <summary>
    /// The whole body of a response a run saved to a file (plans with <c>saveResponses</c>).
    /// Payload: <see cref="RunResponsePayload"/>. Result: the body as base64.
    /// </summary>
    public const string RunResponse = "run.response";

    /// <summary>The earlier runs of a plan, newest first. Payload: <see cref="RunPlanPathPayload"/>. Result: run summaries.</summary>
    public const string RunList = "run.list";

    /// <summary>Deletes earlier runs and their request logs. Payload: <see cref="RunIdsPayload"/>. Result: none.</summary>
    public const string RunDelete = "run.delete";

    /// <summary>Deletes all the earlier runs of a plan. Payload: <see cref="RunPlanPathPayload"/>. Result: none.</summary>
    public const string RunClear = "run.clear";

    /// <summary>Reads the open workspace's git status. Payload: none. Result: git status.</summary>
    public const string GitStatus = "git.status";

    /// <summary>Creates a git repository in the open workspace folder. Payload: none. Result: new status.</summary>
    public const string GitInit = "git.init";

    /// <summary>Commits changes. Payload: <see cref="GitCommitPayload"/>. Result: commit id and new status.</summary>
    public const string GitCommit = "git.commit";

    /// <summary>Pulls the current branch (merge, never rebase). Payload: none. Result: outcome and new status.</summary>
    public const string GitPull = "git.pull";

    /// <summary>Pushes (or publishes) the current branch, never forcing. Payload: none. Result: new status.</summary>
    public const string GitPush = "git.push";

    /// <summary>Downloads the remotes' latest commits without changing files. Payload: none. Result: new status.</summary>
    public const string GitFetch = "git.fetch";

    /// <summary>Reads the configured commit name and email. Payload: none. Result: the identity (parts may be missing).</summary>
    public const string GitIdentity = "git.identity";

    /// <summary>Sets the commit name and email. Payload: <see cref="GitSetIdentityPayload"/>. Result: the identity.</summary>
    public const string GitSetIdentity = "git.identity.set";

    /// <summary>Reads both sides of a conflicted file. Payload: <see cref="GitConflictPayload"/>. Result: the versions.</summary>
    public const string GitConflict = "git.conflict";

    /// <summary>Resolves one conflicted file as the user chose. Payload: <see cref="GitResolvePayload"/>. Result: new status.</summary>
    public const string GitResolve = "git.resolve";

    /// <summary>Discards uncommitted changes to files. Payload: <see cref="GitDiscardPayload"/>. Result: new status.</summary>
    public const string GitDiscard = "git.discard";

    /// <summary>Abandons the merge in progress. Payload: none. Result: new status.</summary>
    public const string GitAbortMerge = "git.abortMerge";

    /// <summary>
    /// Reads a page of the commits that changed the workspace, newest first. Payload:
    /// <see cref="GitLogPayload"/>. Result: commits and whether more follow.
    /// </summary>
    public const string GitLog = "git.log";

    /// <summary>Reads a commit with its message and changed files. Payload: <see cref="GitCommitIdPayload"/>. Result: commit details.</summary>
    public const string GitShow = "git.show";

    /// <summary>
    /// Reads a file before and after a commit. Payload: <see cref="GitCommitFilePayload"/>. Result:
    /// both versions.
    /// </summary>
    public const string GitCommitFile = "git.commitFile";

    /// <summary>
    /// Reads the committed and working tree versions of a changed file, parsing request files.
    /// Payload: <see cref="GitDiffPayload"/>. Result: the diff.
    /// </summary>
    public const string GitDiff = "git.diff";

    /// <summary>Lists the local and remote-tracking branches. Payload: none. Result: status and branches.</summary>
    public const string GitBranchList = "git.branch.list";

    /// <summary>
    /// Checks out a local branch; refused with uncommitted changes. Payload:
    /// <see cref="GitBranchNamePayload"/>. Result: status and branches.
    /// </summary>
    public const string GitBranchSwitch = "git.branch.switch";

    /// <summary>Creates a branch at HEAD. Payload: <see cref="GitCreateBranchPayload"/>. Result: status and branches.</summary>
    public const string GitBranchCreate = "git.branch.create";

    /// <summary>
    /// Checks out a remote-tracking branch as a new local tracking branch. Payload:
    /// <see cref="GitBranchNamePayload"/> (e.g. <c>origin/feature</c>). Result: status and branches.
    /// </summary>
    public const string GitBranchCheckout = "git.branch.checkout";

    /// <summary>
    /// Deletes a local branch; an unmerged one only with <c>force</c>. Payload:
    /// <see cref="GitDeleteBranchPayload"/>. Result: status and branches.
    /// </summary>
    public const string GitBranchDelete = "git.branch.delete";

    /// <summary>
    /// Deletes a branch on its remote (never forced). Payload: <see cref="GitBranchNamePayload"/>
    /// (the remote-tracking branch, e.g. <c>origin/feature</c>). Result: status and branches.
    /// </summary>
    public const string GitBranchDeleteRemote = "git.branch.deleteRemote";

    /// <summary>
    /// Merges a local or remote-tracking branch into the current one (never a rebase); refused with
    /// uncommitted changes. Payload: <see cref="GitMergeBranchPayload"/>. Result: outcome, status,
    /// branches and any conflicts outside the workspace folder.
    /// </summary>
    public const string GitBranchMerge = "git.branch.merge";

    /// <summary>Lists the repository's remotes. Payload: none. Result: remotes.</summary>
    public const string GitRemoteList = "git.remote.list";

    /// <summary>Adds a remote. Payload: <see cref="GitAddRemotePayload"/>. Result: remotes.</summary>
    public const string GitRemoteAdd = "git.remote.add";

    /// <summary>Renames a remote and/or changes its URL. Payload: <see cref="GitUpdateRemotePayload"/>. Result: remotes.</summary>
    public const string GitRemoteUpdate = "git.remote.update";

    /// <summary>Removes a remote. Payload: <see cref="GitRemoteNamePayload"/>. Result: remotes.</summary>
    public const string GitRemoteRemove = "git.remote.remove";

    /// <summary>Reads the app version and platform. Payload: none. Result: about info.</summary>
    public const string AboutGet = "about.get";

    /// <summary>Reads how the operating system formats dates and times. Payload: none. Result: date format.</summary>
    public const string LocaleDateFormat = "locale.dateFormat";

    /// <summary>
    /// Looks for a newer version of the app and downloads it. Payload: none. Result: update status.
    /// </summary>
    public const string UpdateCheck = "update.check";

    /// <summary>Installs the downloaded update and restarts the app. Payload: none. Result: none (the app exits).</summary>
    public const string UpdateRestart = "update.restart";

    /// <summary>Closes the app window, which ends the app. Payload: none. Result: none.</summary>
    public const string AppQuit = "app.quit";

    /// <summary>Reads the user settings. Payload: none. Result: settings.</summary>
    public const string SettingsGet = "settings.get";

    /// <summary>Sets the color theme. Payload: <see cref="SetThemePayload"/>. Result: settings.</summary>
    public const string SettingsSetTheme = "settings.setTheme";

    /// <summary>Changes how responses are kept in the history. Payload: <see cref="HistorySettingsDto"/>. Result: settings.</summary>
    public const string SettingsSetHistory = "settings.setHistory";

    /// <summary>Sets the order of the activity bar's views. Payload: <see cref="SetActivityOrderPayload"/>. Result: settings.</summary>
    public const string SettingsSetActivityOrder = "settings.setActivityOrder";

    /// <summary>Reads the MCP server's settings and state. Payload: none. Result: MCP status.</summary>
    public const string McpStatus = "mcp.status";

    /// <summary>Turns the MCP server on or off, or changes its port. Payload: <see cref="SetMcpPayload"/>. Result: MCP status.</summary>
    public const string McpSet = "mcp.set";

    /// <summary>Replaces the MCP server's token. Payload: none. Result: MCP status.</summary>
    public const string McpRegenerateToken = "mcp.regenerateToken";
}

/// <summary>
/// The events the host sends the UI unprompted, as a <see cref="BridgeEvent"/>. Keep in sync with
/// <c>src/Dynpost.Client/src/app/models/messages.ts</c>.
/// </summary>
internal static class BridgeEventTypes
{
    /// <summary>Something changed the open workspace outside the UI (the MCP server). Payload: <see cref="WorkspaceChangedEvent"/>.</summary>
    public const string WorkspaceChanged = "workspace.changed";

    /// <summary>A load run started outside the UI (the MCP server). Payload: <see cref="RunStartedEvent"/>.</summary>
    public const string RunStarted = "run.started";

    /// <summary>
    /// Something outside the UI (the MCP server) asks it to open a request, run plan or
    /// environment in a tab. Payload: <see cref="Dynpost.Application.Workspaces.WorkspaceItemDto"/>.
    /// </summary>
    public const string ItemOpen = "item.open";

    /// <summary>
    /// The open workspace's files or git state changed, by anyone, so the UI should read the git
    /// status again. Payload: <see cref="GitChangedEvent"/>.
    /// </summary>
    public const string GitChanged = "git.changed";
}

/// <summary>A message from the host the UI didn't ask for; unlike a <see cref="BridgeResponse"/> it has no id.</summary>
internal sealed record BridgeEvent(string Event, object Payload);

/// <param name="Paths">The workspace paths of the files and folders that changed; empty when only other state (such as the selected environment) did.</param>
internal sealed record WorkspaceChangedEvent(IReadOnlyList<string> Paths);

/// <summary>The payload of <see cref="BridgeEventTypes.GitChanged"/>: nothing, the UI reads the status again.</summary>
internal sealed record GitChangedEvent;

/// <param name="Path">The workspace path of the plan being run.</param>
internal sealed record RunStartedEvent(string Path, string RunId);

/// <summary>A message from the UI. <see cref="Id"/> correlates the response.</summary>
internal sealed record BridgeRequest(string Id, string Type, JsonElement? Payload);

/// <summary>The answer to a <see cref="BridgeRequest"/>: either <see cref="Payload"/> or <see cref="Error"/>.</summary>
internal sealed record BridgeResponse(string Id, bool Ok, object? Payload = null, Error? Error = null);

internal sealed record OpenWorkspacePayload(string Path);
internal sealed record RemoveRecentWorkspacePayload(string Path);

internal sealed record SetThemePayload(string Theme);
internal sealed record SetActivityOrderPayload(IReadOnlyList<string>? Order);

/// <param name="Port">The port to listen on; <c>null</c> keeps the current one.</param>
internal sealed record SetMcpPayload(bool Enabled, int? Port = null);

/// <param name="Format">The id of an export format, e.g. <c>postman</c> or <c>insomnia</c>.</param>
/// <param name="IncludeEnvironments">Whether to export the environments (secret values never are).</param>
internal sealed record ExportWorkspacePayload(string Format, bool IncludeEnvironments = true);

/// <param name="Folder">The folder to import into: <c>requests</c> or a folder inside it.</param>
internal sealed record ImportCollectionPayload(string Folder = WorkspaceLayout.RequestsFolder);

internal sealed record LoadRequestPayload(string Path);
internal sealed record PickRequestFilesPayload(bool Multiple = false);

internal sealed record InspectRequestFilesPayload(IReadOnlyList<string> Paths);

internal sealed record CreateRequestPayload(string Folder, RequestDto Request);

internal sealed record SaveRequestPayload(string Path, RequestDto Request);

/// <param name="Id">Chosen by the UI; pass it to <see cref="BridgeMessageTypes.RequestCancel"/> to cancel.</param>
/// <param name="Path">The request file the request belongs to, whose history keeps the response; <c>null</c> when unsaved.</param>
/// <param name="Folder">The folder an unsaved request will be saved in; it inherits that folder's headers and auth.</param>
internal sealed record SendRequestPayload(string Id, RequestDto Request, string? Path = null, string? Folder = null);

/// <param name="Path">The workspace path of the request file.</param>
internal sealed record HistoryPathPayload(string Path);

internal sealed record HistoryEntryPayload(string Path, string Id);

/// <param name="Id">The entry to delete; omitted deletes the request's whole history.</param>
internal sealed record DeleteHistoryPayload(string Path, string? Id = null);

internal sealed record CancelRequestPayload(string Id);

/// <param name="Id">The id the response's request was sent with.</param>
internal sealed record SaveResponseBodyPayload(string Id);

internal sealed record SavedFileDto(string Path);

/// <param name="Environment">The environment's file name in the <c>environments</c> folder.</param>
internal sealed record CookieJarPayload(string Environment);

/// <param name="Environment">The environment's file name in the <c>environments</c> folder.</param>
internal sealed record CapturedValuesPayload(string Environment);

/// <param name="Name">The variable to forget; omitted forgets every captured value.</param>
internal sealed record ClearCapturedValuesPayload(string Environment, string? Name = null);

/// <param name="Auth">The auth settings as they are in the editor (saved or not).</param>
internal sealed record OAuth2TokenPayload(AuthDto Auth);

internal sealed record DeleteCookiePayload(string Environment, string Name, string Domain, string Path);

/// <param name="Folder">The destination folder: <c>requests</c> or a folder inside it.</param>
internal sealed record MoveRequestPayload(string Path, string Folder);

/// <param name="Parent">The destination folder: <c>requests</c> or a folder inside it.</param>
internal sealed record MoveFolderPayload(string Path, string Parent);

/// <param name="Parent">The containing folder: <c>requests</c> or a folder inside it.</param>
internal sealed record CreateFolderPayload(string Parent, string Name);

internal sealed record RenameFolderPayload(string Path, string Name);

internal sealed record DeleteRequestPayload(string Path);

/// <param name="Path">The workspace-relative file; an environment is <c>environments/&lt;id&gt;</c>.</param>
internal sealed record SetSharingPayload(string Path, bool LocalOnly);

internal sealed record DuplicateRequestPayload(string Path);

internal sealed record DuplicateFolderPayload(string Path);

internal sealed record DeleteFolderPayload(string Path);

/// <param name="Path">The folder: <c>requests</c> or a folder inside it.</param>
internal sealed record FolderSettingsPathPayload(string Path);

internal sealed record SaveFolderSettingsPayload(string Path, FolderSettingsDto Settings);

/// <param name="Id">The environment's file name in the <c>environments</c> folder.</param>
internal sealed record EnvironmentIdPayload(string Id);

internal sealed record CreateEnvironmentPayload(EnvironmentDto Environment);

internal sealed record SaveEnvironmentPayload(string Id, EnvironmentDto Environment);

/// <param name="Id">The environment to select, or <c>null</c> for none.</param>
internal sealed record SelectEnvironmentPayload(string? Id);

/// <param name="Paths">Workspace-relative paths to commit; omitted or empty commits every change.</param>
internal sealed record GitDiscardPayload(IReadOnlyList<string> Paths);

internal sealed record GitCommitPayload(string Message, IReadOnlyList<string>? Paths = null);

internal sealed record GitConflictPayload(string Path);

/// <param name="Global">Whether to use it in all of the user's repositories, not only the workspace's.</param>
internal sealed record GitSetIdentityPayload(string Name, string Email, bool Global = true);

/// <param name="Resolution"><c>ours</c>, <c>theirs</c> or <c>edited</c>.</param>
internal sealed record GitResolvePayload(string Path, string Resolution);

/// <param name="Skip">How many of the newest commits to skip; 0 for the first page.</param>
/// <param name="Limit">The page size; omitted uses the default.</param>
internal sealed record GitLogPayload(int Skip = 0, int? Limit = null);

internal sealed record GitCommitIdPayload(string CommitId);

internal sealed record GitCommitFilePayload(string CommitId, string Path);

internal sealed record GitDiffPayload(string Path);

internal sealed record GitAddRemotePayload(string Name, string Url);

/// <param name="Name">The remote to change.</param>
/// <param name="NewName">Its new name (the same name keeps it).</param>
/// <param name="Url">Its new URL (the current URL keeps it).</param>
internal sealed record GitUpdateRemotePayload(string Name, string NewName, string Url);

internal sealed record GitRemoteNamePayload(string Name);

internal sealed record GitBranchNamePayload(string Name);

/// <param name="SwitchTo">Whether to check out the new branch.</param>
internal sealed record GitCreateBranchPayload(string Name, bool SwitchTo = false);

/// <param name="Force">Delete even if its commits aren't merged anywhere (after a second confirmation).</param>
internal sealed record GitDeleteBranchPayload(string Name, bool Force = false);

/// <param name="Remote">Whether <c>Name</c> is a remote-tracking branch (a local branch can share its name).</param>
internal sealed record GitMergeBranchPayload(string Name, bool Remote = false);

internal static class BridgeErrorCodes
{
    public const string UnknownType = "unknown_message";
    public const string BadPayload = "bad_payload";
    public const string Internal = "internal_error";
}

internal sealed record RunPlanPathPayload(string Path);

internal sealed record SaveRunPlanPayload(string Path, string Yaml);

/// <param name="Folder">The destination folder: <c>plans</c> or a folder inside it.</param>
internal sealed record MoveRunPlanPayload(string Path, string Folder);

internal sealed record StartRunPayload(string Path, bool ConfirmLarge = false);

internal sealed record RunIdPayload(string RunId);

internal sealed record RunIdsPayload(IReadOnlyList<string> RunIds);

internal sealed record RunResponsePayload(string RunId, string File);

internal sealed record RunSamplesPayload(string RunId, int Offset = 0, int Limit = 100, bool FailedOnly = false);
