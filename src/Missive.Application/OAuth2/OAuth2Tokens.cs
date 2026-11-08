using System.Collections.Concurrent;
using System.Security.Cryptography;
using Missive.Application.Common;
using Missive.Application.Http;
using Missive.Domain.Http;
using Missive.Domain.Requests;

namespace Missive.Application.OAuth2;

/// <summary>
/// Obtains, caches and refreshes OAuth 2.0 access tokens. Tokens are cached per environment in
/// <see cref="IOAuth2TokenStore"/> (in memory when no workspace is open), so each environment
/// signs in on its own. Register it as a singleton: the in-memory cache lives in it.
/// </summary>
public sealed class OAuth2Tokens(IHttpSender sender, IOAuth2TokenStore store, IAuthorizationBrowser browser, TimeProvider time)
{
    private readonly ConcurrentDictionary<(string Environment, string Key), OAuth2Token> _memory = new();

    /// <summary>
    /// The token to send a request with: the cached one while it is valid, else one obtained with
    /// the refresh token, else (client credentials) a new one. The authorization code grant never
    /// opens the browser here: without a usable token it asks the user to sign in.
    /// </summary>
    /// <param name="root">The workspace the token is cached in; <c>null</c> keeps it in memory.</param>
    /// <param name="environmentId">The selected environment, or <c>null</c> for none.</param>
    public async Task<Result<OAuth2Token>> GetAsync(OAuth2Settings settings, string? root, string? environmentId, CancellationToken cancellationToken = default)
    {
        var cached = await PeekAsync(settings, root, environmentId, cancellationToken);
        if (!cached.IsSuccess)
        {
            return cached.Error!;
        }

        var now = time.GetUtcNow();
        if (cached.Value is { } token && !token.IsExpired(now))
        {
            return token;
        }

        if (cached.Value?.RefreshToken is { Length: > 0 } refreshToken)
        {
            var refreshed = await RequestAsync(settings, OAuth2Flow.RefreshRequest(settings, refreshToken), refreshToken, cancellationToken);
            if (refreshed.IsSuccess)
            {
                return await KeepAsync(settings, root, environmentId, refreshed.Value, cancellationToken);
            }

            if (refreshed.Error!.Code != OAuth2Errors.FailedCode)
            {
                return refreshed.Error;
            }

            // The server refused the refresh token (expired or revoked): it is no use any more.
            await ForgetAsync(settings, root, environmentId, cancellationToken);
        }

        return settings.Grant == OAuth2Grant.ClientCredentials
            ? await RequestNewAsync(settings, root, environmentId, cancellationToken)
            : OAuth2Errors.SignInRequired(expired: cached.Value is not null);
    }

    /// <summary>
    /// Obtains a new token whatever is cached, and caches it: with the client credentials grant
    /// straight from the token endpoint, with the authorization code grant after the user signs in
    /// in the system browser.
    /// </summary>
    public async Task<Result<OAuth2Token>> RequestNewAsync(OAuth2Settings settings, string? root, string? environmentId, CancellationToken cancellationToken = default)
    {
        var token = settings.Grant == OAuth2Grant.ClientCredentials
            ? await RequestAsync(settings, OAuth2Flow.ClientCredentialsRequest(settings), null, cancellationToken)
            : await SignInAsync(settings, cancellationToken);
        return token.IsSuccess ? await KeepAsync(settings, root, environmentId, token.Value, cancellationToken) : token.Error!;
    }

    /// <summary>The cached token for these settings, expired or not, or <c>null</c>.</summary>
    public async Task<Result<OAuth2Token?>> PeekAsync(OAuth2Settings settings, string? root, string? environmentId, CancellationToken cancellationToken = default)
    {
        var environment = environmentId ?? string.Empty;
        return root is null
            ? _memory.GetValueOrDefault((environment, settings.CacheKey))
            : await OAuth2TokenCache.GetAsync(store, root, environment, settings.CacheKey, cancellationToken);
    }

    /// <summary>Forgets the cached token for these settings.</summary>
    public async Task<Result<Unit>> ForgetAsync(OAuth2Settings settings, string? root, string? environmentId, CancellationToken cancellationToken = default)
    {
        var environment = environmentId ?? string.Empty;
        if (root is null)
        {
            _memory.TryRemove((environment, settings.CacheKey), out _);
            return Unit.Value;
        }

        return await OAuth2TokenCache.SetAsync(store, root, environment, settings.CacheKey, null, cancellationToken);
    }

    /// <summary>
    /// Caches a token. The token is worth using even if the cache can't be written, so a failure
    /// to save is not an error.
    /// </summary>
    private async Task<Result<OAuth2Token>> KeepAsync(OAuth2Settings settings, string? root, string? environmentId, OAuth2Token token, CancellationToken cancellationToken)
    {
        var environment = environmentId ?? string.Empty;
        if (root is null)
        {
            _memory[(environment, settings.CacheKey)] = token;
        }
        else
        {
            await OAuth2TokenCache.SetAsync(store, root, environment, settings.CacheKey, token, CancellationToken.None);
        }

        return token;
    }

    private async Task<Result<OAuth2Token>> SignInAsync(OAuth2Settings settings, CancellationToken cancellationToken)
    {
        var verifier = OAuth2Flow.Base64Url(RandomNumberGenerator.GetBytes(32));
        var state = OAuth2Flow.Base64Url(RandomNumberGenerator.GetBytes(16));
        var challenge = OAuth2Flow.CodeChallenge(verifier);

        var callback = await browser.AuthorizeAsync(
            settings.RedirectUri,
            redirect => OAuth2Flow.AuthorizationUrl(settings, redirect, state, challenge),
            cancellationToken);
        if (!callback.IsSuccess)
        {
            return callback.Error!;
        }

        var parameters = callback.Value.Parameters;
        if (parameters.GetValueOrDefault("error") is { Length: > 0 } error)
        {
            var description = parameters.GetValueOrDefault("error_description");
            return OAuth2Errors.Failed($"Sign-in failed: {error}{(string.IsNullOrEmpty(description) ? "." : $" — {description}")}");
        }

        if (parameters.GetValueOrDefault("state") != state)
        {
            // Not the answer to this sign-in (RFC 6749 §10.12): never exchange its code.
            return OAuth2Errors.Failed("Sign-in failed: the answer doesn't belong to this sign-in (its state doesn't match).");
        }

        if (parameters.GetValueOrDefault("code") is not { Length: > 0 } code)
        {
            return OAuth2Errors.Failed("Sign-in failed: the authorization server sent no authorization code.");
        }

        return await RequestAsync(settings, OAuth2Flow.AuthorizationCodeRequest(settings, code, callback.Value.RedirectUri, verifier), null, cancellationToken);
    }

    private async Task<Result<OAuth2Token>> RequestAsync(OAuth2Settings settings, OutgoingRequest request, string? refreshToken, CancellationToken cancellationToken)
    {
        var response = await sender.SendAsync(request, cancellationToken);
        if (!response.IsSuccess)
        {
            return new Error(response.Error!.Code, $"Could not get an OAuth 2.0 token. {response.Error.Message}");
        }

        return OAuth2TokenResponse.Parse(response.Value, settings, time.GetUtcNow(), refreshToken);
    }
}
