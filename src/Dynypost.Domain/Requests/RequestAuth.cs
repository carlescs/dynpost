namespace Dynypost.Domain.Requests;

/// <summary>How a request authenticates. Values may contain <c>{{variable}}</c> references.</summary>
public closed record RequestAuth
{
    /// <summary>No auth, even when a folder above sets some.</summary>
    public static RequestAuth None { get; } = new NoAuth();

    /// <summary>The auth of the nearest folder above that sets one; no auth when none does.</summary>
    public static RequestAuth Inherit { get; } = new InheritAuth();
}

/// <summary>Sends no credentials, whatever the folders above set.</summary>
public sealed record NoAuth : RequestAuth;

/// <summary>Uses the auth of the nearest folder above that sets one (see <see cref="InheritedDefaults"/>).</summary>
public sealed record InheritAuth : RequestAuth;

public sealed record BasicAuth(string Username, string Password) : RequestAuth;

public sealed record BearerAuth(string Token) : RequestAuth;

public enum ApiKeyLocation
{
    Header,
    Query,
}

public sealed record ApiKeyAuth(string Key, string Value, ApiKeyLocation In = ApiKeyLocation.Header) : RequestAuth;

/// <summary>The OAuth 2.0 grant used to obtain an access token.</summary>
public enum OAuth2Grant
{
    /// <summary>The app authenticates as itself with its client id and secret.</summary>
    ClientCredentials,

    /// <summary>A user signs in in the system browser; always uses PKCE.</summary>
    AuthorizationCode,
}

/// <summary>How the client id and secret are sent to the token endpoint.</summary>
public enum OAuth2ClientAuthentication
{
    /// <summary>As a Basic <c>Authorization</c> header (<c>client_secret_basic</c>).</summary>
    Header,

    /// <summary>As <c>client_id</c> and <c>client_secret</c> form fields (<c>client_secret_post</c>).</summary>
    Body,
}

/// <summary>
/// Sends an OAuth 2.0 access token as a bearer token, obtaining it first from the token endpoint.
/// Every value may contain <c>{{variable}}</c> references; the client secret should be a secret
/// variable so it stays out of the request file.
/// </summary>
/// <param name="AuthUrl">The authorization endpoint (authorization code grant only).</param>
/// <param name="RedirectUri">
/// A loopback <c>http</c> URL to receive the authorization code at (authorization code grant
/// only); empty picks a free port on <c>127.0.0.1</c>.
/// </param>
/// <param name="Scope">Space-separated scopes; empty asks for the default ones.</param>
/// <param name="Audience">The <c>audience</c> parameter some providers (Auth0, …) need; empty leaves it out.</param>
public sealed record OAuth2Auth(
    OAuth2Grant Grant,
    string TokenUrl = "",
    string ClientId = "",
    string ClientSecret = "",
    string AuthUrl = "",
    string RedirectUri = "",
    string Scope = "",
    string Audience = "",
    OAuth2ClientAuthentication Credentials = OAuth2ClientAuthentication.Header) : RequestAuth;
