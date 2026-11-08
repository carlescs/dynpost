using Missive.Application.Common;

namespace Missive.Application.OAuth2;

/// <summary>An OAuth 2.0 access token kept on this computer, with what it was obtained for.</summary>
/// <param name="TokenUrl">The token endpoint it came from; kept so the cache can be read by people.</param>
/// <param name="ClientId">The client it was issued to; kept so the cache can be read by people.</param>
/// <param name="Scope">The scope granted (as the server reported it, or as asked for).</param>
/// <param name="RefreshToken">A refresh token to get the next one with, or <c>null</c>.</param>
/// <param name="Expires">When it expires; <c>null</c> when the server didn't say.</param>
/// <param name="Obtained">When it was obtained.</param>
public sealed record OAuth2Token(
    string TokenUrl,
    string ClientId,
    string Scope,
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? Expires,
    DateTimeOffset Obtained)
{
    /// <summary>
    /// A token is treated as expired a little before it is, so it doesn't run out on the way to
    /// the server.
    /// </summary>
    public static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(30);

    public bool IsExpired(DateTimeOffset now) => Expires is { } expires && expires - ExpiryMargin <= now;
}

/// <summary>
/// OAuth 2.0 tokens by environment id and then by <see cref="Domain.Http.OAuth2Settings.CacheKey"/>.
/// Tokens obtained with no environment selected are kept under the empty id. They are stored on
/// this computer only, in the workspace's git-ignored <c>.missive</c> folder.
/// </summary>
public interface IOAuth2TokenStore
{
    /// <summary>Loads every token; a missing store loads as empty.</summary>
    Task<Result<IReadOnlyDictionary<string, IReadOnlyDictionary<string, OAuth2Token>>>> LoadAsync(
        string root,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces every token. Environments without tokens are left out.</summary>
    Task<Result<Unit>> SaveAsync(
        string root,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, OAuth2Token>> tokens,
        CancellationToken cancellationToken = default);
}

/// <summary>What the authorization server sent back to the redirect URL.</summary>
/// <param name="RedirectUri">The redirect URL the browser was sent to, which the token request must repeat.</param>
/// <param name="Parameters">The query parameters of the redirect (<c>code</c>, <c>state</c>, or <c>error</c>…).</param>
public sealed record AuthorizationCallback(Uri RedirectUri, IReadOnlyDictionary<string, string> Parameters);

/// <summary>
/// Lets the user sign in in the system browser and receives the answer on a loopback redirect URL
/// (RFC 8252), for the authorization code grant.
/// </summary>
public interface IAuthorizationBrowser
{
    /// <summary>
    /// Listens on <paramref name="redirectUri"/> (or a free port on <c>127.0.0.1</c> when it is
    /// <c>null</c>), opens the URL <paramref name="authorizationUrl"/> builds for the actual
    /// redirect URL in the system browser, and waits for the redirect. Starting another
    /// authorization cancels one still waiting. Failures (the port is taken, no answer in time,
    /// cancelled) are returned as errors.
    /// </summary>
    Task<Result<AuthorizationCallback>> AuthorizeAsync(
        Uri? redirectUri,
        Func<Uri, Uri> authorizationUrl,
        CancellationToken cancellationToken = default);

    /// <summary>Stops waiting for the authorization in progress; returns whether there was one.</summary>
    bool CancelPending();
}
