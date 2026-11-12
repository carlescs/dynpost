using Missive.Application.Common;
using Missive.Application.History;
using Missive.Application.Runs;
using Missive.Application.Sharing;
using Missive.Domain.Workspaces;

namespace Missive.Application.Workspaces;

/// <summary>
/// The Missive files found in a workspace outside the places the current layout keeps them, as
/// workspace paths. Hidden folders and the root <c>environments/</c> folder are not searched.
/// </summary>
/// <param name="Requests">Request files and <c>_folder.yaml</c> files not inside <c>requests/</c>.</param>
/// <param name="RunPlans">Run plan files (<c>*.run.yaml</c>) not inside <c>plans/</c>.</param>
/// <param name="EmptyFolders">
/// Folders outside <c>requests/</c> and <c>plans/</c> holding no files at all (or only OS clutter
/// and <c>.gitkeep</c>), such as collections that have no request yet.
/// </param>
public sealed record LegacyLayoutScan(
    IReadOnlyList<WorkspacePath> Requests,
    IReadOnlyList<WorkspacePath> RunPlans,
    IReadOnlyList<WorkspacePath> EmptyFolders)
{
    /// <summary>
    /// Whether there are requests or run plans to move. Empty folders alone don't make a
    /// workspace an old one: any folder (such as a code repository) can have some.
    /// </summary>
    public bool HasFilesToMove => Requests.Count > 0 || RunPlans.Count > 0;
}

/// <summary>Reads and records which layout a workspace folder uses, and finds files left in an older layout.</summary>
public interface IWorkspaceLayoutStore
{
    /// <summary>The layout version recorded in <c>missive.yaml</c>; <c>1</c> when none is.</summary>
    Task<Result<int>> ReadVersionAsync(string root, CancellationToken cancellationToken = default);

    /// <summary>Records the layout version in <c>missive.yaml</c>, keeping anything else in the file.</summary>
    Task<Result<Unit>> WriteVersionAsync(string root, int version, CancellationToken cancellationToken = default);

    /// <summary>Finds the files and folders of the old layout (requests and run plans anywhere in the workspace).</summary>
    Task<Result<LegacyLayoutScan>> ScanLegacyAsync(string root, CancellationToken cancellationToken = default);
}

/// <summary>What <see cref="MigrateWorkspaceLayout"/> did, to tell the user.</summary>
/// <param name="Requests">How many request files (and folder defaults) were moved into <c>requests/</c>.</param>
/// <param name="Plans">How many run plans were moved into <c>plans/</c>.</param>
/// <param name="LeftInPlace">Files that couldn't be moved because their new place was taken, with why.</param>
/// <param name="Error">Why the migration stopped early, or <c>null</c>.</param>
public sealed record LayoutMigrationDto(int Requests, int Plans, IReadOnlyList<string> LeftInPlace, string? Error = null);

/// <summary>
/// Moves a workspace from the first layout — requests, collection folders and run plans all over
/// the workspace root — to the current one, where requests and their folders live in
/// <c>requests/</c> and run plans in <c>plans/</c>, keeping their relative paths
/// (<c>users/get-user.yaml</c> → <c>requests/users/get-user.yaml</c>, <c>smoke.run.yaml</c> →
/// <c>plans/smoke.run.yaml</c>). Other files (bodies, data files, notes) stay where they are, so
/// the paths that refer to them keep working. Response history, saved runs and local-only
/// settings follow the moved files, and run plans are rewritten to refer to the requests' new
/// paths. Nothing is ever overwritten: a file whose new place is taken is left where it is. The
/// layout is then recorded in <c>missive.yaml</c> so this runs once per workspace. Nothing is
/// committed: the moves show up as changes in git.
/// </summary>
public sealed class MigrateWorkspaceLayout(
    IWorkspaceLayoutStore layout,
    IWorkspaceRepository repository,
    IRunPlanStore plans,
    IRunPlanFormat planFormat,
    IHistoryStore history,
    IRunStore runs,
    ILocalOnlyFiles localFiles)
{
    private const string GitKeep = ".gitkeep";

    /// <summary>
    /// Migrates the workspace at <paramref name="root"/> if it isn't in the current layout yet.
    /// Returns what was done, or <c>null</c> when there was nothing to move.
    /// </summary>
    public async Task<LayoutMigrationDto?> ExecuteAsync(string root, CancellationToken cancellationToken = default)
    {
        var version = await layout.ReadVersionAsync(root, cancellationToken);
        if (version.IsSuccess && version.Value >= WorkspaceLayout.CurrentLayoutVersion)
        {
            return null;
        }

        var scan = await layout.ScanLegacyAsync(root, cancellationToken);
        if (!scan.IsSuccess)
        {
            return new LayoutMigrationDto(0, 0, [], scan.Error!.Message);
        }

        if (!scan.Value.HasFilesToMove)
        {
            // Already in the current layout (or empty): record it so later stray files are left alone.
            await layout.WriteVersionAsync(root, WorkspaceLayout.CurrentLayoutVersion, cancellationToken);
            return null;
        }

        return await MigrateAsync(root, scan.Value, cancellationToken);
    }

    private async Task<LayoutMigrationDto> MigrateAsync(string root, LegacyLayoutScan scan, CancellationToken cancellationToken)
    {
        var leftInPlace = new List<string>();
        var movedRequests = new Dictionary<string, string>(StringComparer.Ordinal);
        var movedPlans = new List<(WorkspacePath From, WorkspacePath To)>();
        var sources = new List<WorkspacePath>();

        foreach (var file in scan.Requests)
        {
            var target = RequestTarget(file);
            if (Move(root, file, target, leftInPlace))
            {
                sources.Add(file);
                if (WorkspaceLayout.IsRequestPath(target))
                {
                    movedRequests[file.Value] = target.Value;
                    history.Move(root, file, target);
                }

                localFiles.Move(root, file, target);
            }
        }

        foreach (var file in scan.RunPlans)
        {
            var target = PlanTarget(file);
            if (Move(root, file, target, leftInPlace))
            {
                sources.Add(file);
                movedPlans.Add((file, target));
                localFiles.Move(root, file, target);
                await runs.MovePlanAsync(root, file.Value, target.Value, cancellationToken);
            }
        }

        var error = await RewritePlansAsync(root, movedRequests, cancellationToken);
        CleanUpFolders(root, scan, sources, movedRequests.Keys.Select(WorkspacePath.Parse).ToList(), movedPlans.Select(p => p.From).ToList());

        // Without the record the next start would look for old files again; that is harmless, so
        // a failure to write it isn't reported.
        await layout.WriteVersionAsync(root, WorkspaceLayout.CurrentLayoutVersion, cancellationToken);
        return new LayoutMigrationDto(sources.Count - movedPlans.Count, movedPlans.Count, leftInPlace, error);
    }

    /// <summary>A request file goes to the same path inside <c>requests/</c>.</summary>
    internal static WorkspacePath RequestTarget(WorkspacePath file) =>
        WorkspacePath.Parse($"{WorkspaceLayout.RequestsFolder}/{file.Value}");

    /// <summary>
    /// A run plan goes to the same path inside <c>plans/</c>; one already inside <c>requests/</c>
    /// (a collection of that name) goes to the matching place in <c>plans/</c>.
    /// </summary>
    internal static WorkspacePath PlanTarget(WorkspacePath file)
    {
        var relative = WorkspaceLayout.IsAreaFolder(WorkspaceLayout.RequestsRoot, file.Parent)
            ? file.Value[(WorkspaceLayout.RequestsFolder.Length + 1)..]
            : file.Value;
        return WorkspacePath.Parse($"{WorkspaceLayout.PlansFolder}/{relative}");
    }

    private bool Move(string root, WorkspacePath from, WorkspacePath to, List<string> leftInPlace)
    {
        if (repository.EntryExists(root, to))
        {
            leftInPlace.Add($"{from} ('{to}' already exists)");
            return false;
        }

        var parent = repository.CreateFolder(root, to.Parent);
        var moved = parent.IsSuccess ? repository.MoveFile(root, from, to) : parent;
        if (!moved.IsSuccess)
        {
            leftInPlace.Add($"{from} ({moved.Error!.Message})");
            return false;
        }

        return true;
    }

    /// <summary>Points the request paths in every run plan at the requests' new places.</summary>
    private async Task<string?> RewritePlansAsync(string root, IReadOnlyDictionary<string, string> movedRequests, CancellationToken cancellationToken)
    {
        if (movedRequests.Count == 0)
        {
            return null;
        }

        var listed = plans.List(root);
        if (!listed.IsSuccess)
        {
            return listed.Error!.Message;
        }

        string? error = null;
        foreach (var plan in listed.Value)
        {
            var text = await plans.ReadAsync(root, plan, cancellationToken);
            if (!text.IsSuccess)
            {
                error ??= text.Error!.Message;
                continue;
            }

            var rewritten = planFormat.RewriteRequestPaths(text.Value, movedRequests);
            if (rewritten != text.Value)
            {
                var written = await plans.WriteAsync(root, plan, rewritten, cancellationToken);
                if (!written.IsSuccess)
                {
                    error ??= written.Error!.Message;
                }
            }
        }

        return error;
    }

    /// <summary>
    /// Removes the old folders that only held what was moved (and OS clutter), and recreates the
    /// empty collection folders in their new place, with their <c>.gitkeep</c>. Folders that still
    /// hold other files are kept. Best effort: a folder left behind does no harm.
    /// </summary>
    private void CleanUpFolders(
        string root,
        LegacyLayoutScan scan,
        IReadOnlyList<WorkspacePath> sources,
        IReadOnlyList<WorkspacePath> requestSources,
        IReadOnlyList<WorkspacePath> planSources)
    {
        var candidates = new HashSet<WorkspacePath>(scan.EmptyFolders);
        foreach (var source in sources)
        {
            for (var folder = source.Parent; !folder.IsRoot; folder = folder.Parent)
            {
                candidates.Add(folder);
            }
        }

        candidates.Remove(WorkspaceLayout.RequestsRoot);
        candidates.Remove(WorkspaceLayout.PlansRoot);

        // Children first, so a parent is only looked at once its emptied children are gone.
        foreach (var folder in candidates.OrderByDescending(f => f.Segments.Count).ThenBy(f => f.Value, StringComparer.Ordinal))
        {
            if (!repository.DirectoryExists(root, folder) || !HoldsOnlyClutter(root, folder))
            {
                continue;
            }

            var inArea = WorkspaceLayout.IsAreaFolder(WorkspaceLayout.RequestsRoot, folder)
                || WorkspaceLayout.IsAreaFolder(WorkspaceLayout.PlansRoot, folder);
            if (!inArea)
            {
                var target = FolderTarget(folder, requestSources, planSources);
                if (!repository.CreateFolder(root, target).IsSuccess)
                {
                    continue;
                }

                var gitKeep = folder.Combine(GitKeep);
                var targetKeep = target.Combine(GitKeep);
                if (repository.FileExists(root, gitKeep) && !repository.EntryExists(root, targetKeep))
                {
                    repository.MoveFile(root, gitKeep, targetKeep);
                }
            }

            repository.DeleteFolder(root, folder);
        }
    }

    /// <summary>
    /// Where an old folder belongs: with the plans when the nearest folder (itself or a parent)
    /// that held anything held run plans only, else with the requests.
    /// </summary>
    private static WorkspacePath FolderTarget(
        WorkspacePath folder,
        IReadOnlyList<WorkspacePath> requestSources,
        IReadOnlyList<WorkspacePath> planSources)
    {
        for (var current = folder; !current.IsRoot; current = current.Parent)
        {
            if (requestSources.Any(source => IsWithin(source, current)))
            {
                break;
            }

            if (planSources.Any(source => IsWithin(source, current)))
            {
                return WorkspacePath.Parse($"{WorkspaceLayout.PlansFolder}/{folder.Value}");
            }
        }

        return RequestTarget(folder);
    }

    private static bool IsWithin(WorkspacePath path, WorkspacePath folder) =>
        path.Value.StartsWith(folder.Value + "/", StringComparison.Ordinal);

    private bool HoldsOnlyClutter(string root, WorkspacePath folder)
    {
        var contents = repository.ReadFolderContents(root, folder);
        return contents.IsSuccess
            && contents.Value.Links.Count == 0
            && (contents.Value.Unsupported ?? []).Count == 0
            && contents.Value.Files.All(file => WorkspaceLayout.IsClutterFile(file.FileName));
    }
}
