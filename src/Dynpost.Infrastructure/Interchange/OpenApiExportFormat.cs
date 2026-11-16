using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dynpost.Application.Interchange;
using Dynpost.Domain.Requests;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Infrastructure.Interchange;

/// <summary>
/// Writes an OpenAPI 3.0 document: one operation per request (tagged with its folders), servers
/// from the environments' <c>baseUrl</c>, security schemes from auth, and examples from bodies and
/// parameters. What OpenAPI can't hold is reported. The output is stable for the same workspace.
/// </summary>
internal static partial class OpenApiExportFormat
{
    private const string BaseUrl = "baseUrl";

    /// <summary>Header parameters OpenAPI describes another way (and ignores as parameters).</summary>
    private static readonly HashSet<string> SkippedHeaders = new(StringComparer.OrdinalIgnoreCase) { "Accept", "Content-Type", "Authorization" };

    public static ExportOutput Export(RequestCollection collection)
    {
        var warnings = new List<string>();
        var paths = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        var schemes = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        var operationIds = new HashSet<string>(StringComparer.Ordinal);
        var serverKeys = new List<string>();
        var operationServers = new List<(JsonObject Operation, string Server)>();
        var tags = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (request, folders) in Requests(collection.Root, []))
        {
            var (server, path) = SplitUrl(PathParameters.Apply(request.Url, [], v => v).Trim());
            if (path is null)
            {
                warnings.Add($"Skipped '{request.Name}': its URL '{request.Url}' has no host or {{{{{BaseUrl}}}}} OpenAPI can use.");
                continue;
            }

            if (!serverKeys.Contains(server))
            {
                serverKeys.Add(server);
            }

            var method = (request.Body.Type == BodyType.Graphql ? "POST" : request.Method.ToName()).ToLowerInvariant();
            var item = paths.TryGetValue(path, out var existing) ? existing : paths[path] = new JsonObject();
            if (item.ContainsKey(method))
            {
                warnings.Add($"Skipped '{request.Name}': another request is already {method.ToUpperInvariant()} {path}.");
                continue;
            }

            var operation = new JsonObject
            {
                ["operationId"] = UniqueId(Slug.From(request.Name, "request"), operationIds),
                ["summary"] = request.Name,
            };
            if (request.Description.Length > 0)
            {
                operation["description"] = request.Description;
            }

            if (folders.Count > 0)
            {
                var tag = string.Join(" / ", folders);
                tags.Add(tag);
                operation["tags"] = new JsonArray(tag);
            }

            operationServers.Add((operation, server));
            var parameters = Parameters(request, path);
            if (parameters.Count > 0)
            {
                operation["parameters"] = parameters;
            }

            if (RequestBody(request, warnings) is { } body)
            {
                operation["requestBody"] = body;
            }

            operation["responses"] = new JsonObject { ["default"] = new JsonObject { ["description"] = "Response" } };
            switch (Security(request.Auth, schemes))
            {
                case { } name:
                    operation["security"] = new JsonArray(new JsonObject { [name] = new JsonArray() });
                    break;
                case null when request.Auth is NoAuth or InheritAuth:
                    operation["security"] = new JsonArray();
                    break;
            }

            if (request.Settings is { IsEmpty: false })
            {
                warnings.Add($"'{request.Name}' has request settings (timeout, redirects, TLS); OpenAPI has no place for them.");
            }

            item[method] = operation;
        }

        var document = new JsonObject
        {
            ["openapi"] = "3.0.3",
            ["info"] = new JsonObject { ["title"] = collection.Root.Name, ["version"] = "1.0.0" },
        };
        // The server most requests use is the document's; the others are set per operation.
        var main = operationServers.GroupBy(o => o.Server).OrderByDescending(g => g.Count()).ThenBy(g => serverKeys.IndexOf(g.Key)).FirstOrDefault()?.Key;
        foreach (var (operation, server) in operationServers.Where(o => o.Server != main))
        {
            var responses = operation["responses"];
            operation.Remove("responses");
            operation["servers"] = new JsonArray(Server(server, collection));
            operation["responses"] = responses;
        }

        var servers = main is null ? new JsonArray() : Servers(main, collection);
        if (servers.Count > 0)
        {
            document["servers"] = servers;
        }

        if (tags.Count > 0)
        {
            document["tags"] = new JsonArray(tags.Select(t => (JsonNode)new JsonObject { ["name"] = t }).ToArray());
        }

        document["paths"] = new JsonObject(paths.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value)));
        if (schemes.Count > 0)
        {
            document["components"] = new JsonObject
            {
                ["securitySchemes"] = new JsonObject(schemes.Select(s => KeyValuePair.Create(s.Key, (JsonNode?)s.Value))),
            };
        }

        if (HasVariables(collection.Root))
        {
            warnings.Add("Some values use {{variables}}; they are written as they are.");
        }

        return new ExportOutput(InterchangeText.Write(document), [], warnings);
    }

    private static IEnumerable<(HttpRequestDefinition Request, IReadOnlyList<string> Folders)> Requests(CollectionFolder folder, IReadOnlyList<string> path)
    {
        foreach (var request in folder.Requests)
        {
            yield return (request, path);
        }

        foreach (var child in folder.Folders)
        {
            foreach (var item in Requests(child, [.. path, child.Name]))
            {
                yield return item;
            }
        }
    }

    /// <summary>
    /// The server part (<c>{{baseUrl}}</c>, <c>{{host}}</c> or <c>https://host</c>) and the path
    /// (with <c>{name}</c> path parameters) of a URL; the path is <c>null</c> when there is none.
    /// </summary>
    private static (string Server, string? Path) SplitUrl(string url)
    {
        var withoutQuery = url.Split('?', '#')[0];
        string server;
        string rest;
        if (LeadingVariable().Match(withoutQuery) is { Success: true } variable)
        {
            server = variable.Value;
            rest = withoutQuery[variable.Length..];
        }
        else if (Uri.TryCreate(withoutQuery.Contains("://") ? withoutQuery : "http://" + withoutQuery, UriKind.Absolute, out var uri)
            && !withoutQuery.Contains("{{", StringComparison.Ordinal))
        {
            server = $"{uri.Scheme}://{uri.Authority}";
            rest = Uri.UnescapeDataString(uri.AbsolutePath);
        }
        else
        {
            var scheme = withoutQuery.IndexOf("://", StringComparison.Ordinal);
            var slash = withoutQuery.IndexOf('/', scheme < 0 ? 0 : scheme + 3);
            if (slash < 0)
            {
                return (withoutQuery, "/");
            }

            server = withoutQuery[..slash];
            rest = withoutQuery[slash..];
        }

        if (rest.Length == 0)
        {
            rest = "/";
        }

        if (!rest.StartsWith('/'))
        {
            return (server, null);
        }

        var path = PathVariable().Replace(rest, m => "{" + m.Groups["name"].Value + "}");
        path = VariableReference().Replace(path, m => "{" + m.Groups["name"].Value + "}");
        return (server, path);
    }

    private static JsonArray Servers(string first, RequestCollection collection) => new(Server(first, collection));

    /// <summary>
    /// A server object: <c>{{baseUrl}}</c> becomes one entry per environment that defines it (the
    /// first of them here); other <c>{{variables}}</c> become server variables.
    /// </summary>
    private static JsonObject Server(string server, RequestCollection collection)
    {
        var name = LeadingVariable().Match(server) is { Success: true } m ? m.Groups["name"].Value : null;
        if (name is not null)
        {
            var defined = collection.Environments
                .Select(e => (e.Name, Value: e.Variables.FirstOrDefault(v => v.Name == name && !v.Secret)?.Value))
                .FirstOrDefault(e => !string.IsNullOrEmpty(e.Value) && !e.Value.Contains("{{", StringComparison.Ordinal));
            if (defined.Value is not null)
            {
                return new JsonObject { ["url"] = defined.Value, ["description"] = defined.Name };
            }
        }

        var url = VariableReference().Replace(server, v => "{" + v.Groups["name"].Value + "}");
        var variables = new JsonObject();
        foreach (Match v in VariableReference().Matches(server))
        {
            var variableName = v.Groups["name"].Value;
            var value = collection.Environments.SelectMany(e => e.Variables).FirstOrDefault(x => x.Name == variableName && !x.Secret)?.Value;
            variables[variableName] = new JsonObject { ["default"] = value ?? "http://localhost" };
        }

        var result = new JsonObject { ["url"] = url };
        if (variables.Count > 0)
        {
            result["variables"] = variables;
        }

        return result;
    }

    private static JsonArray Parameters(HttpRequestDefinition request, string path)
    {
        var parameters = new JsonArray();
        var values = request.PathParams.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        foreach (Match match in PathTemplate().Matches(path))
        {
            var name = match.Groups["name"].Value;
            parameters.Add(Parameter(name, "path", values.GetValueOrDefault(name) ?? string.Empty, required: true));
        }

        foreach (var param in request.Params.Where(p => p.Enabled))
        {
            parameters.Add(Parameter(param.Key, "query", param.Value, required: false));
        }

        foreach (var header in request.Headers.Where(h => h.Enabled && !SkippedHeaders.Contains(h.Key)))
        {
            parameters.Add(Parameter(header.Key, "header", header.Value, required: false));
        }

        return parameters;
    }

    private static JsonObject Parameter(string name, string location, string example, bool required)
    {
        var parameter = new JsonObject
        {
            ["name"] = name,
            ["in"] = location,
        };
        if (required)
        {
            parameter["required"] = true;
        }

        parameter["schema"] = new JsonObject { ["type"] = "string" };
        if (example.Length > 0)
        {
            parameter["example"] = example;
        }

        return parameter;
    }

    private static JsonObject? RequestBody(HttpRequestDefinition request, List<string> warnings)
    {
        var body = request.Body;
        JsonObject Content(string mediaType, JsonObject media) => new()
        {
            ["content"] = new JsonObject { [mediaType] = media },
        };

        switch (body.Type)
        {
            case BodyType.None:
                return null;
            case BodyType.Json:
                return Content(ContentTypeOf(request) ?? "application/json", new JsonObject { ["example"] = JsonExample(body.Content) });
            case BodyType.Text:
                return Content(ContentTypeOf(request) ?? "text/plain", new JsonObject
                {
                    ["schema"] = new JsonObject { ["type"] = "string" },
                    ["example"] = body.Content,
                });
            case BodyType.Graphql:
                return Content("application/json", new JsonObject
                {
                    ["example"] = new JsonObject { ["query"] = body.Content, ["variables"] = JsonExample(body.Variables.Length > 0 ? body.Variables : "{}") },
                });
            case BodyType.Binary:
                warnings.Add($"'{request.Name}' sends a file as its body; only its media type is described.");
                return Content(ContentTypeOf(request) ?? "application/octet-stream", new JsonObject
                {
                    ["schema"] = new JsonObject { ["type"] = "string", ["format"] = "binary" },
                });
            case BodyType.UrlEncoded or BodyType.Form:
                var properties = new JsonObject();
                foreach (var field in FormFields.Of(body).Where(f => f.Enabled && f.Name.Length > 0))
                {
                    properties[field.Name] = field.IsFile
                        ? new JsonObject { ["type"] = "string", ["format"] = "binary" }
                        : new JsonObject { ["type"] = "string", ["example"] = field.Value };
                }

                return Content(
                    body.Type == BodyType.Form ? "multipart/form-data" : "application/x-www-form-urlencoded",
                    new JsonObject { ["schema"] = new JsonObject { ["type"] = "object", ["properties"] = properties } });
            default:
                return null;
        }
    }

    private static string? ContentTypeOf(HttpRequestDefinition request) =>
        request.Headers.LastOrDefault(h => h.Enabled && h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value is { Length: > 0 } type
            ? InterchangeText.MediaType(type)
            : null;

    private static JsonNode? JsonExample(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return JsonValue.Create(text);
        }
    }

    /// <summary>Adds the security scheme of <paramref name="auth"/> and returns its name; <c>null</c> for no auth.</summary>
    private static string? Security(RequestAuth auth, SortedDictionary<string, JsonObject> schemes)
    {
        (string Name, JsonObject Scheme)? scheme = auth switch
        {
            BasicAuth => ("basicAuth", new JsonObject { ["type"] = "http", ["scheme"] = "basic" }),
            DigestAuth => ("digestAuth", new JsonObject { ["type"] = "http", ["scheme"] = "digest" }),
            BearerAuth => ("bearerAuth", new JsonObject { ["type"] = "http", ["scheme"] = "bearer" }),
            ApiKeyAuth apiKey => ($"apiKey_{Slug.From(apiKey.Key, "key")}", new JsonObject
            {
                ["type"] = "apiKey",
                ["in"] = apiKey.In == ApiKeyLocation.Query ? "query" : "header",
                ["name"] = apiKey.Key,
            }),
            OAuth2Auth oauth => ($"oauth2_{Slug.From(oauth.TokenUrl, "token")}", new JsonObject
            {
                ["type"] = "oauth2",
                ["flows"] = oauth.Grant == OAuth2Grant.ClientCredentials
                    ? new JsonObject { ["clientCredentials"] = new JsonObject { ["tokenUrl"] = oauth.TokenUrl, ["scopes"] = Scopes(oauth.Scope) } }
                    : new JsonObject
                    {
                        ["authorizationCode"] = new JsonObject
                        {
                            ["authorizationUrl"] = oauth.AuthUrl,
                            ["tokenUrl"] = oauth.TokenUrl,
                            ["scopes"] = Scopes(oauth.Scope),
                        },
                    },
            }),
            NoAuth or InheritAuth => null,
        };
        if (scheme is not { } found)
        {
            return null;
        }

        schemes.TryAdd(found.Name, found.Scheme);
        return found.Name;
    }

    private static JsonObject Scopes(string scope) =>
        new(scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().Order(StringComparer.Ordinal)
            .Select(s => KeyValuePair.Create(s, (JsonNode?)JsonValue.Create(s))));

    private static string UniqueId(string id, HashSet<string> taken)
    {
        var candidate = id;
        for (var n = 2; !taken.Add(candidate); n++)
        {
            candidate = $"{id}-{n}";
        }

        return candidate;
    }

    private static bool HasVariables(CollectionFolder folder) =>
        folder.Requests.Any(r => r.Url.Contains("{{", StringComparison.Ordinal) && !r.Url.StartsWith("{{" + BaseUrl + "}}", StringComparison.Ordinal)
            || r.Headers.Any(h => h.Value.Contains("{{", StringComparison.Ordinal))
            || r.Body.Content.Contains("{{", StringComparison.Ordinal))
        || folder.Folders.Any(HasVariables);

    [GeneratedRegex(@"^\{\{\s*(?<name>[^{}\s]+)\s*\}\}")]
    private static partial Regex LeadingVariable();

    [GeneratedRegex(@"\{\{\s*(?<name>[^{}\s]+)\s*\}\}")]
    private static partial Regex VariableReference();

    [GeneratedRegex(@"(?<=/):(?<name>[A-Za-z_][A-Za-z0-9_\-]*)(?=[/;.]|$)")]
    private static partial Regex PathVariable();

    [GeneratedRegex(@"\{(?<name>[^{}/]+)\}")]
    private static partial Regex PathTemplate();
}
