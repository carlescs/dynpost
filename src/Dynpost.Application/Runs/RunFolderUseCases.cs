using Dynpost.Application.Common;
using Dynpost.Application.Folders;
using Dynpost.Application.Sharing;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Application.Runs;

/// <summary>A run plan's new path after a move.</summary>
public sealed record MovedRunPlanDto(string Path);

/// <summary>The run plans inside a folder of <c>plans/</c>, at any depth.</summary>
internal static class RunFolders
{
    public sealed record Scan(WorkspacePath Folder, IReadOnlyList<WorkspacePath> Plans);

    public static Result<Scan> Read(IWorkspaceRepository repository, string root, string? path)
    {
        if (!FolderPaths.TryParseFolder(repository, root, path, allowRoot: false, out var folder, out var error, WorkspaceLayout.PlansRoot))
        {
            return error;
        }

        var contents = repository.ReadFolderContents(root, folder);
        if (!contents.IsSuccess)
        {
            return contents.Error!;
        }

        var plans = contents.Value.Files
            .Where(WorkspaceLayout.IsRunPlanPath)
            .OrderBy(f => f.Value, StringComparer.Ordinal)
            .ToList();
        return new Scan(folder, plans);
    }

    /// <summary>Refuses folders with a plan that is being run.</summary>
    public static Error? Check(Scan scan, RunRegistry registry, string root, string action)
    {
        var running = scan.Plans.FirstOrDefault(p => registry.IsBusy(root, p.Value));
        return running is null
            ? null
            : new Error(ErrorCodes.InvalidRequest, $"Stop the run of '{running}' before this folder is {action}.");
    }

    /// <summary>Points the saved runs of the plans that were in <paramref name="from"/> at their paths under <paramref name="to"/>.</summary>
    public static async Task FollowAsync(
        IRunStore runs,
        string root,
        IEnumerable<WorkspacePath> plans,
        WorkspacePath from,
        WorkspacePath to,
        CancellationToken cancellationToken)
    {
        // Run history is local and only a convenience: a failure here doesn't undo the move.
        foreach (var plan in plans)
        {
            await runs.MovePlanAsync(root, plan.Value, to.Value + plan.Value[from.Value.Length..], cancellationToken);
        }
    }
}

/// <summary>The folders shown in the Load runs view: every folder inside <c>plans/</c>, parents first.</summary>
public sealed class ListRunFolders(IWorkspaceRepository repository, CurrentWorkspace current)
{
    public Task<Result<IReadOnlyList<string>>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return Task.FromResult<Result<IReadOnlyList<string>>>(root.Error!);
        }

        if (!repository.DirectoryExists(root.Value, WorkspaceLayout.PlansRoot))
        {
            return Task.FromResult(Result<IReadOnlyList<string>>.Success([]));
        }

        var contents = repository.ReadFolderContents(root.Value, WorkspaceLayout.PlansRoot);
        if (!contents.IsSuccess)
        {
            return Task.FromResult<Result<IReadOnlyList<string>>>(contents.Error!);
        }

        IReadOnlyList<string> folders = contents.Value.Folders
            .Where(f => WorkspaceLayout.IsAreaFolder(WorkspaceLayout.PlansRoot, f))
            .Select(f => f.Value)
            .Order(StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(Result<IReadOnlyList<string>>.Success(folders));
    }
}

/// <summary>
/// Renames a folder of run plans; the plans move along with their saved runs. Folders that also
/// hold requests, or a plan being run, are refused.
/// </summary>
public sealed class RenameRunFolder(
    RenameFolder rename,
    IWorkspaceRepository repository,
    IRunStore runs,
    RunRegistry registry,
    CurrentWorkspace current)
{
    public async Task<Result<FolderDto>> ExecuteAsync(string path, string name, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        var scan = RunFolders.Read(repository, root.Value, path);
        if (!scan.IsSuccess)
        {
            return scan.Error!;
        }

        if (RunFolders.Check(scan.Value, registry, root.Value, "renamed") is { } refused)
        {
            return refused;
        }

        var renamed = rename.Execute(path, name, WorkspaceLayout.PlansRoot);
        if (renamed.IsSuccess && renamed.Value.Path != scan.Value.Folder.Value)
        {
            await RunFolders.FollowAsync(
                runs, root.Value, scan.Value.Plans, scan.Value.Folder, WorkspacePath.Parse(renamed.Value.Path), cancellationToken);
        }

        return renamed;
    }
}

/// <summary>
/// Moves a folder of run plans into another folder (<c>plans</c> itself for the top level); the plans
/// move along with their saved runs. A folder with a plan being run is refused.
/// </summary>
public sealed class MoveRunFolder(
    MoveFolder move,
    IWorkspaceRepository repository,
    IRunStore runs,
    RunRegistry registry,
    CurrentWorkspace current)
{
    public async Task<Result<FolderDto>> ExecuteAsync(string path, string parent, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        var scan = RunFolders.Read(repository, root.Value, path);
        if (!scan.IsSuccess)
        {
            return scan.Error!;
        }

        if (RunFolders.Check(scan.Value, registry, root.Value, "moved") is { } refused)
        {
            return refused;
        }

        var moved = move.Execute(path, parent, WorkspaceLayout.PlansRoot);
        if (moved.IsSuccess && moved.Value.Path != scan.Value.Folder.Value)
        {
            await RunFolders.FollowAsync(
                runs, root.Value, scan.Value.Plans, scan.Value.Folder, WorkspacePath.Parse(moved.Value.Path), cancellationToken);
        }

        return moved;
    }
}

/// <summary>
/// Copies a folder of run plans next to it as <c>… copy</c>. The copies start without runs.
/// </summary>
public sealed class DuplicateRunFolder(
    DuplicateFolder duplicate,
    IWorkspaceRepository repository,
    CurrentWorkspace current)
{
    public Result<FolderDto> Execute(string path)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        var scan = RunFolders.Read(repository, root.Value, path);
        if (!scan.IsSuccess)
        {
            return scan.Error!;
        }

        // Copying a plan that is being run is harmless.
        return duplicate.Execute(path, WorkspaceLayout.PlansRoot);
    }
}

/// <summary>
/// Deletes a folder of run plans with the saved runs of its plans. Folders that hold files that
/// aren't Dynpost's, or a plan being run, are refused.
/// </summary>
public sealed class DeleteRunFolder(
    DeleteFolder delete,
    IWorkspaceRepository repository,
    IRunStore runs,
    RunRegistry registry,
    CurrentWorkspace current)
{
    public async Task<Result<Unit>> ExecuteAsync(string path, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        var scan = RunFolders.Read(repository, root.Value, path);
        if (!scan.IsSuccess)
        {
            return scan.Error!;
        }

        if (RunFolders.Check(scan.Value, registry, root.Value, "deleted") is { } refused)
        {
            return refused;
        }

        var deleted = delete.Execute(path, WorkspaceLayout.PlansRoot);
        if (deleted.IsSuccess)
        {
            // Run history is local and only a convenience, so a failure here doesn't undo the delete.
            foreach (var plan in scan.Value.Plans)
            {
                await runs.DeletePlanAsync(root.Value, plan.Value, cancellationToken);
            }
        }

        return deleted;
    }
}

/// <summary>
/// Moves a run plan file into another folder (<c>plans</c> itself for the top level), keeping its file
/// name; its saved runs and local-only setting follow it. A plan being run can't be moved.
/// </summary>
public sealed class MoveRunPlan(
    IRunPlanStore store,
    IWorkspaceRepository repository,
    IRunStore runs,
    RunRegistry registry,
    ILocalOnlyFiles localFiles,
    CurrentWorkspace current)
{
    public async Task<Result<MovedRunPlanDto>> ExecuteAsync(string path, string folder, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        var parsed = RunPlanPaths.Parse(path);
        if (!parsed.IsSuccess)
        {
            return parsed.Error!;
        }

        if (!FolderPaths.TryParseFolder(repository, root.Value, folder, allowRoot: true, out var target, out var error, WorkspaceLayout.PlansRoot))
        {
            return error;
        }

        if (!store.Exists(root.Value, parsed.Value))
        {
            return new Error(ErrorCodes.NotFound, $"The run plan '{path}' does not exist.");
        }

        if (target == parsed.Value.Parent)
        {
            return new MovedRunPlanDto(parsed.Value.Value);
        }

        if (registry.IsBusy(root.Value, parsed.Value.Value))
        {
            return new Error(ErrorCodes.InvalidRequest, "Stop the run of this plan before moving it.");
        }

        var destination = target.Combine(parsed.Value.FileName);
        if (store.Exists(root.Value, destination))
        {
            return new Error(ErrorCodes.AlreadyExists, $"'{destination}' already exists.");
        }

        var moved = store.Move(root.Value, parsed.Value, destination);
        if (!moved.IsSuccess)
        {
            return moved.Error!;
        }

        // A local-only plan must stay out of git in its new folder.
        var followed = localFiles.Move(root.Value, parsed.Value, destination);
        if (!followed.IsSuccess)
        {
            return followed.Error!;
        }

        await runs.MovePlanAsync(root.Value, parsed.Value.Value, destination.Value, cancellationToken);
        return new MovedRunPlanDto(destination.Value);
    }
}
