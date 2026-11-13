using System.Globalization;

namespace Dynypost.Domain.Http;

/// <summary>A cookie kept in an environment's cookie jar.</summary>
/// <param name="Domain">
/// Where it is sent: a host without a leading dot (<c>api.example.com</c>) for a host-only cookie,
/// set without a <c>Domain</c> attribute and sent to that host only; a domain with a leading dot
/// (<c>.example.com</c>) for a cookie sent to the domain and its subdomains.
/// </param>
/// <param name="Path">The path prefix it is sent for, e.g. <c>/</c>.</param>
/// <param name="Expires">When it expires; <c>null</c> for a session cookie.</param>
public sealed record StoredCookie(
    string Name,
    string Value,
    string Domain,
    string Path,
    DateTimeOffset? Expires = null,
    bool Secure = false,
    bool HttpOnly = false)
{
    /// <summary>Whether two cookies are the same cookie (one replaces the other): same name, domain and path.</summary>
    public bool SameIdentity(string name, string domain, string path) =>
        Name == name
        && string.Equals(Domain, domain, StringComparison.OrdinalIgnoreCase)
        && Path == path;

    /// <summary>Whether the cookie has expired at <paramref name="now"/>.</summary>
    public bool IsExpired(DateTimeOffset now) => Expires is { } expires && expires <= now;
}

/// <summary>A cookie a response asked to set, as written in its <c>Set-Cookie</c> header.</summary>
/// <param name="MaxAge">The <c>Max-Age</c> attribute in seconds, if any.</param>
/// <param name="SameSite">The <c>SameSite</c> attribute as written, if any.</param>
public sealed record ResponseCookie(
    string Name,
    string Value,
    string? Domain = null,
    string? Path = null,
    DateTimeOffset? Expires = null,
    long? MaxAge = null,
    bool Secure = false,
    bool HttpOnly = false,
    string? SameSite = null);

/// <summary>Reads <c>Set-Cookie</c> header values (RFC 6265, section 5.2).</summary>
public static class SetCookieHeader
{
    private static readonly string[] DateFormats =
    [
        "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
        "ddd, d MMM yyyy HH:mm:ss 'GMT'",
        "dddd, dd-MMM-yy HH:mm:ss 'GMT'",
        "ddd, dd-MMM-yyyy HH:mm:ss 'GMT'",
        "ddd, dd-MMM-yy HH:mm:ss 'GMT'",
        "ddd MMM d HH:mm:ss yyyy",
    ];

    /// <summary>Parses one header value; returns <c>null</c> when it has no <c>name=value</c> pair.</summary>
    public static ResponseCookie? Parse(string header)
    {
        var parts = header.Split(';');
        var pair = parts[0];
        var equals = pair.IndexOf('=');
        if (equals < 0)
        {
            return null;
        }

        var name = pair[..equals].Trim();
        var value = pair[(equals + 1)..].Trim();
        if (name.Length == 0)
        {
            return null;
        }

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        var cookie = new ResponseCookie(name, value);
        foreach (var attribute in parts.Skip(1))
        {
            var index = attribute.IndexOf('=');
            var key = (index < 0 ? attribute : attribute[..index]).Trim();
            var text = index < 0 ? string.Empty : attribute[(index + 1)..].Trim();
            switch (key.ToLowerInvariant())
            {
                case "domain" when text.Length > 0:
                    cookie = cookie with { Domain = text };
                    break;
                case "path" when text.StartsWith('/'):
                    cookie = cookie with { Path = text };
                    break;
                case "expires" when ParseDate(text) is { } expires:
                    cookie = cookie with { Expires = expires };
                    break;
                case "max-age" when long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seconds):
                    cookie = cookie with { MaxAge = seconds };
                    break;
                case "secure":
                    cookie = cookie with { Secure = true };
                    break;
                case "httponly":
                    cookie = cookie with { HttpOnly = true };
                    break;
                case "samesite" when text.Length > 0:
                    cookie = cookie with { SameSite = text };
                    break;
            }
        }

        return cookie;
    }

    private static DateTimeOffset? ParseDate(string text) =>
        DateTimeOffset.TryParseExact(
            text,
            DateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces,
            out var date)
            ? date
            : DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date)
                ? date
                : null;
}
