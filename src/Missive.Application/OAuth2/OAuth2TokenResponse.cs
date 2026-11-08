using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Missive.Application.Common;
using Missive.Application.Http;
using Missive.Domain.Http;

namespace Missive.Application.OAuth2;

/// <summary>Error codes and messages for obtaining OAuth 2.0 tokens.</summary>
public static class OAuth2Errors
{
    /// <summary>The token or authorization endpoint refused, or answered something unusable.</summary>
    public const string FailedCode = "oauth2.failed";

    /// <summary>The authorization code grant needs the user to sign in first ('Get new token').</summary>
    public const string SignInRequiredCode = "oauth2.sign_in_required";

    public static Error Failed(string message) => new(FailedCode, message);

    public static Error SignInRequired(bool expired) => new(
        SignInRequiredCode,
        expired
            ? "The OAuth 2.0 access token has expired and can't be refreshed: use 'Get new token' on the Auth tab to sign in again."
            : "There is no OAuth 2.0 access token yet: use 'Get new token' on the Auth tab to sign in.");
}

/// <summary>Reads the answer of a token endpoint (RFC 6749 §5).</summary>
public static class OAuth2TokenResponse
{
    /// <param name="settings">What the token was asked for.</param>
    /// <param name="previousRefreshToken">
    /// The refresh token used, kept when the server doesn't issue a new one (RFC 6749 §6).
    /// </param>
    public static Result<OAuth2Token> Parse(HttpResponseData response, OAuth2Settings settings, DateTimeOffset now, string? previousRefreshToken = null)
    {
        var text = Encoding.UTF8.GetString(response.Body);
        var fields = Fields(response, text);
        var success = response.StatusCode is >= 200 and < 300;
        var accessToken = fields.GetValueOrDefault("access_token");

        if (!success || string.IsNullOrEmpty(accessToken))
        {
            var error = fields.GetValueOrDefault("error");
            var description = fields.GetValueOrDefault("error_description");
            var status = $"{response.StatusCode}{(string.IsNullOrEmpty(response.ReasonPhrase) ? string.Empty : " " + response.ReasonPhrase)}";
            return OAuth2Errors.Failed(
                !string.IsNullOrEmpty(error)
                    ? $"The token endpoint refused the request ({status}): {error}{(string.IsNullOrEmpty(description) ? "." : $" — {description}")}"
                    : success
                        ? $"The token endpoint's answer has no access token ({status})."
                        : $"The token endpoint answered {status}.");
        }

        DateTimeOffset? expires = null;
        if (fields.GetValueOrDefault("expires_in") is { } expiresIn
            && double.TryParse(expiresIn, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0)
        {
            expires = now.AddSeconds(Math.Min(seconds, 100L * 365 * 24 * 3600));
        }

        var refreshToken = fields.GetValueOrDefault("refresh_token");
        return new OAuth2Token(
            settings.TokenUrl.AbsoluteUri,
            settings.ClientId,
            fields.GetValueOrDefault("scope") is { Length: > 0 } scope ? scope : settings.Scope,
            accessToken,
            string.IsNullOrEmpty(refreshToken) ? previousRefreshToken : refreshToken,
            expires,
            now);
    }

    /// <summary>
    /// The top-level fields of the answer: JSON as asked for, or a form-encoded body, which some
    /// older servers (GitHub, …) send. Numbers and booleans are kept as their text.
    /// </summary>
    private static Dictionary<string, string> Fields(HttpResponseData response, string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var contentType = response.Headers
            .FirstOrDefault(h => string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;

        if (contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var pair in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = pair.IndexOf('=');
                var name = WebUtility.UrlDecode(equals < 0 ? pair : pair[..equals]);
                fields.TryAdd(name, equals < 0 ? string.Empty : WebUtility.UrlDecode(pair[(equals + 1)..]));
            }

            return fields;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return fields;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                var value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.GetRawText(),
                    _ => null,
                };
                if (value is not null)
                {
                    fields.TryAdd(property.Name, value);
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON: no fields, reported as an answer without a token.
        }

        return fields;
    }
}
