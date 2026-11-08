using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Missive.Application.Interchange;
using Missive.Domain.Environments;
using Missive.Domain.Requests;

namespace Missive.Infrastructure.Interchange;

/// <summary>
/// Insomnia's export format (version 4, also read by Bruno and Hoppscotch): a flat list of
/// resources (workspace, folders, requests, environments) linked by <c>parentId</c>. Insomnia
/// references environment variables as <c>{{ _.name }}</c>.
/// </summary>
internal static partial class InsomniaFormat
{
    public static bool IsExport(JsonNode root) =>
        root.Str("_type") == "export" && root.Get("resources") is JsonArray;

    // ---- Import ----

    public static ParsedCollection Import(JsonNode document, string fallbackName)
    {
        var warnings = new List<string>();
        var resources = document.Arr("resources").Where(r => r.Str("_id") is not null).ToList();
        var children = resources
            .GroupBy(r => r.Str("parentId") ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Get("metaSortKey")?.Text() is { } key && double.TryParse(key, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0).ToList());

        var workspaces = resources.Where(r => r.Str("_type") == "workspace").ToList();
        var skipped = new Dictionary<string, int>(StringComparer.Ordinal);

        CollectionFolder Folder(string id, string name)
        {
            var folders = new List<CollectionFolder>();
            var requests = new List<HttpRequestDefinition>();
            foreach (var resource in children.GetValueOrDefault(id) ?? [])
            {
                switch (resource.Str("_type"))
                {
                    case "request_group":
                        folders.Add(Folder(resource.Str("_id")!, resource.Str("name") ?? string.Empty));
                        break;
                    case "request":
                        if (ImportRequest(resource, warnings) is { } request)
                        {
                            requests.Add(request);
                        }

                        break;
                    case var type when type is "grpc_request" or "websocket_request" or "unit_test_suite" or "unit_test":
                        skipped[type] = skipped.GetValueOrDefault(type) + 1;
                        break;
                }
            }

            return new CollectionFolder(name, folders, requests);
        }

        CollectionFolder collection;
        if (workspaces.Count == 1)
        {
            collection = Folder(workspaces[0].Str("_id")!, workspaces[0].Str("name") ?? fallbackName);
        }
        else
        {
            // Several workspaces (or none, in hand-made files): one folder each, and top-level resources.
            var top = Folder(string.Empty, fallbackName);
            var folders = workspaces.Select(w => Folder(w.Str("_id")!, w.Str("name") ?? string.Empty));
            collection = top with { Folders = [.. folders, .. top.Folders] };
        }

        foreach (var (type, count) in skipped)
        {
            warnings.Add($"Skipped {count} {type.Replace('_', ' ')}(s): Missive only imports HTTP requests.");
        }

        var environments = new List<EnvironmentDefinition>();
        foreach (var workspace in workspaces)
        {
            environments.AddRange(ImportEnvironments(workspace, children));
        }

        var format = $"Insomnia export v{document.Str("__export_format") ?? "4"}";
        return new ParsedCollection(format, new RequestCollection(collection, environments), warnings);
    }

    /// <summary>
    /// The workspace's sub-environments, each with the base environment's variables underneath;
    /// or the base environment alone when it has no sub-environments.
    /// </summary>
    private static IEnumerable<EnvironmentDefinition> ImportEnvironments(JsonNode workspace, Dictionary<string, List<JsonNode>> children)
    {
        var bases = (children.GetValueOrDefault(workspace.Str("_id")!) ?? []).Where(r => r.Str("_type") == "environment").ToList();
        foreach (var baseEnvironment in bases)
        {
            var baseVariables = Flatten(baseEnvironment.Obj("data"));
            var subs = (children.GetValueOrDefault(baseEnvironment.Str("_id")!) ?? []).Where(r => r.Str("_type") == "environment").ToList();
            if (subs.Count == 0)
            {
                if (baseVariables.Count > 0)
                {
                    yield return new EnvironmentDefinition(baseEnvironment.Str("name") ?? "Base Environment", ToVariables(baseVariables));
                }

                continue;
            }

            foreach (var sub in subs)
            {
                var merged = new Dictionary<string, string>(baseVariables, StringComparer.Ordinal);
                foreach (var (key, value) in Flatten(sub.Obj("data")))
                {
                    merged[key] = value;
                }

                yield return new EnvironmentDefinition(sub.Str("name") ?? string.Empty, ToVariables(merged));
            }
        }
    }

    private static List<EnvironmentVariable> ToVariables(Dictionary<string, string> values) =>
        values.Select(v => new EnvironmentVariable(v.Key, FromInsomnia(v.Value))).ToList();

    /// <summary>Nested environment data becomes dotted names (<c>_.api.host</c> → <c>api.host</c>).</summary>
    private static Dictionary<string, string> Flatten(JsonObject? data)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        void Add(string prefix, JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                foreach (var (key, value) in obj)
                {
                    Add(prefix.Length == 0 ? key : $"{prefix}.{key}", value);
                }
            }
            else
            {
                result[prefix] = node.AnyText();
            }
        }

        if (data is not null)
        {
            Add(string.Empty, data);
        }

        result.Remove(string.Empty);
        return result;
    }

    private static HttpRequestDefinition? ImportRequest(JsonNode resource, List<string> warnings)
    {
        var name = resource.Str("name") ?? string.Empty;
        var methodName = resource.Str("method") ?? "GET";
        if (!RequestMethods.TryParse(methodName, out var method))
        {
            warnings.Add($"Skipped the request '{name}': Missive doesn't support the {methodName} method.");
            return null;
        }

        var (url, urlQuery) = InterchangeText.SplitQuery(resource.Str("url") ?? string.Empty);
        var parameters = urlQuery.Concat(Pairs(resource.Arr("parameters"))).ToList();
        var headers = Pairs(resource.Arr("headers"));
        var body = ImportBody(resource.Obj("body"), name, warnings);
        var request = new HttpRequestDefinition(
            name,
            method,
            url,
            parameters,
            InterchangeText.WithoutImpliedContentType(headers, body.Type),
            ImportAuth(resource.Obj("authentication"), name, warnings),
            body)
        {
            Description = resource.Str("description") ?? string.Empty,
        };

        var converted = InterchangeText.MapText(request, FromInsomnia);
        if (TemplateTag().IsMatch(converted.Url + converted.Body.Content + converted.Body.Variables + string.Join(' ', converted.Headers.Select(h => h.Value))))
        {
            warnings.Add($"'{name}' uses Insomnia template tags ({{% … %}}), which Missive doesn't run; they were kept as text.");
        }

        return converted;
    }

    private static List<KeyValueEntry> Pairs(IEnumerable<JsonNode> pairs) =>
        pairs
            .Where(p => p.Str("name") is not null)
            .Select(p => new KeyValueEntry(p.Str("name")!, p.Str("value") ?? string.Empty, !p.Bool("disabled")))
            .ToList();

    private static RequestAuth ImportAuth(JsonObject? auth, string owner, List<string> warnings)
    {
        if (auth is null || auth.Bool("disabled"))
        {
            return RequestAuth.None;
        }

        switch (auth.Str("type"))
        {
            case null or "none":
                return RequestAuth.None;
            case "basic":
                return new BasicAuth(auth.Str("username") ?? string.Empty, auth.Str("password") ?? string.Empty);
            case "bearer":
                var prefix = auth.Str("prefix");
                if (prefix is { Length: > 0 } && !string.Equals(prefix, "Bearer", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"'{owner}' sends its token with the prefix '{prefix}'; Missive always uses 'Bearer'.");
                }

                return new BearerAuth(auth.Str("token") ?? string.Empty);
            case "apikey":
                return new ApiKeyAuth(
                    auth.Str("key") ?? string.Empty,
                    auth.Str("value") ?? string.Empty,
                    auth.Str("addTo") == "queryParams" ? ApiKeyLocation.Query : ApiKeyLocation.Header);
            case "oauth2":
                if (InterchangeText.OAuth2Grant(auth.Str("grantType"), owner, warnings) is not { } grant)
                {
                    return RequestAuth.None;
                }

                return new OAuth2Auth(
                    grant,
                    auth.Str("accessTokenUrl") ?? string.Empty,
                    auth.Str("clientId") ?? string.Empty,
                    auth.Str("clientSecret") ?? string.Empty,
                    grant == OAuth2Grant.AuthorizationCode ? auth.Str("authorizationUrl") ?? string.Empty : string.Empty,
                    InterchangeText.LoopbackRedirect(auth.Str("redirectUrl"), grant, owner, warnings),
                    auth.Str("scope") ?? string.Empty,
                    auth.Str("audience") ?? string.Empty,
                    auth.Bool("credentialsInBody") ? OAuth2ClientAuthentication.Body : OAuth2ClientAuthentication.Header);
            case var type:
                warnings.Add($"'{owner}' uses {type} auth, which Missive doesn't support; it was imported without auth.");
                return RequestAuth.None;
        }
    }

    private static RequestBody ImportBody(JsonObject? body, string owner, List<string> warnings)
    {
        if (body is null)
        {
            return RequestBody.None;
        }

        if (InterchangeText.MediaType(body.Str("mimeType")) == "application/graphql")
        {
            return new RequestBody(
                BodyType.Graphql,
                body.Str("text") ?? string.Empty,
                body.Str("variables") ?? string.Empty);
        }

        var mimeType = body.Str("mimeType");
        if (InterchangeText.IsJson(mimeType) && ImportGraphqlJson(body.Str("text")) is { } graphql)
        {
            return graphql;
        }

        if (body.Str("fileName") is { Length: > 0 } fileName)
        {
            return new RequestBody(BodyType.Binary, fileName);
        }

        var fields = body.Arr("params").Where(p => !p.Bool("disabled") && p.Str("name") is not null).ToList();

        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var field in fields)
        {
            if (field.Str("type") == "file")
            {
                var path = field.Str("value") ?? field.Str("fileName");
                if (string.IsNullOrWhiteSpace(path))
                {
                    warnings.Add($"Skipped the file field '{field.Str("name")}' of '{owner}': the import has no file path.");
                    continue;
                }

                var contentType = field.Str("contentType");
                pairs.Add(KeyValuePair.Create(
                    field.Str("name")!,
                    $"@{path}{(string.IsNullOrWhiteSpace(contentType) ? string.Empty : $";type={contentType}")}"));
            }
            else
            {
                pairs.Add(KeyValuePair.Create(field.Str("name")!, field.Str("value") ?? string.Empty));
            }
        }

        switch (InterchangeText.BodyTypeFor(mimeType))
        {
            case BodyType.UrlEncoded:
                return new RequestBody(BodyType.UrlEncoded, InterchangeText.UrlEncodedBody(pairs));
            case BodyType.Form:
                return new RequestBody(BodyType.Form, InterchangeText.FormBody(pairs));
            case var type:
                var text = body.Str("text") ?? string.Empty;
                if (text.Length == 0)
                {
                    if (body.Str("fileName") is { Length: > 0 })
                    {
                        warnings.Add($"'{owner}' sends a file as its body, which Missive doesn't support; it was imported without a body.");
                    }

                    return RequestBody.None;
                }

                return new RequestBody(type, text);
        }
    }

    private static RequestBody? ImportGraphqlJson(string? text)
    {
        try
        {
            if (JsonNode.Parse(text ?? string.Empty) is not JsonObject payload || payload.Str("query") is not { } query)
            {
                return null;
            }

            var variables = payload.Get("variables");
            var variablesText = variables is JsonValue value && value.TryGetValue<string>(out var stringValue)
                ? stringValue
                : variables?.ToJsonString() ?? string.Empty;
            return new RequestBody(BodyType.Graphql, query, variablesText);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary><c>{{ _.name }}</c> (Insomnia) → <c>{{name}}</c> (Missive).</summary>
    private static string FromInsomnia(string text) =>
        InsomniaVariable().Replace(text, match => "{{" + match.Groups["name"].Value + "}}");

    // ---- Export ----

    public static ExportOutput Export(RequestCollection collection)
    {
        var resources = new JsonArray();
        var workspaceId = Id("wrk", "workspace");
        resources.Add(new JsonObject
        {
            ["_id"] = workspaceId,
            ["_type"] = "workspace",
            ["parentId"] = null,
            ["name"] = collection.Root.Name,
            ["description"] = string.Empty,
            ["scope"] = "collection",
        });

        var baseId = Id("env", "base");
        resources.Add(Environment(baseId, workspaceId, "Base Environment", new JsonObject()));
        foreach (var environment in collection.Environments)
        {
            var data = new JsonObject();
            foreach (var variable in environment.Variables)
            {
                data[variable.Name] = variable.Secret ? string.Empty : ToInsomnia(variable.Value);
            }

            resources.Add(Environment(Id("env", "environment:" + environment.Name), baseId, environment.Name, data));
        }

        AddFolder(resources, collection.Root, workspaceId, string.Empty);

        var document = new JsonObject
        {
            ["_type"] = "export",
            ["__export_format"] = 4,
            ["__export_source"] = "missive",
            ["resources"] = resources,
        };

        var warnings = collection.Environments.Any(e => e.Variables.Any(v => v.Secret))
            ? new List<string> { "Secret variables were exported without their values." }
            : [];
        return new ExportOutput(InterchangeText.Write(document), [], warnings);
    }

    private static JsonObject Environment(string id, string parentId, string name, JsonObject data) => new()
    {
        ["_id"] = id,
        ["_type"] = "environment",
        ["parentId"] = parentId,
        ["name"] = name,
        ["data"] = data,
    };

    private static void AddFolder(JsonArray resources, CollectionFolder folder, string parentId, string path)
    {
        var order = 0;
        foreach (var child in folder.Folders)
        {
            var childPath = $"{path}/{child.Name}";
            var id = Id("fld", childPath);
            resources.Add(new JsonObject
            {
                ["_id"] = id,
                ["_type"] = "request_group",
                ["parentId"] = parentId,
                ["name"] = child.Name,
                ["metaSortKey"] = order++,
            });
            AddFolder(resources, child, id, childPath);
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var original in folder.Requests)
        {
            var request = InterchangeText.MapText(original, ToInsomnia);
            var key = $"{path}/{request.Name}";
            var repeat = seen[key] = seen.GetValueOrDefault(key) + 1;
            var resource = new JsonObject
            {
                ["_id"] = Id("req", repeat == 1 ? key : $"{key}#{repeat}"),
                ["_type"] = "request",
                ["parentId"] = parentId,
                ["name"] = request.Name,
                ["method"] = request.Method.ToName(),
                ["url"] = request.Url,
                ["parameters"] = Pairs(request.Params),
                ["headers"] = Pairs(request.Headers),
                ["authentication"] = ExportAuth(request.Auth),
                ["body"] = ExportBody(request.Body),
                ["metaSortKey"] = order++,
            };
            if (original.Description.Length > 0)
            {
                resource["description"] = original.Description;
            }

            resources.Add(resource);
        }
    }

    private static JsonArray Pairs(IEnumerable<KeyValueEntry> entries) =>
        new(entries
            .Select(e =>
            {
                var pair = new JsonObject { ["name"] = e.Key, ["value"] = e.Value };
                if (!e.Enabled)
                {
                    pair["disabled"] = true;
                }

                return (JsonNode)pair;
            })
            .ToArray());

    private static JsonObject ExportAuth(RequestAuth auth) => auth switch
    {
        BasicAuth basic => new JsonObject { ["type"] = "basic", ["username"] = basic.Username, ["password"] = basic.Password },
        BearerAuth bearer => new JsonObject { ["type"] = "bearer", ["token"] = bearer.Token },
        ApiKeyAuth apiKey => new JsonObject
        {
            ["type"] = "apikey",
            ["key"] = apiKey.Key,
            ["value"] = apiKey.Value,
            ["addTo"] = apiKey.In == ApiKeyLocation.Query ? "queryParams" : "header",
        },
        OAuth2Auth oauth => oauth.Grant == OAuth2Grant.ClientCredentials
            ? new JsonObject
            {
                ["type"] = "oauth2",
                ["grantType"] = "client_credentials",
                ["accessTokenUrl"] = oauth.TokenUrl,
                ["clientId"] = oauth.ClientId,
                ["clientSecret"] = oauth.ClientSecret,
                ["scope"] = oauth.Scope,
                ["audience"] = oauth.Audience,
                ["credentialsInBody"] = oauth.Credentials == OAuth2ClientAuthentication.Body,
            }
            : new JsonObject
            {
                ["type"] = "oauth2",
                ["grantType"] = "authorization_code",
                ["authorizationUrl"] = oauth.AuthUrl,
                ["accessTokenUrl"] = oauth.TokenUrl,
                ["redirectUrl"] = oauth.RedirectUri,
                ["clientId"] = oauth.ClientId,
                ["clientSecret"] = oauth.ClientSecret,
                ["scope"] = oauth.Scope,
                ["audience"] = oauth.Audience,
                ["credentialsInBody"] = oauth.Credentials == OAuth2ClientAuthentication.Body,
                ["usePkce"] = true,
                ["pkceMethod"] = "S256",
            },
        _ => new JsonObject(),
    };

    private static JsonObject ExportBody(RequestBody body)
    {
        JsonArray Fields(bool encoded) => new(InterchangeText.BodyFields(body.Content, encoded)
            .Select(f => (JsonNode)new JsonObject { ["name"] = f.Key, ["value"] = f.Value })
            .ToArray());

        return body.Type switch
        {
            BodyType.Json or BodyType.Text => new JsonObject
            {
                ["mimeType"] = InterchangeText.MediaTypeOf(body.Type),
                ["text"] = body.Content,
            },
            BodyType.Graphql => ExportGraphqlBody(body),
            BodyType.UrlEncoded => new JsonObject { ["mimeType"] = "application/x-www-form-urlencoded", ["params"] = Fields(encoded: true) },
            BodyType.Form => new JsonObject { ["mimeType"] = "multipart/form-data", ["params"] = Fields(encoded: false) },
            _ => new JsonObject(),
        };
    }

    private static JsonObject ExportGraphqlBody(RequestBody body)
    {
        JsonNode variables;
        try
        {
            variables = string.IsNullOrWhiteSpace(body.Variables)
                ? new JsonObject()
                : JsonNode.Parse(body.Variables) ?? new JsonObject();
        }
        catch (System.Text.Json.JsonException)
        {
            variables = JsonValue.Create(body.Variables)!;
        }

        var payload = new JsonObject
        {
            ["query"] = body.Content,
            ["variables"] = variables,
        };
        return new JsonObject
        {
            ["mimeType"] = "application/json",
            ["text"] = InterchangeText.Write(payload),
        };
    }

    /// <summary><c>{{name}}</c> (Missive) → <c>{{ _.name }}</c> (Insomnia).</summary>
    private static string ToInsomnia(string text) =>
        InterchangeText.RenameVariables(text, name => $" _.{name} ");

    /// <summary>A stable id: the same workspace exports with the same ids, so files diff cleanly.</summary>
    private static string Id(string prefix, string seed) =>
        $"{prefix}_{PostmanFormat.DeterministicId("insomnia:" + seed).Replace("-", string.Empty)}";

    [GeneratedRegex(@"\{\{\s*_\.(?<name>[^{}\s]+)\s*\}\}")]
    private static partial Regex InsomniaVariable();

    [GeneratedRegex(@"\{%.*?%\}")]
    private static partial Regex TemplateTag();
}
