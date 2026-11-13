using Dynypost.Application.Requests;
using Dynypost.Domain.Requests;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Dynypost.Infrastructure.Yaml;

/// <summary>
/// Reads and writes request files. Keys are always written in the same order
/// (<c>name</c>, <c>method</c>, <c>url</c>, <c>params</c>, <c>headers</c>, <c>auth</c>,
/// <c>body</c>, <c>capture</c>, <c>description</c>) and empty or default values are omitted. Disabled params and headers are written
/// with a <c>~</c> prefix on their name. A request without <c>auth</c> inherits the auth of its
/// folders; <c>type: none</c> sends none. Folder defaults (<c>_folder.yaml</c>) use the same
/// <c>headers</c> and <c>auth</c> keys, in that order.
/// </summary>
public static class RequestYamlSerializer
{
    public const string DisabledPrefix = HttpRequestDefinition.DisabledPrefix;

    public static string Serialize(HttpRequestDefinition request)
    {
        var writer = new YamlWriter()
            .Scalar("name", request.Name)
            .Scalar("method", request.Method.ToName())
            .Scalar("url", request.Url);

        WriteEntries(writer, "params", request.Params);
        WriteEntries(writer, "headers", request.Headers);
        WriteAuth(writer, request.Auth);

        if (request.Body.Type != BodyType.None)
        {
            writer.Mapping("body", body =>
            {
                body.Scalar("type", RequestMapper.BodyTypeName(request.Body.Type));
                if (request.Body.Type == BodyType.Graphql)
                {
                    if (request.Body.Content.Length > 0)
                    {
                        body.Scalar("query", request.Body.Content);
                    }

                    if (request.Body.Variables.Length > 0)
                    {
                        body.Scalar("variables", request.Body.Variables);
                    }
                }
                else if (request.Body.Fields is { Count: > 0 } fields)
                {
                    body.MappingSequence("fields", fields, WriteField);
                }
                else if (request.Body.Content.Length > 0)
                {
                    body.Scalar("content", request.Body.Content);
                }
            });
        }

        if (request.Captures.Count > 0)
        {
            writer.Mapping("capture", map =>
            {
                foreach (var capture in request.Captures)
                {
                    map.Scalar(capture.Variable, capture.Expression);
                }
            });
        }

        if (request.Description.Length > 0)
        {
            writer.Scalar("description", request.Description);
        }

        return writer.ToString();
    }

    /// <summary>Writes the defaults of a folder (<c>_folder.yaml</c>): <c>headers</c>, then <c>auth</c>.</summary>
    public static string SerializeFolder(FolderDefaults defaults)
    {
        var writer = new YamlWriter();
        WriteEntries(writer, "headers", defaults.Headers);
        WriteAuth(writer, defaults.Auth);
        return writer.ToString();
    }

    /// <summary>
    /// Parses a folder defaults file. Throws <see cref="RequestFormatException"/> when the text is
    /// not valid. An empty file sets nothing.
    /// </summary>
    public static FolderDefaults DeserializeFolder(string yaml)
    {
        var root = Load(yaml, allowEmpty: true);
        return root is null ? FolderDefaults.Empty : new FolderDefaults(ReadEntries(root, "headers"), ReadAuth(root));
    }

    /// <summary>
    /// Parses a request file. Throws <see cref="RequestFormatException"/> when the text is not a
    /// valid request. <paramref name="fallbackName"/> is used when the file has no <c>name</c>.
    /// </summary>
    public static HttpRequestDefinition Deserialize(string yaml, string fallbackName)
    {
        var root = Load(yaml, allowEmpty: false)!;
        var name = OptionalString(root, "name");
        var methodName = OptionalString(root, "method");
        var method = RequestMethod.Get;
        if (methodName is not null && !RequestMethods.TryParse(methodName, out method))
        {
            throw new RequestFormatException($"'{methodName}' is not a supported HTTP method.");
        }

        return new HttpRequestDefinition(
            string.IsNullOrWhiteSpace(name) ? fallbackName : name,
            method,
            OptionalString(root, "url") ?? string.Empty,
            ReadEntries(root, "params"),
            ReadEntries(root, "headers"),
            ReadAuth(root),
            ReadBody(root))
        {
            Captures = ReadCaptures(root),
            Description = OptionalString(root, "description") ?? string.Empty,
        };
    }

    private static YamlMappingNode? Load(string yaml, bool allowEmpty)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count == 0)
            {
                return allowEmpty ? null : throw new RequestFormatException("The file is empty.");
            }

            var node = stream.Documents[0].RootNode;
            if (allowEmpty && IsNull(node))
            {
                return null;
            }

            return node as YamlMappingNode
                ?? throw new RequestFormatException("The file must contain a YAML mapping (key: value pairs).");
        }
        catch (YamlException ex)
        {
            throw new RequestFormatException($"Invalid YAML at line {ex.Start.Line}: {ex.Message}");
        }
    }

    private static void WriteEntries(YamlWriter writer, string key, IReadOnlyList<KeyValueEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        writer.Mapping(key, map =>
        {
            foreach (var entry in entries)
            {
                map.Scalar(entry.Enabled ? entry.Key : DisabledPrefix + entry.Key, entry.Value);
            }
        });
    }

    /// <summary>
    /// Writes a body field: <c>name</c>, then <c>value</c> (text) or <c>file</c> (a path), then
    /// <c>fileName</c>, <c>contentType</c>, <c>encode</c> and <c>disabled</c> when they are set.
    /// </summary>
    private static void WriteField(YamlWriter writer, FormField field)
    {
        writer.Scalar("name", field.Name);
        if (field.IsFile)
        {
            writer.Scalar("file", field.Value);
            ScalarIfNotEmpty(writer, "fileName", field.FileName);
        }
        else
        {
            ScalarIfNotEmpty(writer, "value", field.Value);
        }

        ScalarIfNotEmpty(writer, "contentType", field.ContentType);
        if (field.Encode)
        {
            writer.Boolean("encode", true);
        }

        if (!field.Enabled)
        {
            writer.Boolean("disabled", true);
        }
    }

    private static void WriteAuth(YamlWriter writer, RequestAuth auth)
    {
        if (auth is InheritAuth)
        {
            return;
        }

        writer.Mapping("auth", map =>
        {
            switch (auth)
            {
                case NoAuth:
                    map.Scalar("type", "none");
                    break;
                case BasicAuth basic:
                    map.Scalar("type", "basic");
                    ScalarIfNotEmpty(map, "username", basic.Username);
                    ScalarIfNotEmpty(map, "password", basic.Password);
                    break;
                case BearerAuth bearer:
                    map.Scalar("type", "bearer");
                    ScalarIfNotEmpty(map, "token", bearer.Token);
                    break;
                case ApiKeyAuth apiKey:
                    map.Scalar("type", "apiKey");
                    ScalarIfNotEmpty(map, "key", apiKey.Key);
                    ScalarIfNotEmpty(map, "value", apiKey.Value);
                    if (apiKey.In == ApiKeyLocation.Query)
                    {
                        map.Scalar("in", "query");
                    }

                    break;
                case OAuth2Auth oauth:
                    // The grant is always written: it decides which of the other keys matter.
                    map.Scalar("type", "oauth2");
                    map.Scalar("grant", RequestMapper.GrantName(oauth.Grant));
                    ScalarIfNotEmpty(map, "authUrl", oauth.AuthUrl);
                    ScalarIfNotEmpty(map, "tokenUrl", oauth.TokenUrl);
                    ScalarIfNotEmpty(map, "redirectUri", oauth.RedirectUri);
                    ScalarIfNotEmpty(map, "clientId", oauth.ClientId);
                    ScalarIfNotEmpty(map, "clientSecret", oauth.ClientSecret);
                    ScalarIfNotEmpty(map, "scope", oauth.Scope);
                    ScalarIfNotEmpty(map, "audience", oauth.Audience);
                    if (oauth.Credentials == OAuth2ClientAuthentication.Body)
                    {
                        map.Scalar("credentials", "body");
                    }

                    break;
            }
        });
    }

    private static void ScalarIfNotEmpty(YamlWriter writer, string key, string value)
    {
        if (value.Length > 0)
        {
            writer.Scalar(key, value);
        }
    }

    private static List<KeyValueEntry> ReadEntries(YamlMappingNode root, string key)
    {
        var node = Optional(root, key);
        if (node is null || IsNull(node))
        {
            return [];
        }

        if (node is not YamlMappingNode map)
        {
            throw new RequestFormatException($"'{key}' must be a mapping of names to values.");
        }

        var entries = new List<KeyValueEntry>();
        foreach (var (keyNode, valueNode) in map.Children)
        {
            var name = ScalarText(keyNode, key);
            var enabled = !name.StartsWith(DisabledPrefix, StringComparison.Ordinal);
            entries.Add(new KeyValueEntry(enabled ? name : name[DisabledPrefix.Length..], ScalarText(valueNode, $"{key}.{name}"), enabled));
        }

        return entries;
    }

    private static List<CaptureRule> ReadCaptures(YamlMappingNode root)
    {
        var node = Optional(root, "capture");
        if (node is null || IsNull(node))
        {
            return [];
        }

        if (node is not YamlMappingNode map)
        {
            throw new RequestFormatException("'capture' must be a mapping of variable names to JSONPaths or headers.");
        }

        return map.Children
            .Select(pair =>
            {
                var variable = ScalarText(pair.Key, "capture");
                return new CaptureRule(variable, ScalarText(pair.Value, $"capture.{variable}"));
            })
            .ToList();
    }

    private static RequestAuth ReadAuth(YamlMappingNode root)
    {
        var node = Optional(root, "auth");
        if (node is null || IsNull(node))
        {
            return RequestAuth.Inherit;
        }

        if (node is not YamlMappingNode map)
        {
            throw new RequestFormatException("'auth' must be a mapping.");
        }

        var type = OptionalString(map, "type") ?? "inherit";
        string Field(string name) => OptionalString(map, name) ?? string.Empty;

        return type.ToLowerInvariant() switch
        {
            "inherit" => RequestAuth.Inherit,
            "none" => RequestAuth.None,
            "basic" => new BasicAuth(Field("username"), Field("password")),
            "bearer" => new BearerAuth(Field("token")),
            "apikey" => new ApiKeyAuth(Field("key"), Field("value"), Field("in").ToLowerInvariant() switch
            {
                "" or "header" => ApiKeyLocation.Header,
                "query" => ApiKeyLocation.Query,
                var other => throw new RequestFormatException($"'{other}' is not a supported API key location."),
            }),
            "oauth2" => ReadOAuth2(Field),
            _ => throw new RequestFormatException($"'{type}' is not a supported auth type."),
        };
    }

    private static OAuth2Auth ReadOAuth2(Func<string, string> field)
    {
        var grantName = field("grant");
        var grant = OAuth2Grant.ClientCredentials;
        if (grantName.Length > 0 && !RequestMapper.TryParseGrant(grantName, out grant))
        {
            throw new RequestFormatException($"'{grantName}' is not a supported OAuth 2.0 grant (use clientCredentials or authorizationCode).");
        }

        var credentials = field("credentials").ToLowerInvariant() switch
        {
            "" or "header" => OAuth2ClientAuthentication.Header,
            "body" => OAuth2ClientAuthentication.Body,
            var other => throw new RequestFormatException($"'{other}' is not a supported way to send the client credentials (use header or body)."),
        };

        return new OAuth2Auth(
            grant,
            field("tokenUrl"),
            field("clientId"),
            field("clientSecret"),
            field("authUrl"),
            field("redirectUri"),
            field("scope"),
            field("audience"),
            credentials);
    }

    private static RequestBody ReadBody(YamlMappingNode root)
    {
        var node = Optional(root, "body");
        if (node is null || IsNull(node))
        {
            return RequestBody.None;
        }

        if (node is not YamlMappingNode map)
        {
            throw new RequestFormatException("'body' must be a mapping with 'type' and 'content'.");
        }

        var typeName = OptionalString(map, "type") ?? "text";
        if (!RequestMapper.TryParseBodyType(typeName, out var type))
        {
            throw new RequestFormatException($"'{typeName}' is not a supported body type.");
        }

        return type switch
        {
            BodyType.None => RequestBody.None,
            BodyType.Graphql => new RequestBody(
                type,
                OptionalString(map, "query") ?? OptionalString(map, "content") ?? string.Empty,
                OptionalString(map, "variables") ?? string.Empty),
            BodyType.Form or BodyType.UrlEncoded when Optional(map, "fields") is { } fields && !IsNull(fields) =>
                RequestBody.WithFields(type, ReadFields(fields)),
            _ => new RequestBody(type, OptionalString(map, "content") ?? string.Empty),
        };
    }

    private static IEnumerable<FormField> ReadFields(YamlNode node)
    {
        if (node is not YamlSequenceNode sequence)
        {
            throw new RequestFormatException("'body.fields' must be a list of fields.");
        }

        foreach (var item in sequence.Children)
        {
            if (item is not YamlMappingNode field)
            {
                throw new RequestFormatException("Each of 'body.fields' must be a mapping with a 'name'.");
            }

            var name = OptionalString(field, "name") ?? string.Empty;
            var file = OptionalString(field, "file");
            if (file is null && Optional(field, "file") is { } fileNode && IsNull(fileNode))
            {
                file = string.Empty;
            }

            yield return new FormField(
                name,
                file ?? OptionalString(field, "value") ?? string.Empty,
                file is null ? FormFieldKind.Text : FormFieldKind.File,
                !OptionalBoolean(field, "disabled", $"body.fields.{name}"),
                OptionalString(field, "contentType") ?? string.Empty,
                file is null ? string.Empty : OptionalString(field, "fileName") ?? string.Empty,
                OptionalBoolean(field, "encode", $"body.fields.{name}"));
        }
    }

    private static bool OptionalBoolean(YamlMappingNode map, string key, string owner) =>
        OptionalString(map, key)?.ToLowerInvariant() switch
        {
            null or "" or "false" => false,
            "true" => true,
            var other => throw new RequestFormatException($"'{owner}.{key}' must be true or false, not '{other}'."),
        };

    private static YamlNode? Optional(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node : null;

    private static string? OptionalString(YamlMappingNode map, string key)
    {
        var node = Optional(map, key);
        return node is null || IsNull(node) ? null : ScalarText(node, key);
    }

    private static bool IsNull(YamlNode node) =>
        node is YamlScalarNode { Style: ScalarStyle.Plain or ScalarStyle.Any } scalar
        && scalar.Tag.IsEmpty
        && scalar.Value is null or "" or "~" or "null" or "Null" or "NULL";

    private static string ScalarText(YamlNode node, string key) => node switch
    {
        YamlScalarNode scalar when IsNull(scalar) => string.Empty,
        YamlScalarNode scalar => scalar.Value ?? string.Empty,
        _ => throw new RequestFormatException($"'{key}' must be a single value."),
    };
}

/// <summary>A request file whose content is not a valid request.</summary>
public sealed class RequestFormatException(string message) : Exception(message);
