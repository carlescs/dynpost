using Dynypost.Application.Common;
using Dynypost.Domain.Requests;
using Dynypost.Domain.Workspaces;

namespace Dynypost.Application.Workspaces;

/// <summary>A file or folder found in a workspace.</summary>
public closed record WorkspaceEntry(WorkspacePath Path);

public sealed record FolderEntry(WorkspacePath Path, IReadOnlyList<WorkspaceEntry> Children) : WorkspaceEntry(Path);

/// <summary>A request file. <see cref="Request"/> holds the parsed file, or why it could not be read.</summary>
public sealed record RequestFileEntry(WorkspacePath Path, Result<HttpRequestDefinition> Request) : WorkspaceEntry(Path);

/// <summary>Everything inside a folder, recursively, as workspace paths.</summary>
/// <param name="Files">Regular files.</param>
/// <param name="Folders">Sub-folders.</param>
/// <param name="Links">Symbolic links and other special entries, which are never followed.</param>
/// <param name="Unsupported">
/// Entries whose names are not valid workspace paths (such as <c>a:b.txt</c> on Linux), relative
/// to the folder with <c>/</c> separators. Their contents are not listed.
/// </param>
public sealed record FolderContents(
    IReadOnlyList<WorkspacePath> Files,
    IReadOnlyList<WorkspacePath> Folders,
    IReadOnlyList<WorkspacePath> Links,
    IReadOnlyList<string>? Unsupported = null);

/// <summary>Reads and writes request files and folders inside a workspace folder.</summary>
public interface IWorkspaceRepository
{
    /// <summary>Whether <paramref name="root"/> is an existing folder.</summary>
    bool FolderExists(string root);

    /// <summary>
    /// Lists the collection folders and request files in the workspace's <c>requests/</c> folder,
    /// sorted; empty when there is no such folder.
    /// </summary>
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

    /// <summary>Copies a file byte for byte. Fails if the destination already exists.</summary>
    Result<Unit> CopyFile(string root, WorkspacePath from, WorkspacePath to);

    /// <summary>Deletes a file.</summary>
    Result<Unit> DeleteFile(string root, WorkspacePath path);

    /// <summary>Whether a file or a folder exists at <paramref name="path"/>.</summary>
    bool EntryExists(string root, WorkspacePath path);

    bool DirectoryExists(string root, WorkspacePath path);

    Result<Unit> CreateFolder(string root, WorkspacePath path);

    /// <summary>Renames or moves a folder and everything in it. Fails if the destination exists.</summary>
    Result<Unit> MoveFolder(string root, WorkspacePath from, WorkspacePath to);

    Result<FolderContents> ReadFolderContents(string root, WorkspacePath path);

    /// <summary>Deletes a folder and everything in it.</summary>
    Result<Unit> DeleteFolder(string root, WorkspacePath path);
}
