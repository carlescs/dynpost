using Dynpost.Application.Common;
using Dynpost.Application.Folders;
using Dynpost.Domain.Requests;
using Dynpost.Domain.Workspaces;
using Dynpost.Infrastructure.Yaml;
using static Dynpost.Infrastructure.Workspaces.WorkspaceFiles;

namespace Dynpost.Infrastructure.Workspaces;

/// <summary>Stores the defaults of a folder in its <c>_folder.yaml</c> file.</summary>
public sealed class YamlFolderDefaultsStore : IFolderDefaultsStore
{
    public async Task<Result<FolderDefaults>> ReadAsync(string root, WorkspacePath folder, CancellationToken cancellationToken = default)
    {
        var path = folder.Combine(WorkspaceLayout.FolderDefaultsFile);
        string? text;
        try
        {
            text = await ReadIfExistsAsync(FullPath(root, path), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not read '{path}': {ex.Message}");
        }

        return text is null ? FolderDefaults.Empty : Parse(text, path);
    }

    public async Task<Result<bool>> WriteAsync(string root, WorkspacePath folder, FolderDefaults defaults, CancellationToken cancellationToken = default)
    {
        var path = folder.Combine(WorkspaceLayout.FolderDefaultsFile);
        try
        {
            var fullPath = FullPath(root, path);
            if (defaults.IsEmpty)
            {
                if (!File.Exists(fullPath))
                {
                    return false;
                }

                File.Delete(fullPath);
                return true;
            }

            var content = RequestYamlSerializer.SerializeFolder(defaults);
            var existing = await ReadIfExistsAsync(fullPath, cancellationToken);
            if (existing is not null && Parse(existing, path) is { IsSuccess: true } parsed
                && RequestYamlSerializer.SerializeFolder(parsed.Value) == content)
            {
                // Already holds these defaults, even if formatted by hand: saving must not produce diffs that change nothing.
                return false;
            }

            return await WriteIfChangedAsync(fullPath, content, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not write '{path}': {ex.Message}");
        }
    }

    private static Result<FolderDefaults> Parse(string text, WorkspacePath path)
    {
        try
        {
            return RequestYamlSerializer.DeserializeFolder(text);
        }
        catch (RequestFormatException ex)
        {
            return new Error(ErrorCodes.InvalidFile, $"'{path}' is not a valid folder defaults file. {ex.Message}");
        }
    }
}
