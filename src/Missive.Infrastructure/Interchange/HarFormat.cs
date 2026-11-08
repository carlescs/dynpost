using System.Text.Json.Nodes;
using Missive.Application.Interchange;
using Missive.Domain.Requests;

namespace Missive.Infrastructure.Interchange;

/// <summary>
/// HTTP Archive (HAR 1.2) files, as saved by browser developer tools and proxies. Each entry
/// becomes a request, grouped in a folder per host.
/// </summary>
internal static class HarFormat
{
    /// <summary>Headers that describe the connection rather than the request; the sender sets them.</summary>
    private static readonly HashSet<string> ConnectionHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Connection", "Keep-Alive", "Transfer-Encoding", "Upgrade", "TE",
    };

    public static bool IsHar(JsonNode root) => root.Obj("log").Get("entries") is JsonArray;

    public static ParsedCollection Import(JsonNode root, string fallbackName)
    {
        var warnings = new List<string>();
        var hosts = new Dictionary<string, List<HttpRequestDefinition>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        var unsupported = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var entry in root.Obj("log").Arr("entries"))
        {
            var request = entry.Obj("request");
            var url = request.Str("url");
            if (url is null)
            {
                continue;
            }

            var methodName = request.Str("method") ?? "GET";
            if (!RequestMethods.TryParse(methodName, out var method))
            {
                unsupported.Add(methodName.ToUpperInvariant());
                continue;
            }

            var (baseUrl, urlQuery) = InterchangeText.SplitQuery(url);
            var query = request.Get("queryString") is JsonArray
                ? request.Arr("queryString")
                    .Where(q => q.Str("name") is not null)
                    .Select(q => new KeyValueEntry(InterchangeText.Decode(q.Str("name")!), InterchangeText.Decode(q.Str("value") ?? string.Empty)))
                    .ToList()
                : urlQuery;

            var headers = request.Arr("headers")
                .Select(h => (Name: h.Str("name") ?? string.Empty, Value: h.Str("value") ?? string.Empty))
                .Where(h => h.Name.Length > 0 && !h.Name.StartsWith(':') && !ConnectionHeaders.Contains(h.Name))
                .Select(h => new KeyValueEntry(h.Name, h.Value))
                .ToList();

            var body = ImportBody(request.Obj("postData"));
            var host = Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? uri.Authority : string.Empty;
            var path = uri?.AbsolutePath ?? baseUrl;

            if (!hosts.TryGetValue(host, out var requests))
            {
                hosts[host] = requests = [];
                order.Add(host);
            }

            requests.Add(new HttpRequestDefinition(
                $"{method.ToName()} {path}",
                method,
                baseUrl,
                query,
                InterchangeText.WithoutImpliedContentType(headers, body.Type),
                RequestAuth.None,
                body));
        }

        if (unsupported.Count > 0)
        {
            warnings.Add($"Skipped requests with methods Missive doesn't support: {string.Join(", ", unsupported)}.");
        }

        var folders = order
            .Where(host => host.Length > 0)
            .Select(host => new CollectionFolder(host, [], hosts[host]))
            .ToList();
        var top = new CollectionFolder(
            StripExtension(fallbackName),
            folders,
            hosts.GetValueOrDefault(string.Empty) ?? []);
        return new ParsedCollection("HAR archive", new RequestCollection(top, []), warnings);
    }

    private static RequestBody ImportBody(JsonObject? postData)
    {
        if (postData is null)
        {
            return RequestBody.None;
        }

        var mimeType = postData.Str("mimeType");
        var type = InterchangeText.BodyTypeFor(mimeType);
        var text = postData.Str("text") ?? string.Empty;
        var fields = new List<KeyValuePair<string, string>>();
        foreach (var field in postData.Arr("params").Where(p => p.Str("name") is not null))
        {
            if (field.Str("fileName") is { Length: > 0 } fileName)
            {
                var contentType = field.Str("contentType");
                fields.Add(KeyValuePair.Create(
                    field.Str("name")!,
                    $"@{fileName}{(string.IsNullOrWhiteSpace(contentType) ? string.Empty : $";type={contentType}")}"));
            }
            else
            {
                fields.Add(KeyValuePair.Create(field.Str("name")!, field.Str("value") ?? string.Empty));
            }
        }

        return type switch
        {
            BodyType.UrlEncoded => new RequestBody(type, fields.Count > 0
                ? InterchangeText.UrlEncodedBody(fields)
                : string.Join('\n', text.Split('&', StringSplitOptions.RemoveEmptyEntries))),
            BodyType.Form => fields.Count > 0 ? new RequestBody(type, InterchangeText.FormBody(fields)) : RequestBody.None,
            _ when postData.Str("fileName") is { Length: > 0 } fileName => new RequestBody(BodyType.Binary, fileName),
            _ => text.Length == 0 ? RequestBody.None : new RequestBody(type, text),
        };
    }

    private static string StripExtension(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        return name.Length == 0 ? "HAR import" : name;
    }
}
