using Dynpost.Application.Common;
using Dynpost.Application.Environments;
using Dynpost.Domain.Environments;
using Dynpost.Domain.Workspaces;
using Dynpost.Infrastructure.Yaml;
using static Dynpost.Infrastructure.Workspaces.WorkspaceFiles;

namespace Dynpost.Infrastructure.Environments;

/// <summary>Stores environments as YAML files in the workspace's <c>environments</c> folder.</summary>
public sealed class FileSystemEnvironmentStore : IEnvironmentStore
{
    private static readonly WorkspacePath Folder = WorkspacePath.Root.Combine(WorkspaceLayout.EnvironmentsFolder);

    public async Task<Result<IReadOnlyList<EnvironmentFile>>> ListAsync(string root, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = new DirectoryInfo(FullPath(root, Folder));
            if (!directory.Exists)
            {
                return Result<IReadOnlyList<EnvironmentFile>>.Success([]);
            }

            var files = new List<EnvironmentFile>();
            foreach (var file in directory.EnumerateFiles().OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                // Symbolic links could point outside the workspace.
                if (file.LinkTarget is null && EnvironmentMapper.IsValidId(file.Name))
                {
                    files.Add(new EnvironmentFile(file.Name, await ReadAsync(root, file.Name, cancellationToken)));
                }
            }

            return files;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return IoError($"Could not read the environments folder: {ex.Message}");
        }
    }

    public bool Exists(string root, string id) => File.Exists(PathOf(root, id));

    public async Task<Result<EnvironmentDefinition>> ReadAsync(string root, string id, CancellationToken cancellationToken = default)
    {
        string? text;
        try
        {
            text = await ReadIfExistsAsync(PathOf(root, id), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return IoError($"Could not read '{Display(id)}': {ex.Message}");
        }

        return text is null
            ? new Error(ErrorCodes.NotFound, $"The environment file '{Display(id)}' does not exist.")
            : Parse(text, id);
    }

    public async Task<Result<bool>> WriteAsync(string root, string id, EnvironmentDefinition environment, CancellationToken cancellationToken = default)
    {
        var content = EnvironmentYamlSerializer.Serialize(environment);
        try
        {
            var fullPath = PathOf(root, id);
            // Leave the file alone when it already holds this environment, even if it was formatted
            // by hand: saving must not produce diffs that change nothing.
            var existing = await ReadIfExistsAsync(fullPath, cancellationToken);
            if (existing is not null && (existing == content || IsEquivalent(existing, id, content)))
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, content, Utf8NoBom, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return IoError($"Could not write '{Display(id)}': {ex.Message}");
        }
    }

    public Result<Unit> Move(string root, string from, string to)
    {
        try
        {
            File.Move(PathOf(root, from), PathOf(root, to), overwrite: false);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return IoError($"Could not rename '{Display(from)}' to '{Display(to)}': {ex.Message}");
        }
    }

    public Result<Unit> Delete(string root, string id)
    {
        try
        {
            File.Delete(PathOf(root, id));
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return IoError($"Could not delete '{Display(id)}': {ex.Message}");
        }
    }

    private static Result<EnvironmentDefinition> Parse(string text, string id)
    {
        try
        {
            return EnvironmentYamlSerializer.Deserialize(text, WorkspaceLayout.FileStem(id));
        }
        catch (WorkspaceFileFormatException ex)
        {
            return new Error(ErrorCodes.InvalidFile, $"'{Display(id)}' is not a valid environment file. {ex.Message}");
        }
    }

    private static bool IsEquivalent(string existing, string id, string content)
    {
        var parsed = Parse(existing, id);
        return parsed.IsSuccess && EnvironmentYamlSerializer.Serialize(parsed.Value) == content;
    }

    private static string PathOf(string root, string id) =>
        EnvironmentMapper.IsValidId(id)
            ? FullPath(root, Folder.Combine(id))
            : throw new UnauthorizedAccessException($"'{id}' is not a valid environment file name.");

    private static string Display(string id) => $"{WorkspaceLayout.EnvironmentsFolder}/{id}";

    private static Error IoError(string message) => new(ErrorCodes.Io, message);
}
