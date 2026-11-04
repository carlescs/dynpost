using Missive.Application.Common;
using Missive.Domain.Requests;
using Missive.Domain.Workspaces;

namespace Missive.Application.Workspaces;

/// <summary>A file or folder found in a workspace.</summary>
public abstract record WorkspaceEntry(WorkspacePath Path);

public sealed record FolderEntry(WorkspacePath Path, IReadOnlyList<WorkspaceEntry> Children) : WorkspaceEntry(Path);

/// <summary>A request file. <see cref="Request"/> holds the parsed file, or why it could not be read.</summary>
public sealed record RequestFileEntry(WorkspacePath Path, Result<HttpRequestDefinition> Request) : WorkspaceEntry(Path);

/// <summary>Reads and writes request files inside a workspace folder.</summary>
public interface IWorkspaceRepository
{
    /// <summary>Whether <paramref name="root"/> is an existing folder.</summary>
    bool FolderExists(string root);

    /// <summary>Lists the collection folders and request files in the workspace, sorted.</summary>
    Task<Result<IReadOnlyList<WorkspaceEntry>>> ListAsync(string root, CancellationToken cancellationToken = default);

    bool FileExists(string root, WorkspacePath path);

    Task<Result<HttpRequestDefinition>> ReadRequestAsync(string root, WorkspacePath path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a request file, creating its folder if needed. Leaves the file untouched when its
    /// content would not change; the result says whether the file was written.
    /// </summary>
    Task<Result<bool>> WriteRequestAsync(string root, WorkspacePath path, HttpRequestDefinition request, CancellationToken cancellationToken = default);

    /// <summary>Renames or moves a file. Fails if the destination already exists.</summary>
    Result<Unit> MoveFile(string root, WorkspacePath from, WorkspacePath to);
}
