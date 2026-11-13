using Dynpost.Application.Common;
using Dynpost.Application.Runs;
using Dynpost.Domain.Workspaces;
using static Dynpost.Infrastructure.Workspaces.WorkspaceFiles;

namespace Dynpost.Infrastructure.Runs;

/// <summary>Run plans are the <c>*.run.yaml</c> files anywhere in the workspace's <c>plans/</c> folder.</summary>
public sealed class FileSystemRunPlanStore : IRunPlanStore
{
    public Result<IReadOnlyList<WorkspacePath>> List(string root)
    {
        var plans = new List<WorkspacePath>();
        try
        {
            if (Directory.Exists(FullPath(root, WorkspaceLayout.PlansRoot)))
            {
                Collect(root, WorkspaceLayout.PlansRoot, plans);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not list the run plans: {ex.Message}");
        }

        return plans;
    }

    public async Task<Result<string>> ReadAsync(string root, WorkspacePath path, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ReadIfExistsAsync(FullPath(root, path), cancellationToken)
                ?? (Result<string>)new Error(ErrorCodes.NotFound, $"The run plan '{path}' does not exist.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not read '{path}': {ex.Message}");
        }
    }

    public async Task<Result<bool>> WriteAsync(string root, WorkspacePath path, string text, CancellationToken cancellationToken = default)
    {
        try
        {
            return await WriteIfChangedAsync(FullPath(root, path), text, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not write '{path}': {ex.Message}");
        }
    }

    public bool Exists(string root, WorkspacePath path) => File.Exists(FullPath(root, path)) || Directory.Exists(FullPath(root, path));

    public Result<Unit> Move(string root, WorkspacePath from, WorkspacePath to)
    {
        try
        {
            File.Move(FullPath(root, from), FullPath(root, to), overwrite: false);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not rename '{from}' to '{to}': {ex.Message}");
        }
    }

    public Result<Unit> Delete(string root, WorkspacePath path)
    {
        try
        {
            File.Delete(FullPath(root, path));
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not delete '{path}': {ex.Message}");
        }
    }

    private static void Collect(string root, WorkspacePath folder, List<WorkspacePath> plans)
    {
        var directory = new DirectoryInfo(FullPath(root, folder));
        var folders = new List<WorkspacePath>();
        foreach (var info in directory.EnumerateFileSystemInfos().OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (info.LinkTarget is not null || !WorkspacePath.TryParse(info.Name, out _))
            {
                continue;
            }

            if (info is DirectoryInfo && WorkspaceLayout.IsCollectionFolder(folder, info.Name))
            {
                folders.Add(folder.Combine(info.Name));
            }
            else if (info is FileInfo && WorkspaceLayout.IsRunPlanFileName(info.Name))
            {
                plans.Add(folder.Combine(info.Name));
            }
        }

        foreach (var child in folders)
        {
            Collect(root, child, plans);
        }
    }
}
