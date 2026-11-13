namespace Dynpost.Application.Workspaces;

/// <summary>An entry in the workspace explorer tree.</summary>
/// <param name="Path">Workspace-relative path with <c>/</c> separators.</param>
/// <param name="Name">The request name, or the folder name.</param>
/// <param name="Kind"><c>folder</c> or <c>request</c>.</param>
/// <param name="Method">For requests that could be read: the HTTP method.</param>
/// <param name="Error">For request files that could not be read: why.</param>
/// <param name="LocalOnly">For requests: whether the file is kept on this computer only, never committed.</param>
public sealed record WorkspaceNodeDto(
    string Path,
    string Name,
    string Kind,
    string? Method = null,
    string? Error = null,
    IReadOnlyList<WorkspaceNodeDto>? Children = null,
    bool LocalOnly = false);

/// <param name="Migration">
/// When opening the workspace moved it to the current layout (see <see cref="MigrateWorkspaceLayout"/>): what was moved.
/// </param>
public sealed record WorkspaceDto(string Name, string Root, IReadOnlyList<WorkspaceNodeDto> Nodes, LayoutMigrationDto? Migration = null);
