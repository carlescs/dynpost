using Dynypost.Application.Common;

namespace Dynypost.Application.Workspaces;

/// <summary>Holds the workspace folder the app has open. Registered as a singleton.</summary>
public sealed class CurrentWorkspace
{
    private string? _root;

    public string? Root => Volatile.Read(ref _root);

    public void Open(string root) => Volatile.Write(ref _root, root);

    internal Result<string> RequireRoot() =>
        Root is { } root
            ? root
            : new Error(ErrorCodes.NoWorkspace, "Open a workspace folder first.");
}
