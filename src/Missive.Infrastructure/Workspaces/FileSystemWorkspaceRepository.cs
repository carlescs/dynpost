using Missive.Application.Common;
using Missive.Application.Workspaces;
using Missive.Domain.Requests;
using Missive.Domain.Workspaces;
using Missive.Infrastructure.Yaml;
using static Missive.Infrastructure.Workspaces.WorkspaceFiles;

namespace Missive.Infrastructure.Workspaces;

/// <summary>Stores a workspace as plain YAML files in a folder on disk.</summary>
public sealed class FileSystemWorkspaceRepository : IWorkspaceRepository
{
    public bool FolderExists(string root) => Directory.Exists(root);

    public async Task<Result<IReadOnlyList<WorkspaceEntry>>> ListAsync(string root, CancellationToken cancellationToken = default)
    {
        try
        {
            return Result<IReadOnlyList<WorkspaceEntry>>.Success(
                await ListFolderAsync(root, WorkspacePath.Root, cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return IoError($"Could not read the workspace folder: {ex.Message}");
        }
    }

    public bool FileExists(string root, WorkspacePath path) => File.Exists(FullPath(root, path));

    public async Task<Result<HttpRequestDefinition>> ReadRequestAsync(string root, WorkspacePath path, CancellationToken cancellationToken = default)
    {
        string text;
        try
        {
            text = await File.ReadAllTextAsync(FullPath(root, path), Utf8NoBom, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return new Error(ErrorCodes.NotFound, $"The request file '{path}' does not exist.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return IoError($"Could not read '{path}': {ex.Message}");
        }

        return Parse(text, path);
    }

    public async Task<Result<bool>> WriteRequestAsync(string root, WorkspacePath path, HttpRequestDefinition request, CancellationToken cancellationToken = default)
    {
        var fullPath = FullPath(root, path);
        var content = RequestYamlSerializer.Serialize(request);

        try
        {
            if (File.Exists(fullPath))
            {
                // Leave the file alone when it already holds this request, even if it was formatted
                // by hand: saving must not produce diffs that change nothing.
                var existing = await File.ReadAllTextAsync(fullPath, Utf8NoBom, cancellationToken);
                if (existing == content || IsEquivalent(existing, path, content))
                {
                    return false;
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, content, Utf8NoBom, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return IoError($"Could not write '{path}': {ex.Message}");
        }
    }

    public Result<Unit> MoveFile(string root, WorkspacePath from, WorkspacePath to)
    {
        try
        {
            File.Move(FullPath(root, from), FullPath(root, to), overwrite: false);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return IoError($"Could not rename '{from}' to '{to}': {ex.Message}");
        }
    }

    public bool EntryExists(string root, WorkspacePath path) => Path.Exists(FullPath(root, path));

    public bool DirectoryExists(string root, WorkspacePath path) => Directory.Exists(FullPath(root, path));

    public Result<Unit> CreateFolder(string root, WorkspacePath path)
    {
        try
        {
            Directory.CreateDirectory(FullPath(root, path));
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return IoError($"Could not create '{path}': {ex.Message}");
        }
    }

    public Result<Unit> MoveFolder(string root, WorkspacePath from, WorkspacePath to)
    {
        var source = FullPath(root, from);
        var destination = FullPath(root, to);
        try
        {
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            {
                // Case-only rename: go through a temporary name so it also works on
                // case-insensitive file systems.
                var temporary = destination + ".missive-rename-" + Guid.NewGuid().ToString("N")[..8];
                Directory.Move(source, temporary);
                Directory.Move(temporary, destination);
            }
            else
            {
                if (Path.Exists(destination))
                {
                    return new Error(ErrorCodes.AlreadyExists, $"'{to}' already exists.");
                }

                Directory.Move(source, destination);
            }

            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return IoError($"Could not rename '{from}' to '{to}': {ex.Message}");
        }
    }

    public Result<FolderContents> ReadFolderContents(string root, WorkspacePath path)
    {
        var files = new List<WorkspacePath>();
        var folders = new List<WorkspacePath>();
        var links = new List<WorkspacePath>();

        try
        {
            Collect(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return IoError($"Could not read '{path}': {ex.Message}");
        }

        return new FolderContents(files, folders, links);

        void Collect(WorkspacePath folder)
        {
            foreach (var info in new DirectoryInfo(FullPath(root, folder)).EnumerateFileSystemInfos())
            {
                if (!WorkspacePath.TryParse(info.Name, out _))
                {
                    continue;
                }

                var child = folder.Combine(info.Name);
                if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    links.Add(child);
                }
                else if (info is DirectoryInfo)
                {
                    folders.Add(child);
                    Collect(child);
                }
                else
                {
                    files.Add(child);
                }
            }
        }
    }

    public Result<Unit> DeleteFolder(string root, WorkspacePath path)
    {
        try
        {
            Directory.Delete(FullPath(root, path), recursive: true);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return IoError($"Could not delete '{path}': {ex.Message}");
        }
    }

    private async Task<IReadOnlyList<WorkspaceEntry>> ListFolderAsync(string root, WorkspacePath folder, CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(FullPath(root, folder));
        var folders = new List<WorkspaceEntry>();
        var files = new List<WorkspaceEntry>();

        foreach (var info in directory.EnumerateFileSystemInfos().OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Symbolic links could point outside the workspace or form cycles.
            if (info.LinkTarget is not null || !WorkspacePath.TryParse(info.Name, out _))
            {
                continue;
            }

            if (info is DirectoryInfo && WorkspaceLayout.IsCollectionFolder(folder, info.Name))
            {
                var path = folder.Combine(info.Name);
                folders.Add(new FolderEntry(path, await ListFolderAsync(root, path, cancellationToken)));
            }
            else if (info is FileInfo && WorkspaceLayout.IsRequestFile(folder, info.Name))
            {
                var path = folder.Combine(info.Name);
                files.Add(new RequestFileEntry(path, await ReadRequestAsync(root, path, cancellationToken)));
            }
        }

        return [.. folders, .. files];
    }

    private static Result<HttpRequestDefinition> Parse(string text, WorkspacePath path)
    {
        try
        {
            return RequestYamlSerializer.Deserialize(text, WorkspaceLayout.FileStem(path.FileName));
        }
        catch (RequestFormatException ex)
        {
            return new Error(ErrorCodes.InvalidFile, $"'{path}' is not a valid request file. {ex.Message}");
        }
    }

    private static bool IsEquivalent(string existing, WorkspacePath path, string content)
    {
        var parsed = Parse(existing, path);
        return parsed.IsSuccess && RequestYamlSerializer.Serialize(parsed.Value) == content;
    }

    private static Error IoError(string message) => new(ErrorCodes.Io, message);
}
