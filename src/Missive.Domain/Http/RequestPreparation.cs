using System.Text;
using Missive.Domain.Requests;

namespace Missive.Domain.Http;

/// <summary>The outcome of <see cref="RequestPreparation.Prepare"/>: a request, or why it can't be sent.</summary>
public sealed record PreparedRequest(OutgoingRequest? Request, IReadOnlyList<string> Errors)
{
    public bool IsValid => Request is not null;
}

/// <summary>Turns a saved request definition into the request that is actually sent.</summary>
public static class RequestPreparation
{
    public const string AuthorizationHeader = "Authorization";

    /// <summary>
    /// Resolves <c>{{variable}}</c> references, appends enabled query parameters to the URL, applies
    /// auth and builds the body. Disabled parameters and headers are left out. A header the request
    /// sets explicitly wins over the one auth would add. A URL without a scheme gets <c>http://</c>.
    /// </summary>
    public static PreparedRequest Prepare(HttpRequestDefinition request, IReadOnlyDictionary<string, string> variables)
    {
        var errors = new List<string>();
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        string Resolve(string text) => VariableTemplate.Resolve(text, variables, missing);

        var url = Resolve(request.Url).Trim();
        var query = Resolved(request.Params, Resolve);
        var headers = Resolved(request.Headers, Resolve);
        ApplyAuth(request.Auth, Resolve, headers, query, errors);
        var body = BuildBody(request.Body, Resolve);

        if (missing.Count > 0)
        {
            errors.Insert(0, $"The request uses variables that aren't defined: {string.Join(", ", missing)}.");
        }

        CheckHeaders(headers, errors);
        var uri = BuildUrl(url, query, errors, reportInvalid: missing.Count == 0);

        return errors.Count == 0 && uri is not null
            ? new PreparedRequest(new OutgoingRequest(request.Method.ToName(), uri, headers, body), [])
            : new PreparedRequest(null, errors);
    }

    private static List<KeyValuePair<string, string>> Resolved(IReadOnlyList<KeyValueEntry> entries, Func<string, string> resolve) =>
        entries
            .Where(entry => entry.Enabled)
            .Select(entry => KeyValuePair.Create(resolve(entry.Key), resolve(entry.Value)))
            .ToList();

    private static void ApplyAuth(
        RequestAuth auth,
        Func<string, string> resolve,
        List<KeyValuePair<string, string>> headers,
        List<KeyValuePair<string, string>> query,
        List<string> errors)
    {
        switch (auth)
        {
            case BasicAuth basic:
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{resolve(basic.Username)}:{resolve(basic.Password)}"));
                AddHeaderUnlessSet(headers, AuthorizationHeader, $"Basic {credentials}");
                break;

            case BearerAuth bearer:
                var token = resolve(bearer.Token).Trim();
                if (token.Length == 0)
                {
                    errors.Add("Enter a bearer token, or set auth to 'No auth'.");
                }
                else
                {
                    AddHeaderUnlessSet(headers, AuthorizationHeader, $"Bearer {token}");
                }

                break;

            case ApiKeyAuth apiKey:
                var key = resolve(apiKey.Key).Trim();
                var value = resolve(apiKey.Value);
                if (key.Length == 0)
                {
                    errors.Add("Enter the name of the API key, or set auth to 'No auth'.");
                }
                else if (apiKey.In == ApiKeyLocation.Query)
                {
                    query.Add(KeyValuePair.Create(key, value));
                }
                else
                {
                    AddHeaderUnlessSet(headers, key, value);
                }

                break;
        }
    }

    private static void AddHeaderUnlessSet(List<KeyValuePair<string, string>> headers, string name, string value)
    {
        if (!headers.Any(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)))
        {
            headers.Add(KeyValuePair.Create(name, value));
        }
    }

    private static OutgoingBody? BuildBody(RequestBody body, Func<string, string> resolve) => body.Type switch
    {
        BodyType.Json => new TextBody(resolve(body.Content), "application/json"),
        BodyType.Text => new TextBody(resolve(body.Content), "text/plain"),
        // One name=value pair per line (or already joined with '&'), sent as written.
        BodyType.UrlEncoded => new TextBody(
            string.Join('&', Lines(resolve(body.Content))),
            "application/x-www-form-urlencoded"),
        // One name=value field per line; a line without '=' is a field with an empty value.
        BodyType.Form => new MultipartBody(Lines(resolve(body.Content))
            .Select(line => line.IndexOf('=') is var index and >= 0
                ? KeyValuePair.Create(line[..index].Trim(), line[(index + 1)..].Trim())
                : KeyValuePair.Create(line, string.Empty))
            .ToList()),
        _ => null,
    };

    private static IEnumerable<string> Lines(string content) =>
        content.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0);

    private static void CheckHeaders(List<KeyValuePair<string, string>> headers, List<string> errors)
    {
        foreach (var (name, value) in headers)
        {
            if (name.Length == 0 || !name.All(IsTokenChar))
            {
                errors.Add($"'{name}' is not a valid header name.");
            }
            else if (value.Contains('\r') || value.Contains('\n'))
            {
                errors.Add($"The value of the header '{name}' can't contain line breaks.");
            }
        }
    }

    // RFC 9110 token characters.
    private static bool IsTokenChar(char c) =>
        c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
            or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';

    private static Uri? BuildUrl(string url, List<KeyValuePair<string, string>> query, List<string> errors, bool reportInvalid)
    {
        if (url.Length == 0)
        {
            errors.Add("Enter a URL.");
            return null;
        }

        if (!url.Contains("://", StringComparison.Ordinal))
        {
            url = "http://" + url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            // A URL with unresolved variables is already reported as such.
            if (reportInvalid)
            {
                errors.Add($"'{url}' is not a valid http or https URL.");
            }

            return null;
        }

        if (query.Count == 0)
        {
            return uri;
        }

        var builder = new UriBuilder(uri);
        var existing = builder.Query.TrimStart('?');
        var added = string.Join('&', query.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        builder.Query = existing.Length == 0 ? added : $"{existing}&{added}";
        return builder.Uri;
    }
}
