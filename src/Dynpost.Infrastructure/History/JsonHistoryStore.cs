using System.Text.Json;
using System.Text.Json.Serialization;
using Dynpost.Application.Common;
using Dynpost.Application.History;
using Dynpost.Domain.Workspaces;
using Dynpost.Infrastructure.Environments;
using Dynpost.Infrastructure.Git;
using static Dynpost.Infrastructure.Workspaces.WorkspaceFiles;

namespace Dynpost.Infrastructure.History;

/// <summary>
/// Keeps the requests' response history in <c>.dynpost/history/</c>, which is git-ignored:
/// <c>index.json</c> lists the entries of each request file (by workspace path, newest first) and
/// each entry's response is in <c>&lt;id&gt;.json</c> next to it. Following a rename or move only
/// rewrites the index. The history is a convenience, so an unreadable index reads as empty.
/// Registered as a singleton: one lock keeps the index consistent.
/// </summary>
/// <example>
/// <code>
/// {
///   "users/get-user.yaml": [
///     { "id": "20261107T120000000-1a2b3c4d", "sentAt": "2026-11-07T12:00:00+00:00", "method": "GET",
///       "url": "https://api.example.com/users/1", "status": 200, "statusText": "OK",
///       "durationMs": 120, "size": 85, "contentType": "application/json", "bytes": 1460 }
///   ]
/// }
/// </code>
/// </example>
public sealed class JsonHistoryStore : IHistoryStore
{
    public const string FolderName = "history";
    public const string IndexFileName = "index.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NewLine = "\n",
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions CompactOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<Result<IReadOnlyList<HistoryEntryDto>>> ListAsync(
        string root,
        WorkspacePath request,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var index = ReadIndex(root);
            return index.TryGetValue(request.Value, out var entries)
                ? entries.Select(entry => entry.ToDto()).ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed("read", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Result<HistoryEntryDetailsDto?>> LoadAsync(
        string root,
        WorkspacePath request,
        string id,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var index = ReadIndex(root);
            if (!index.TryGetValue(request.Value, out var entries) || entries.Find(entry => entry.Id == id) is not { } entry)
            {
                return Result<HistoryEntryDetailsDto?>.Success(null);
            }

            var text = await ReadIfExistsAsync(EntryPath(root, id), cancellationToken);
            if (text is null)
            {
                return Result<HistoryEntryDetailsDto?>.Success(null);
            }

            var stored = JsonSerializer.Deserialize<HistoryEntryDetailsDto>(text, CompactOptions);
            return stored is null ? Result<HistoryEntryDetailsDto?>.Success(null) : stored with { Entry = entry.ToDto() };
        }
        catch (JsonException)
        {
            return Result<HistoryEntryDetailsDto?>.Success(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed("read", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Result<Unit>> AddAsync(
        string root,
        WorkspacePath request,
        HistoryEntryDetailsDto entry,
        HistoryLimits limits,
        CancellationToken cancellationToken = default)
    {
        if (!ResponseHistory.IsValidId(entry.Entry.Id))
        {
            return new Error(ErrorCodes.InvalidRequest, $"'{entry.Entry.Id}' is not a valid history entry id.");
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await GitIgnoreFile.EnsureIgnoredAsync(root, cancellationToken);
            var json = JsonSerializer.Serialize(entry, CompactOptions);
            var path = EntryPath(root, entry.Entry.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, json, Utf8NoBom, cancellationToken);

            var index = ReadIndex(root);
            var entries = index.TryGetValue(request.Value, out var existing) ? existing : [];
            entries.Insert(0, IndexEntry.From(entry.Entry, Utf8NoBom.GetByteCount(json)));
            index[request.Value] = entries;

            var dropped = new List<string>();
            if (entries.Count > limits.MaxEntries)
            {
                dropped.AddRange(entries.Skip(Math.Max(1, limits.MaxEntries)).Select(e => e.Id));
                entries.RemoveRange(Math.Max(1, limits.MaxEntries), entries.Count - Math.Max(1, limits.MaxEntries));
            }

            // The oldest entries of the whole workspace go first; the one just added always stays.
            var total = index.Values.SelectMany(list => list).Sum(e => e.Bytes);
            var oldest = index
                .SelectMany(pair => pair.Value.Select(e => (Request: pair.Key, Entry: e)))
                .Where(item => item.Entry.Id != entry.Entry.Id)
                .OrderBy(item => item.Entry.SentAt)
                .ThenBy(item => item.Entry.Id, StringComparer.Ordinal)
                .ToList();
            foreach (var (key, old) in oldest)
            {
                if (total <= limits.MaxTotalBytes)
                {
                    break;
                }

                index[key].Remove(old);
                total -= old.Bytes;
                dropped.Add(old.Id);
            }

            WriteIndex(root, index);
            DeleteEntries(root, dropped);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed("save", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Result<Unit>> DeleteAsync(
        string root,
        WorkspacePath request,
        string? id,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var index = ReadIndex(root);
            if (!index.TryGetValue(request.Value, out var entries))
            {
                return Unit.Value;
            }

            var dropped = entries.Where(entry => id is null || entry.Id == id).Select(entry => entry.Id).ToList();
            if (dropped.Count == 0)
            {
                return Unit.Value;
            }

            entries.RemoveAll(entry => dropped.Contains(entry.Id));
            WriteIndex(root, index);
            DeleteEntries(root, dropped);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed("save", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Result<Unit> Move(string root, WorkspacePath from, WorkspacePath to)
    {
        if (from == to)
        {
            return Unit.Value;
        }

        _lock.Wait();
        try
        {
            var index = ReadIndex(root);
            var moved = index.Keys.Where(key => IsAtOrWithin(key, from.Value)).ToList();
            if (moved.Count == 0)
            {
                return Unit.Value;
            }

            var entries = moved.ToDictionary(key => key, key => index[key], StringComparer.Ordinal);
            foreach (var key in moved)
            {
                index.Remove(key);
            }

            foreach (var (key, list) in entries)
            {
                var target = to.Value + key[from.Value.Length..];
                index[target] = index.TryGetValue(target, out var existing)
                    ? [.. list.Concat(existing).OrderByDescending(e => e.SentAt)]
                    : list;
            }

            WriteIndex(root, index);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed("save", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Result<Unit> Remove(string root, WorkspacePath path)
    {
        _lock.Wait();
        try
        {
            var index = ReadIndex(root);
            var removed = index.Keys.Where(key => IsAtOrWithin(key, path.Value)).ToList();
            if (removed.Count == 0)
            {
                return Unit.Value;
            }

            var dropped = removed.SelectMany(key => index[key]).Select(entry => entry.Id).ToList();
            foreach (var key in removed)
            {
                index.Remove(key);
            }

            WriteIndex(root, index);
            DeleteEntries(root, dropped);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed("save", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static bool IsAtOrWithin(string key, string path) =>
        path.Length == 0
        || key == path
        || (key.Length > path.Length && key.StartsWith(path, StringComparison.Ordinal) && key[path.Length] == '/');

    private static string FolderPath(string root) => LocalWorkspaceFolder.FilePath(root, FolderName);

    private static string IndexPath(string root) => Path.Combine(FolderPath(root), IndexFileName);

    private static string EntryPath(string root, string id) => Path.Combine(FolderPath(root), id + ".json");

    private static Dictionary<string, List<IndexEntry>> ReadIndex(string root)
    {
        string text;
        try
        {
            text = File.ReadAllText(IndexPath(root), Utf8NoBom);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(StringComparer.Ordinal);
        }

        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, List<IndexEntry?>?>>(text, JsonOptions) ?? [];
            var index = new Dictionary<string, List<IndexEntry>>(StringComparer.Ordinal);
            foreach (var (key, entries) in stored)
            {
                var valid = (entries ?? [])
                    .Where(entry => entry is not null && ResponseHistory.IsValidId(entry.Id))
                    .Select(entry => entry!)
                    .ToList();
                if (valid.Count > 0 && WorkspacePath.TryParse(key, out var path) && path.Value == key && !path.IsRoot)
                {
                    index[key] = valid;
                }
            }

            return index;
        }
        catch (JsonException)
        {
            // The history is only a convenience: start over rather than fail every send.
            return new(StringComparer.Ordinal);
        }
    }

    private static void WriteIndex(string root, Dictionary<string, List<IndexEntry>> index)
    {
        var path = IndexPath(root);
        var kept = new SortedDictionary<string, List<IndexEntry>>(StringComparer.Ordinal);
        foreach (var (key, entries) in index.Where(pair => pair.Value.Count > 0))
        {
            kept[key] = entries;
        }

        if (kept.Count == 0)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(kept, JsonOptions) + "\n", Utf8NoBom);
        File.Move(temporary, path, overwrite: true);
    }

    private static void DeleteEntries(string root, IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            try
            {
                File.Delete(EntryPath(root, id));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover entry file is harmless; the index no longer lists it.
            }
        }
    }

    private static Error Failed(string action, Exception ex) =>
        new(ErrorCodes.Io, $"Could not {action} the response history: {ex.Message}");

    /// <summary>An entry as listed in the index, with the size of its file.</summary>
    private sealed record IndexEntry(
        string Id,
        DateTimeOffset SentAt,
        string? Method,
        string? Url,
        int Status,
        string? StatusText,
        long DurationMs,
        long Size,
        string? ContentType,
        long Bytes,
        bool BodyOmitted = false)
    {
        public static IndexEntry From(HistoryEntryDto entry, long bytes) =>
            new(entry.Id, entry.SentAt, entry.Method, entry.Url, entry.Status, entry.StatusText, entry.DurationMs, entry.Size, entry.ContentType, bytes, entry.BodyOmitted);

        public HistoryEntryDto ToDto() =>
            new(Id, SentAt, Method ?? string.Empty, Url ?? string.Empty, Status, StatusText ?? string.Empty, DurationMs, Size, ContentType, BodyOmitted);
    }
}
