using Dynypost.Application.Common;
using Dynypost.Application.Settings;

namespace Dynypost.Application.Workspaces;

/// <summary>A workspace folder opened recently.</summary>
/// <param name="Name">The folder's name, as shown for an open workspace.</param>
/// <param name="Path">The folder's full path.</param>
public sealed record RecentWorkspaceDto(string Name, string Path);

/// <summary>The list of recently opened workspace folders kept in <see cref="AppSettings"/>.</summary>
public static class RecentWorkspaces
{
    /// <summary>How many folders the list keeps.</summary>
    public const int MaxCount = 10;

    /// <summary>
    /// The list with <paramref name="folder"/> moved (or added) to the top, keeping at most
    /// <see cref="MaxCount"/> folders. Returns <paramref name="recent"/> itself when the folder is
    /// already on top, so settings that don't change aren't rewritten.
    /// </summary>
    public static IReadOnlyList<string> Add(IReadOnlyList<string>? recent, string folder)
    {
        recent ??= [];
        if (recent.Count > 0 && recent[0] == folder && recent.Count <= MaxCount)
        {
            return recent;
        }

        return [folder, .. recent.Where(f => f != folder).Take(MaxCount - 1)];
    }
}

/// <summary>
/// Lists the recently opened workspace folders, the most recent first. Folders that no longer
/// exist are removed from the list.
/// </summary>
public sealed class GetRecentWorkspaces(IAppSettingsStore settings, IWorkspaceRepository repository)
{
    public async Task<Result<IReadOnlyList<RecentWorkspaceDto>>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var recent = (await settings.LoadAsync(cancellationToken)).RecentWorkspaces ?? [];
        var existing = recent.Where(f => !string.IsNullOrWhiteSpace(f) && repository.FolderExists(f)).ToList();
        var missing = recent.Except(existing).ToHashSet();

        if (missing.Count > 0)
        {
            // Remove only what was found missing: a folder opened meanwhile must stay.
            // Best effort: a list that can't be saved is still worth showing.
            await settings.UpdateAsync(
                saved => saved with { RecentWorkspaces = [.. (saved.RecentWorkspaces ?? []).Where(f => !missing.Contains(f))] },
                cancellationToken);
        }

        return Result<IReadOnlyList<RecentWorkspaceDto>>.Success(existing
            .Select(folder => new RecentWorkspaceDto(WorkspaceTreeBuilder.WorkspaceName(folder), folder))
            .ToList());
    }
}

/// <summary>Removes a workspace folder from the recently opened list.</summary>
public sealed class RemoveRecentWorkspace(IAppSettingsStore settings)
{
    public async Task<Result<Unit>> ExecuteAsync(string folder, CancellationToken cancellationToken = default)
    {
        var result = await settings.UpdateAsync(
            saved => saved.RecentWorkspaces?.Contains(folder) == true
                ? saved with { RecentWorkspaces = [.. saved.RecentWorkspaces.Where(f => f != folder)] }
                : saved,
            cancellationToken);
        return result.IsSuccess ? Unit.Value : result.Error!;
    }
}
