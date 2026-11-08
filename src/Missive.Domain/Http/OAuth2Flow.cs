using System.Net;
using System.Security.Cryptography;
using System.Text;
using Missive.Domain.Requests;

namespace Missive.Domain.Http;

/// <summary>An <see cref="OAuth2Auth"/> with its <c>{{variable}}</c> references resolved and checked.</summary>
/// <param name="AuthUrl">The authorization endpoint; <c>null</c> for the client credentials grant.</param>
/// <param name="RedirectUri">The fixed loopback redirect URL, or <c>null</c> to pick a free port.</param>
public sealed record OAuth2Settings(
    OAuth2Grant Grant,
    Uri TokenUrl,
    string ClientId,
    string ClientSecret,
    Uri? AuthUrl,
    Uri? RedirectUri,
    string Scope,
    string Audience,
    OAuth2ClientAuthentication Credentials)
{
    /// <summary>
    /// Identifies the tokens these settings obtain, so a cached token is only reused by requests
    /// that would get the same one: a digest of the grant, endpoints, client id, scope and audience
    /// (not the secret, which never decides who the token is for).
    /// </summary>
    public string CacheKey
    {
        get
        {
            var identity = string.Join('\n', Grant, TokenUrl.AbsoluteUri, AuthUrl?.AbsoluteUri ?? string.Empty, ClientId, Scope, Audience);
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
        }
    }
}

/// <summary>The outcome of <see cref="OAuth2Flow.Resolve"/>: settings, or why there are none.</summary>
/// <param name="MissingVariables">Variables the settings use that have no value, sorted.</param>
public sealed record OAuth2Resolution(OAuth2Settings? Settings, IReadOnlyList<string> Errors, IReadOnlyList<string> MissingVariables)
{
    public bool IsValid => Settings is not null;
}

/// <summary>
/// The OAuth 2.0 rules Missive follows (RFC 6749, with PKCE from RFC 7636 and loopback redirects
/// from RFC 8252): resolving the settings, and building the token and authorization requests.
/// </summary>
public static class OAuth2Flow
{
    /// <summary>The path of the redirect URL when Missive picks the port.</summary>
    public const string DefaultRedirectPath = "/callback";

    /// <summary>Resolves the settings' variables and checks they can be used to obtain a token.</summary>
    public static OAuth2Resolution Resolve(OAuth2Auth auth, IReadOnlyDictionary<string, string> variables)
    {
        var errors = new List<string>();
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        string Resolve(string text) => VariableTemplate.Resolve(text, variables, missing).Trim();

        var tokenUrl = Resolve(auth.TokenUrl);
        var clientId = Resolve(auth.ClientId);
        // The secret is used exactly as written: spaces in it are part of it.
        var clientSecret = VariableTemplate.Resolve(auth.ClientSecret, variables, missing);
        var authUrl = auth.Grant == OAuth2Grant.AuthorizationCode ? Resolve(auth.AuthUrl) : string.Empty;
        var redirect = auth.Grant == OAuth2Grant.AuthorizationCode ? Resolve(auth.RedirectUri) : string.Empty;
        var scope = string.Join(' ', Resolve(auth.Scope).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var audience = Resolve(auth.Audience);

        if (missing.Count > 0)
        {
            errors.Add($"The OAuth 2.0 settings use variables that aren't defined: {string.Join(", ", missing)}.");
        }

        // A URL with unresolved variables is already reported as such.
        var check = missing.Count == 0;
        var token = Endpoint(tokenUrl, "token URL", errors, check);
        var authorize = auth.Grant == OAuth2Grant.AuthorizationCode ? Endpoint(authUrl, "authorization URL", errors, check) : null;
        if (clientId.Length == 0)
        {
            errors.Add("Enter the OAuth 2.0 client id.");
        }

        Uri? redirectUri = null;
        if (redirect.Length > 0 && check)
        {
            if (!Uri.TryCreate(redirect, UriKind.Absolute, out redirectUri) || redirectUri.Scheme != Uri.UriSchemeHttp || !IsLoopback(redirectUri))
            {
                errors.Add($"The redirect URL '{redirect}' must be an http URL on this computer, such as http://127.0.0.1:8765/callback.");
                redirectUri = null;
            }
        }

        if (errors.Count > 0 || token is null || (auth.Grant == OAuth2Grant.AuthorizationCode && authorize is null))
        {
            return new OAuth2Resolution(null, errors, [.. missing]);
        }

        return new OAuth2Resolution(
            new OAuth2Settings(auth.Grant, token, clientId, clientSecret, authorize, redirectUri, scope, audience, auth.Credentials),
            [],
            []);
    }

    /// <summary>The token request of the client credentials grant.</summary>
    public static OutgoingRequest ClientCredentialsRequest(OAuth2Settings settings)
    {
        var fields = new List<KeyValuePair<string, string>> { new("grant_type", "client_credentials") };
        AddIfNotEmpty(fields, "scope", settings.Scope);
        AddIfNotEmpty(fields, "audience", settings.Audience);
        return TokenRequest(settings, fields);
    }

    /// <summary>The token request exchanging an authorization code, with its PKCE verifier.</summary>
    public static OutgoingRequest AuthorizationCodeRequest(OAuth2Settings settings, string code, Uri redirectUri, string codeVerifier) =>
        TokenRequest(settings,
        [
            new("grant_type", "authorization_code"),
            new("code", code),
            new("redirect_uri", redirectUri.AbsoluteUri),
            new("code_verifier", codeVerifier),
        ]);

    /// <summary>The token request trading a refresh token for a new access token.</summary>
    public static OutgoingRequest RefreshRequest(OAuth2Settings settings, string refreshToken)
    {
        var fields = new List<KeyValuePair<string, string>> { new("grant_type", "refresh_token"), new("refresh_token", refreshToken) };
        AddIfNotEmpty(fields, "scope", settings.Scope);
        return TokenRequest(settings, fields);
    }

    /// <summary>The URL the browser opens for the user to sign in and authorize the client.</summary>
    public static Uri AuthorizationUrl(OAuth2Settings settings, Uri redirectUri, string state, string codeChallenge)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            new("response_type", "code"),
            new("client_id", settings.ClientId),
            new("redirect_uri", redirectUri.AbsoluteUri),
        };
        AddIfNotEmpty(query, "scope", settings.Scope);
        AddIfNotEmpty(query, "audience", settings.Audience);
        query.Add(new("state", state));
        query.Add(new("code_challenge", codeChallenge));
        query.Add(new("code_challenge_method", "S256"));

        var builder = new UriBuilder(settings.AuthUrl!);
        var existing = builder.Query.TrimStart('?');
        var added = Encode(query);
        builder.Query = existing.Length == 0 ? added : $"{existing}&{added}";
        return builder.Uri;
    }

    /// <summary>The PKCE <c>S256</c> code challenge of a code verifier.</summary>
    public static string CodeChallenge(string codeVerifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    /// <summary>Encodes bytes as unpadded base64url, the form PKCE verifiers and challenges use.</summary>
    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Whether a URL points at this computer.</summary>
    public static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);

    private static OutgoingRequest TokenRequest(OAuth2Settings settings, List<KeyValuePair<string, string>> fields)
    {
        var headers = new List<KeyValuePair<string, string>> { new("Accept", "application/json") };
        if (settings.Credentials == OAuth2ClientAuthentication.Header && settings.ClientSecret.Length > 0)
        {
            // RFC 6749 §2.3.1: the id and secret are form-encoded before being joined.
            var credentials = $"{FormEncode(settings.ClientId)}:{FormEncode(settings.ClientSecret)}";
            headers.Add(new(RequestPreparation.AuthorizationHeader, $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials))}"));
        }
        else
        {
            fields.Add(new("client_id", settings.ClientId));
            AddIfNotEmpty(fields, "client_secret", settings.ClientSecret);
        }

        return new OutgoingRequest("POST", settings.TokenUrl, headers, new TextBody(Encode(fields), "application/x-www-form-urlencoded"));
    }

    private static Uri? Endpoint(string url, string what, List<string> errors, bool check)
    {
        if (url.Length == 0)
        {
            errors.Add($"Enter the OAuth 2.0 {what}.");
            return null;
        }

        if (!check)
        {
            return null;
        }

        if (!url.Contains("://", StringComparison.Ordinal))
        {
            url = "https://" + url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add($"The OAuth 2.0 {what} '{url}' is not a valid http or https URL.");
            return null;
        }

        return uri;
    }

    private static void AddIfNotEmpty(List<KeyValuePair<string, string>> fields, string name, string value)
    {
        if (value.Length > 0)
        {
            fields.Add(new(name, value));
        }
    }

    private static string Encode(IEnumerable<KeyValuePair<string, string>> fields) =>
        string.Join('&', fields.Select(f => $"{FormEncode(f.Key)}={FormEncode(f.Value)}"));

    private static string FormEncode(string value) => WebUtility.UrlEncode(value);
}
