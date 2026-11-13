using Missive.Application.Common;

namespace Missive.Application.Workspaces;

/// <summary>Holds the workspace folder the app has open. Registered as a singleton.</summary>
public sealed class CurrentWorkspace
{
    private string? _root;

    public string? Root => Volatile.Read(ref _root);

    /// <summary>Raised after a folder is opened as the workspace, with that folder.</summary>
    public event Action<string>? Opened;

    public void Open(string root)
    {
        Volatile.Write(ref _root, root);
        Opened?.Invoke(root);
    }

    internal Result<string> RequireRoot() =>
        Root is { } root
            ? root
            : new Error(ErrorCodes.NoWorkspace, "Open a workspace folder first.");
}
