using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Missive.Domain.Http;
using Missive.Domain.Requests;

namespace Missive.Infrastructure.Interchange;

/// <summary>URL, query and body helpers shared by the import and export formats.</summary>
internal static partial class InterchangeText
{
    /// <summary>JSON as other apps write it: indented with 2 spaces, LF line endings, non-ASCII kept.</summary>
    public static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Write(JsonNode node) => node.ToJsonString(WriteOptions) + "\n";

    /// <summary>
    /// Splits a URL into the part before <c>?</c> and its query parameters, decoded (<c>+</c> is a
    /// space). Missive encodes parameters when it sends them, so they are stored decoded.
    /// </summary>
    public static (string Url, List<KeyValueEntry> Query) SplitQuery(string url)
    {
        var index = url.IndexOf('?');
        if (index < 0)
        {
            return (url, []);
        }

        var query = url[(index + 1)..];
        var hash = query.IndexOf('#');
        var fragment = hash < 0 ? string.Empty : query[hash..];
        if (hash >= 0)
        {
            query = query[..hash];
        }

        var entries = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var equals = pair.IndexOf('=');
                return equals < 0
                    ? new KeyValueEntry(Decode(pair), string.Empty)
                    : new KeyValueEntry(Decode(pair[..equals]), Decode(pair[(equals + 1)..]));
            })
            .ToList();
        return (url[..index] + fragment, entries);
    }

    /// <summary>Percent-decodes a query component; <c>+</c> is a space.</summary>
    public static string Decode(string text)
    {
        var spaced = text.Replace('+', ' ');
        try
        {
            return Uri.UnescapeDataString(spaced);
        }
        catch (UriFormatException)
        {
            return spaced;
        }
    }

    /// <summary>Percent-encodes a query component, leaving <c>{{variable}}</c> references as written.</summary>
    public static string Encode(string text)
    {
        var builder = new StringBuilder();
        var last = 0;
        foreach (Match match in Variable().Matches(text))
        {
            builder.Append(Uri.EscapeDataString(text[last..match.Index])).Append(match.Value);
            last = match.Index + match.Length;
        }

        return builder.Append(Uri.EscapeDataString(text[last..])).ToString();
    }

    /// <summary>The URL with the enabled parameters appended, as other apps show it.</summary>
    public static string JoinQuery(string url, IEnumerable<KeyValueEntry> parameters)
    {
        var query = string.Join('&', parameters
            .Where(p => p.Enabled)
            .Select(p => p.Value.Length == 0 && p.Key.Length > 0 ? Encode(p.Key) : $"{Encode(p.Key)}={Encode(p.Value)}"));
        if (query.Length == 0)
        {
            return url;
        }

        return url + (url.Contains('?') ? "&" : "?") + query;
    }

    /// <summary>
    /// Parses a <c>form</c> or <c>urlencoded</c> body: one <c>name=value</c> per line, as Missive
    /// stores them. Values in <c>urlencoded</c> bodies are already encoded.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> BodyFields(string content, bool encoded)
    {
        var lines = content.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0);
        if (encoded)
        {
            lines = lines.SelectMany(line => line.Split('&', StringSplitOptions.RemoveEmptyEntries));
        }

        foreach (var line in lines)
        {
            var index = line.IndexOf('=');
            var name = index < 0 ? line : line[..index].Trim();
            var value = index < 0 ? string.Empty : line[(index + 1)..].Trim();
            yield return encoded
                ? KeyValuePair.Create(Decode(name), Decode(value))
                : KeyValuePair.Create(name, value);
        }
    }

    /// <summary>A <c>form</c> body from fields: one <c>name=value</c> per line.</summary>
    public static string FormBody(IEnumerable<KeyValuePair<string, string>> fields) =>
        string.Join('\n', fields.Select(f => $"{OneLine(f.Key)}={OneLine(f.Value)}"));

    /// <summary>A <c>urlencoded</c> body from decoded fields: one encoded <c>name=value</c> per line.</summary>
    public static string UrlEncodedBody(IEnumerable<KeyValuePair<string, string>> fields) =>
        string.Join('\n', fields.Select(f => $"{Encode(f.Key)}={Encode(f.Value)}"));

    /// <summary>Whether a media type is JSON (<c>application/json</c>, <c>application/problem+json</c>, …).</summary>
    public static bool IsJson(string? mediaType)
    {
        var type = MediaType(mediaType);
        return type == "application/json" || type.EndsWith("+json", StringComparison.Ordinal) || type == "text/json";
    }

    /// <summary>The media type without parameters, lower case.</summary>
    public static string MediaType(string? contentType) =>
        (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();

    /// <summary>The body type for a media type; <see cref="BodyType.Text"/> for anything else.</summary>
    public static BodyType BodyTypeFor(string? mediaType) => MediaType(mediaType) switch
    {
        "application/x-www-form-urlencoded" => BodyType.UrlEncoded,
        "multipart/form-data" => BodyType.Form,
        var type when IsJson(type) => BodyType.Json,
        _ => BodyType.Text,
    };

    /// <summary>The media type a Missive body is sent with.</summary>
    public static string MediaTypeOf(BodyType type) => type switch
    {
        BodyType.Json => "application/json",
        BodyType.UrlEncoded => "application/x-www-form-urlencoded",
        BodyType.Form => "multipart/form-data",
        _ => "text/plain",
    };

    /// <summary>Whether Missive sets this media type itself, so an explicit header isn't needed.</summary>
    /// <remarks>
    /// A multipart <c>Content-Type</c> is always left to Missive, which adds the boundary; a copied
    /// one (as in a HAR capture) would name the wrong boundary.
    /// </remarks>
    public static bool IsImpliedContentType(BodyType type, string? contentType) =>
        type != BodyType.None
        && MediaType(contentType) == MediaTypeOf(type)
        && (type == BodyType.Form || !(contentType ?? string.Empty).Contains(';'));

    /// <summary>The headers without a <c>Content-Type</c> that Missive sets by itself for the body.</summary>
    public static List<KeyValueEntry> WithoutImpliedContentType(IEnumerable<KeyValueEntry> headers, BodyType type) =>
        headers
            .Where(h => !(string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase) && IsImpliedContentType(type, h.Value)))
            .ToList();

    /// <summary>Applies <paramref name="map"/> to every text of a request that may hold <c>{{variable}}</c> references.</summary>
    public static HttpRequestDefinition MapText(HttpRequestDefinition request, Func<string, string> map) =>
        request with
        {
            Url = map(request.Url),
            Params = request.Params.Select(p => p with { Key = map(p.Key), Value = map(p.Value) }).ToList(),
            Headers = request.Headers.Select(h => h with { Key = map(h.Key), Value = map(h.Value) }).ToList(),
            Auth = request.Auth switch
            {
                BasicAuth basic => new BasicAuth(map(basic.Username), map(basic.Password)),
                BearerAuth bearer => new BearerAuth(map(bearer.Token)),
                ApiKeyAuth apiKey => apiKey with { Key = map(apiKey.Key), Value = map(apiKey.Value) },
                OAuth2Auth oauth => oauth with
                {
                    TokenUrl = map(oauth.TokenUrl),
                    ClientId = map(oauth.ClientId),
                    ClientSecret = map(oauth.ClientSecret),
                    AuthUrl = map(oauth.AuthUrl),
                    RedirectUri = map(oauth.RedirectUri),
                    Scope = map(oauth.Scope),
                    Audience = map(oauth.Audience),
                },
                var other => other,
            },
            Body = request.Body with { Content = map(request.Body.Content) },
        };

    /// <summary>
    /// The OAuth 2.0 grant Missive uses for another app's grant name, or <c>null</c> (with a
    /// warning) for grants it doesn't support (implicit, password, …).
    /// </summary>
    public static OAuth2Grant? OAuth2Grant(string? grant, string owner, List<string> warnings)
    {
        switch (grant)
        {
            case "client_credentials":
                return Missive.Domain.Requests.OAuth2Grant.ClientCredentials;
            case null or "" or "authorization_code" or "authorization_code_with_pkce":
                return Missive.Domain.Requests.OAuth2Grant.AuthorizationCode;
            default:
                warnings.Add($"'{owner}' uses the OAuth 2.0 {grant.Replace('_', ' ')} grant, which Missive doesn't support; it was imported without auth.");
                return null;
        }
    }

    /// <summary>
    /// A redirect URL Missive can listen on (an http URL on this computer, or one built from
    /// variables), else empty so Missive picks one, with a warning: web callbacks such as other
    /// apps' own can't receive the code.
    /// </summary>
    public static string LoopbackRedirect(string? redirect, OAuth2Grant grant, string owner, List<string> warnings)
    {
        redirect = redirect?.Trim() ?? string.Empty;
        if (redirect.Length == 0 || grant != Missive.Domain.Requests.OAuth2Grant.AuthorizationCode || redirect.Contains("{{", StringComparison.Ordinal))
        {
            return grant == Missive.Domain.Requests.OAuth2Grant.AuthorizationCode ? redirect : string.Empty;
        }

        if (Uri.TryCreate(redirect, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp && OAuth2Flow.IsLoopback(uri))
        {
            return redirect;
        }

        warnings.Add($"'{owner}' redirects to {redirect}, which Missive can't receive; it uses a redirect URL on this computer (http://127.0.0.1) instead, which the OAuth 2.0 client may need to allow.");
        return string.Empty;
    }

    /// <summary>Replaces <c>{{name}}</c> references using <paramref name="rename"/>.</summary>
    public static string RenameVariables(string text, Func<string, string> rename) =>
        VariableTemplate.Replace(text, name => "{{" + rename(name) + "}}");

    private static string OneLine(string text) => text.Replace("\r", string.Empty).Replace('\n', ' ');

    [GeneratedRegex(@"\{\{[^{}]*\}\}")]
    private static partial Regex Variable();
}
