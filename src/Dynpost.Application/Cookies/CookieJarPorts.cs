using Dynpost.Application.Common;
using Dynpost.Domain.Http;

namespace Dynpost.Application.Cookies;

/// <summary>
/// The cookie jars of a workspace's environments, by environment id. They are stored on this
/// computer only, in the workspace's git-ignored <c>.dynpost</c> folder.
/// </summary>
public interface ICookieJarStore
{
    /// <summary>Loads every jar; a missing store loads as empty.</summary>
    Task<Result<IReadOnlyDictionary<string, IReadOnlyList<StoredCookie>>>> LoadAsync(
        string root,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces every jar. Empty jars are left out.</summary>
    Task<Result<Unit>> SaveAsync(
        string root,
        IReadOnlyDictionary<string, IReadOnlyList<StoredCookie>> jars,
        CancellationToken cancellationToken = default);
}

/// <summary>A cookie as shown in the UI.</summary>
/// <param name="Expires">When it expires; <c>null</c> for a session cookie.</param>
public sealed record CookieDto(
    string Name,
    string Value,
    string Domain,
    string Path,
    DateTimeOffset? Expires,
    bool Secure,
    bool HttpOnly)
{
    public static CookieDto From(StoredCookie cookie) =>
        new(cookie.Name, cookie.Value, cookie.Domain, cookie.Path, cookie.Expires, cookie.Secure, cookie.HttpOnly);
}
