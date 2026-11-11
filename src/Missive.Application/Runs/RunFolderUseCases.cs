using Missive.Application.Common;
using Missive.Application.Folders;
using Missive.Application.Sharing;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;

namespace Missive.Application.Runs;

/// <summary>A run plan's new path after a move.</summary>
public sealed record MovedRunPlanDto(string Path);

/// <summary>
/// What a folder holds, as far as the Load runs view cares: the run plans inside it (at any
/// depth) and whether it also holds requests. Folder actions of the Load runs view only act on
/// folders without requests, so organizing plans never moves, copies or deletes a request (and
/// never breaks the request paths that plans refer to).
/// </summary>
internal static class RunFolders
{
    public sealed record Scan(WorkspacePath Folder, IReadOnlyList<WorkspacePath> Plans, bool HoldsRequests);

    public static Result<Scan> Read(IWorkspaceRepository repository, string root, string? path, bool allowRoot = false)
    {
        if (!FolderPaths.TryParseFolder(repository, root, path, allowRoot, out var folder, out var error))
        {
            return error;
        }

        var contents = repository.ReadFolderContents(root, folder);
        if (!contents.IsSuccess)
        {
            return contents.Error!;
        }

        var plans = contents.Value.Files
            .Where(f => WorkspaceLayout.IsRunPlanFileName(f.FileName) && IsCollectionPath(f.Parent))
            .OrderBy(f => f.Value, StringComparer.Ordinal)
            .ToList();
        var holdsRequests = contents.Value.Files.Any(WorkspaceLayout.IsRequestPath);
        return new Scan(folder, plans, holdsRequests);
    }

    /// <summary>Refuses folders with requests in them, or with a plan that is being run.</summary>
    public static Error? Check(Scan scan, RunRegistry registry, string root, string action)
    {
        if (scan.HoldsRequests)
        {
            return RequestsError(scan, action);
        }

        var running = scan.Plans.FirstOrDefault(p => registry.IsBusy(root, p.Value));
        return running is null
            ? null
            : new Error(ErrorCodes.InvalidRequest, $"Stop the run of '{running}' before this folder is {action}.");
    }

    public static Error RequestsError(Scan scan, string action) => new(
        ErrorCodes.InvalidRequest,
        $"'{scan.Folder}' also holds requests, so it can't be {action} here; use the Explorer instead.");

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

    private static bool IsCollectionPath(WorkspacePath folder)
    {
        var parent = WorkspacePath.Root;
        foreach (var segment in folder.Segments)
        {
            if (!WorkspaceLayout.IsCollectionFolder(parent, segment))
            {
                return false;
            }

            parent = parent.Combine(segment);
        }

        return true;
    }
}

/// <summary>
/// The folders shown in the Load runs view: the collection folders that hold run plans (at any
/// depth), and those that hold no requests at all (such as a folder just made for plans). Folders
/// with requests but no plans are left to the Explorer. The list is in tree order, parents first.
/// </summary>
public sealed class ListRunFolders(IWorkspaceRepository repository, IRunPlanStore store, CurrentWorkspace current)
{
    public async Task<Result<IReadOnlyList<string>>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        var entries = await repository.ListAsync(root.Value, cancellationToken);
        if (!entries.IsSuccess)
        {
            return entries.Error!;
        }

        var plans = store.List(root.Value);
        if (!plans.IsSuccess)
        {
            return plans.Error!;
        }

        // Every folder that has a plan somewhere inside it.
        var withPlans = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plan in plans.Value)
        {
            for (var folder = plan.Parent; !folder.IsRoot; folder = folder.Parent)
            {
                withPlans.Add(folder.Value);
            }
        }

        var folders = new List<string>();
        Collect(entries.Value, withPlans, folders);
        return folders;
    }

    /// <summary>Adds the folders to show among <paramref name="entries"/>; returns whether they hold requests.</summary>
    private static bool Collect(IReadOnlyList<WorkspaceEntry> entries, HashSet<string> withPlans, List<string> folders)
    {
        var holdsRequests = false;
        foreach (var entry in entries)
        {
            if (entry is RequestFileEntry)
            {
                holdsRequests = true;
            }
            else if (entry is FolderEntry folder)
            {
                var index = folders.Count;
                var inner = Collect(folder.Children, withPlans, folders);
                holdsRequests |= inner;
                if (!inner || withPlans.Contains(folder.Path.Value))
                {
                    folders.Insert(index, folder.Path.Value);
                }
            }
        }

        return holdsRequests;
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

        var renamed = rename.Execute(path, name);
        if (renamed.IsSuccess && renamed.Value.Path != scan.Value.Folder.Value)
        {
            await RunFolders.FollowAsync(
                runs, root.Value, scan.Value.Plans, scan.Value.Folder, WorkspacePath.Parse(renamed.Value.Path), cancellationToken);
        }

        return renamed;
    }
}

/// <summary>
/// Moves a folder of run plans into another folder (<c>''</c> is the workspace root); the plans
/// move along with their saved runs. Folders that also hold requests, or a plan being run, are refused.
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

        var moved = move.Execute(path, parent);
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
/// Folders that also hold requests are refused, so duplicating plans never duplicates requests.
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

        // Copying a plan that is being run is harmless, so only requests are refused.
        if (scan.Value.HoldsRequests)
        {
            return RunFolders.RequestsError(scan.Value, "duplicated");
        }

        return duplicate.Execute(path);
    }
}

/// <summary>
/// Deletes a folder of run plans with the saved runs of its plans. Folders that also hold requests
/// (or files that aren't Missive's), or a plan being run, are refused.
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

        var deleted = delete.Execute(path);
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
/// Moves a run plan file into another folder (<c>''</c> is the workspace root), keeping its file
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

        if (!FolderPaths.TryParseFolder(repository, root.Value, folder, allowRoot: true, out var target, out var error))
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
