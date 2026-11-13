using Dynpost.Application.Common;
using Dynpost.Application.Settings;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Application.History;

/// <summary>Lists the responses kept in a request's history, newest first.</summary>
public sealed class ListResponseHistory(IHistoryStore store, CurrentWorkspace current)
{
    public async Task<Result<IReadOnlyList<HistoryEntryDto>>> ExecuteAsync(string path, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        return HistoryPaths.TryParseRequest(path, out var request, out var error)
            ? await store.ListAsync(root.Value, request, cancellationToken)
            : error;
    }
}

/// <summary>Reads one response kept in a request's history.</summary>
public sealed class GetResponseHistoryEntry(IHistoryStore store, CurrentWorkspace current)
{
    public async Task<Result<HistoryEntryDetailsDto>> ExecuteAsync(string path, string id, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!HistoryPaths.TryParseRequest(path, out var request, out var error))
        {
            return error;
        }

        if (!ResponseHistory.IsValidId(id))
        {
            return NotFound();
        }

        var loaded = await store.LoadAsync(root.Value, request, id, cancellationToken);
        if (!loaded.IsSuccess)
        {
            return loaded.Error!;
        }

        return loaded.Value is { } entry ? entry : NotFound();
    }

    private static Error NotFound() => new(ErrorCodes.NotFound, "That response is no longer kept in the history.");
}

/// <summary>Deletes one response from a request's history, or all of them; returns the entries left.</summary>
public sealed class DeleteResponseHistory(IHistoryStore store, CurrentWorkspace current)
{
    public async Task<Result<IReadOnlyList<HistoryEntryDto>>> ExecuteAsync(string path, string? id, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!HistoryPaths.TryParseRequest(path, out var request, out var error))
        {
            return error;
        }

        if (id is not null && !ResponseHistory.IsValidId(id))
        {
            return await store.ListAsync(root.Value, request, cancellationToken);
        }

        var deleted = await store.DeleteAsync(root.Value, request, id, cancellationToken);
        return deleted.IsSuccess ? await store.ListAsync(root.Value, request, cancellationToken) : deleted.Error!;
    }
}

/// <summary>Changes whether responses are kept in the history, and how many and how large.</summary>
public sealed class SetHistorySettings(IAppSettingsStore store)
{
    public const string InvalidSettingsCode = "settings.invalid_history";

    public async Task<Result<AppSettingsDto>> ExecuteAsync(HistorySettingsDto history, CancellationToken cancellationToken = default)
    {
        if (history.MaxEntries is < 1 or > HistorySettings.MaxEntriesLimit)
        {
            return new Error(InvalidSettingsCode, $"Keep between 1 and {HistorySettings.MaxEntriesLimit} responses per request.");
        }

        if (history.MaxBodyBytes is < 0 or > HistorySettings.MaxBodyBytesLimit)
        {
            return new Error(InvalidSettingsCode, $"The body size limit must be between 0 and {HistorySettings.MaxBodyBytesLimit / (1024 * 1024)} MB.");
        }

        var settings = new HistorySettings(history.Enabled, history.MaxEntries, history.MaxBodyBytes);
        var saved = await store.UpdateAsync(current => current with { History = settings }, cancellationToken);
        return saved.IsSuccess ? AppSettingsDto.From(saved.Value) : saved.Error!;
    }
}

internal static class HistoryPaths
{
    public static bool TryParseRequest(string path, out WorkspacePath request, out Error error)
    {
        error = null!;
        if (WorkspacePath.TryParse(path, out request) && !request.IsRoot
            && WorkspaceLayout.IsRequestPath(request))
        {
            return true;
        }

        error = new Error(ErrorCodes.InvalidPath, $"'{path}' is not a valid request path.");
        return false;
    }
}
