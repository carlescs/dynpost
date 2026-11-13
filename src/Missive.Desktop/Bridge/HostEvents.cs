using System.Diagnostics;
using System.Text.Json;
using Missive.Application.Workspaces;

namespace Missive.Desktop.Bridge;

/// <summary>
/// Tells the UI about changes it didn't make itself (for example, an AI assistant editing requests
/// through the MCP server), so it can re-read what changed. Events are sent once the bridge is
/// connected to the window; until then they are dropped, as there is no UI to update.
/// </summary>
internal sealed class HostEvents
{
    private Func<string, Task>? _send;

    /// <summary>Sends events through <paramref name="send"/> from now on.</summary>
    public void Connect(Func<string, Task> send) => Volatile.Write(ref _send, send);

    /// <summary>Files of the open workspace changed (or, with no paths, something else in it, such as the selected environment).</summary>
    /// <param name="paths">The workspace paths of the files and folders that changed.</param>
    public Task WorkspaceChangedAsync(IReadOnlyList<string> paths) =>
        SendAsync(BridgeEventTypes.WorkspaceChanged, new WorkspaceChangedEvent(paths));

    /// <summary>
    /// The open workspace's files or git state changed (in the app, in other programs or through
    /// other git tools), so the git status may have too.
    /// </summary>
    public Task GitChangedAsync() =>
        SendAsync(BridgeEventTypes.GitChanged, new GitChangedEvent());

    /// <summary>A run of the plan at <paramref name="planPath"/> started outside the UI, so the UI can follow it.</summary>
    public Task RunStartedAsync(string planPath, string runId) =>
        SendAsync(BridgeEventTypes.RunStarted, new RunStartedEvent(planPath, runId));

    /// <summary>Something outside the UI asked it to open a workspace file in a tab.</summary>
    public Task OpenRequestedAsync(WorkspaceItemDto item) =>
        SendAsync(BridgeEventTypes.ItemOpen, item);

    private async Task SendAsync(string type, object payload)
    {
        if (Volatile.Read(ref _send) is not { } send)
        {
            return;
        }

        try
        {
            await send(JsonSerializer.Serialize(new BridgeEvent(type, payload), BridgeRouter.JsonOptions));
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Bridge event '{type}' failed: {ex}");
        }
    }
}
