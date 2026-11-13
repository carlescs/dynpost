using Dynypost.Application.Common;
using Dynypost.Application.Http;
using Dynypost.Domain.Workspaces;

namespace Dynypost.Application.History;

/// <summary>
/// The responses kept for each request file of a workspace, newest first. They are stored on this
/// computer only, in the workspace's git-ignored <c>.dynypost</c> folder, and keyed by the request's
/// workspace path, so the use cases that rename, move or delete requests keep them in step.
/// </summary>
public interface IHistoryStore
{
    /// <summary>The entries kept for a request, newest first; none when it has no history.</summary>
    Task<Result<IReadOnlyList<HistoryEntryDto>>> ListAsync(
        string root,
        WorkspacePath request,
        CancellationToken cancellationToken = default);

    /// <summary>One entry of a request's history, or <c>null</c> when it isn't kept (any more).</summary>
    Task<Result<HistoryEntryDetailsDto?>> LoadAsync(
        string root,
        WorkspacePath request,
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an entry to a request's history, then drops its oldest entries beyond
    /// <see cref="HistoryLimits.MaxEntries"/> and the oldest entries of the whole workspace while
    /// they take more than <see cref="HistoryLimits.MaxTotalBytes"/> (never the one added).
    /// </summary>
    Task<Result<Unit>> AddAsync(
        string root,
        WorkspacePath request,
        HistoryEntryDetailsDto entry,
        HistoryLimits limits,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes one entry of a request's history, or all of them when <paramref name="id"/> is <c>null</c>.</summary>
    Task<Result<Unit>> DeleteAsync(
        string root,
        WorkspacePath request,
        string? id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Follows a rename or move: the history of the request at <paramref name="from"/>, or of the
    /// requests inside it when it is a folder, moves to the matching path under <paramref name="to"/>.
    /// </summary>
    Result<Unit> Move(string root, WorkspacePath from, WorkspacePath to);

    /// <summary>Deletes the history of the request at <paramref name="path"/>, or of every request inside it.</summary>
    Result<Unit> Remove(string root, WorkspacePath path);
}

/// <summary>How much history is kept.</summary>
/// <param name="MaxEntries">The entries kept per request.</param>
/// <param name="MaxTotalBytes">The size of all the entries of a workspace together.</param>
public sealed record HistoryLimits(int MaxEntries, long MaxTotalBytes);

/// <summary>A response kept in a request's history, as listed.</summary>
/// <param name="Id">Identifies the entry within the workspace.</param>
/// <param name="SentAt">When the request was sent.</param>
/// <param name="Url">The URL it was sent to, with secrets hidden.</param>
/// <param name="Size">The size of the body as received, in bytes.</param>
/// <param name="BodyOmitted">Whether the body was too large to keep (binary bodies) — text bodies are cut off instead.</param>
public sealed record HistoryEntryDto(
    string Id,
    DateTimeOffset SentAt,
    string Method,
    string Url,
    int Status,
    string StatusText,
    long DurationMs,
    long Size,
    string? ContentType,
    bool BodyOmitted = false);

/// <summary>An entry of a request's history with the response that was kept.</summary>
public sealed record HistoryEntryDetailsDto(HistoryEntryDto Entry, ResponseDto Response);
