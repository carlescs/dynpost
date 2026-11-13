using Dynpost.Application.Common;
using Dynpost.Application.Environments;
using Dynpost.Application.Requests;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Http;
using Dynpost.Domain.Requests;

namespace Dynpost.Application.OAuth2;

/// <summary>
/// What the UI shows about a cached OAuth 2.0 token. The token itself is not sent to the UI: it
/// is only ever used by the backend.
/// </summary>
/// <param name="Expires">When it expires; <c>null</c> when the server didn't say.</param>
/// <param name="Expired">Whether it has expired (or is about to).</param>
/// <param name="Refreshable">Whether a refresh token came with it, so it can be renewed without signing in.</param>
/// <param name="Scope">The scope granted.</param>
public sealed record OAuth2TokenDto(DateTimeOffset Obtained, DateTimeOffset? Expires, bool Expired, bool Refreshable, string Scope)
{
    public static OAuth2TokenDto From(OAuth2Token token, DateTimeOffset now) =>
        new(token.Obtained, token.Expires, token.IsExpired(now), !string.IsNullOrEmpty(token.RefreshToken), token.Scope);
}

/// <summary>Resolves the OAuth 2.0 settings of the auth in the editor with the selected environment.</summary>
internal static class OAuth2Context
{
    public sealed record Resolved(OAuth2Settings Settings, string? Root, string? EnvironmentId);

    public static async Task<Result<Resolved>> ResolveAsync(
        AuthDto auth,
        ActiveEnvironment environment,
        CurrentWorkspace current,
        CancellationToken cancellationToken)
    {
        var domain = RequestMapper.ToDomain(auth);
        if (!domain.IsSuccess)
        {
            return domain.Error!;
        }

        if (domain.Value is not OAuth2Auth oauth)
        {
            return new Error(ErrorCodes.InvalidRequest, "The request doesn't use OAuth 2.0 auth.");
        }

        var root = current.Root;
        var variables = await environment.LoadAsync(cancellationToken);
        if (!variables.IsSuccess)
        {
            return variables.Error!;
        }

        var resolution = OAuth2Flow.Resolve(oauth, variables.Value.Values);
        return resolution.IsValid
            ? new Resolved(resolution.Settings!, root, variables.Value.EnvironmentId)
            : new Error(ErrorCodes.InvalidRequest, variables.Value.Explain(resolution.Errors, resolution.MissingVariables));
    }
}

/// <summary>
/// Obtains a new OAuth 2.0 token for the auth in the editor ('Get new token') and caches it for
/// the selected environment, whatever was cached. The authorization code grant opens the system
/// browser for the user to sign in.
/// </summary>
public sealed class GetOAuth2Token(OAuth2Tokens tokens, ActiveEnvironment environment, CurrentWorkspace current, TimeProvider time)
{
    public async Task<Result<OAuth2TokenDto>> ExecuteAsync(AuthDto auth, CancellationToken cancellationToken = default)
    {
        var context = await OAuth2Context.ResolveAsync(auth, environment, current, cancellationToken);
        if (!context.IsSuccess)
        {
            return context.Error!;
        }

        var (settings, root, environmentId) = context.Value;
        var token = await tokens.RequestNewAsync(settings, root, environmentId, cancellationToken);
        return token.Map(t => OAuth2TokenDto.From(t, time.GetUtcNow()));
    }
}

/// <summary>
/// The cached OAuth 2.0 token for the auth in the editor and the selected environment, or
/// <c>null</c> when there is none, or when the settings are incomplete (the editor shows the
/// problem when sending).
/// </summary>
public sealed class GetOAuth2TokenStatus(OAuth2Tokens tokens, ActiveEnvironment environment, CurrentWorkspace current, TimeProvider time)
{
    public async Task<Result<OAuth2TokenDto?>> ExecuteAsync(AuthDto auth, CancellationToken cancellationToken = default)
    {
        var context = await OAuth2Context.ResolveAsync(auth, environment, current, cancellationToken);
        if (!context.IsSuccess)
        {
            return context.Error!.Code == ErrorCodes.InvalidRequest ? Result<OAuth2TokenDto?>.Success(null) : context.Error;
        }

        var (settings, root, environmentId) = context.Value;
        var token = await tokens.PeekAsync(settings, root, environmentId, cancellationToken);
        return token.Map(t => t is null ? null : OAuth2TokenDto.From(t, time.GetUtcNow()));
    }
}

/// <summary>Forgets the cached OAuth 2.0 token for the auth in the editor and the selected environment.</summary>
public sealed class ClearOAuth2Token(OAuth2Tokens tokens, ActiveEnvironment environment, CurrentWorkspace current)
{
    public async Task<Result<Unit>> ExecuteAsync(AuthDto auth, CancellationToken cancellationToken = default)
    {
        var context = await OAuth2Context.ResolveAsync(auth, environment, current, cancellationToken);
        if (!context.IsSuccess)
        {
            return context.Error!;
        }

        var (settings, root, environmentId) = context.Value;
        return await tokens.ForgetAsync(settings, root, environmentId, cancellationToken);
    }
}

/// <summary>Stops waiting for the user to sign in in the browser; returns whether a sign-in was in progress.</summary>
public sealed class CancelOAuth2SignIn(IAuthorizationBrowser browser)
{
    public bool Execute() => browser.CancelPending();
}
