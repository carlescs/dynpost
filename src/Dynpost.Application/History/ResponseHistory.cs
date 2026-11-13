using System.Security.Cryptography;
using Dynpost.Application.Http;
using Dynpost.Application.Settings;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Application.History;

/// <summary>
/// Records the responses of saved requests in their history, when the user keeps one (see
/// <see cref="HistorySettings"/>). Used by <see cref="SendRequest"/>; a response is worth showing
/// even when it can't be recorded, so failures only mean it isn't.
/// </summary>
public sealed class ResponseHistory(IHistoryStore store, IAppSettingsStore settings)
{
    /// <summary>
    /// Keeps <paramref name="response"/> in the history of the request at <paramref name="path"/>
    /// and returns the id of the new entry; <c>null</c> when it wasn't kept.
    /// </summary>
    /// <param name="url">The URL as sent, with secrets hidden.</param>
    /// <param name="secrets">Values hidden wherever they appear in the response.</param>
    public async Task<string?> RecordAsync(
        string root,
        string? path,
        string method,
        string url,
        ResponseDto response,
        IReadOnlyCollection<string> secrets,
        DateTimeOffset sentAt)
    {
        if (path is null || !WorkspacePath.TryParse(path, out var request) || request.IsRoot
            || !WorkspaceLayout.IsRequestPath(request))
        {
            return null;
        }

        var options = ((await settings.LoadAsync()).History ?? HistorySettings.Default).Normalized();
        if (!options.Enabled)
        {
            return null;
        }

        var (kept, omitted) = HistoryRedaction.Prepare(response, secrets, options.MaxBodyBytes);
        var id = NewId(sentAt);
        var entry = new HistoryEntryDto(
            id,
            sentAt,
            method,
            url,
            response.Status,
            response.StatusText,
            response.DurationMs,
            response.Size,
            response.ContentType,
            omitted);
        var added = await store.AddAsync(
            root,
            request,
            new HistoryEntryDetailsDto(entry, kept),
            new HistoryLimits(options.MaxEntries, HistorySettings.MaxTotalBytes),
            CancellationToken.None);
        return added.IsSuccess ? id : null;
    }

    /// <summary>An id that sorts by time and is safe as a file name.</summary>
    private static string NewId(DateTimeOffset sentAt) =>
        $"{sentAt.UtcDateTime:yyyyMMdd'T'HHmmssfff}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}";

    /// <summary>Whether <paramref name="id"/> could be an entry id (and so is safe as a file name).</summary>
    public static bool IsValidId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or 'T');
}
