using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Dynpost.Infrastructure.Interchange;

/// <summary>
/// Reads an import file as a JSON tree, whether it is written in JSON or in YAML (OpenAPI
/// descriptions often are), so each format reads one kind of tree.
/// </summary>
internal static partial class InterchangeDocument
{
    /// <summary>Guards against YAML alias "bombs" that expand to huge trees.</summary>
    private const int MaxNodes = 2_000_000;

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 256,
    };

    /// <summary>The tree of <paramref name="text"/>, or why it is neither JSON nor YAML.</summary>
    public static JsonNode? Parse(string text, out string? error)
    {
        error = null;
        var trimmed = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                return JsonNode.Parse(trimmed, documentOptions: JsonOptions);
            }
            catch (JsonException ex)
            {
                error = $"The file is not valid JSON: {ex.Message}";
                return null;
            }
        }

        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(trimmed));
            if (stream.Documents.Count == 0)
            {
                error = "The file is empty.";
                return null;
            }

            var count = 0;
            return FromYaml(stream.Documents[0].RootNode, ref count, 0);
        }
        catch (YamlException ex)
        {
            error = $"The file is neither JSON nor valid YAML: {ex.Message}";
            return null;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static JsonNode? FromYaml(YamlNode node, ref int count, int depth)
    {
        if (++count > MaxNodes || depth > 256)
        {
            throw new InvalidOperationException("The file is too large or too deeply nested to import.");
        }

        switch (node)
        {
            case YamlMappingNode mapping:
                var obj = new JsonObject();
                foreach (var (key, value) in mapping.Children)
                {
                    var name = key is YamlScalarNode scalar ? scalar.Value ?? string.Empty : key.ToString();
                    obj[name] = FromYaml(value, ref count, depth + 1);
                }

                return obj;

            case YamlSequenceNode sequence:
                var array = new JsonArray();
                foreach (var item in sequence.Children)
                {
                    array.Add(FromYaml(item, ref count, depth + 1));
                }

                return array;

            case YamlScalarNode scalar:
                return Scalar(scalar);

            default:
                return null;
        }
    }

    /// <summary>Plain scalars get YAML's core types (null, booleans, numbers); quoted ones stay text.</summary>
    private static JsonNode? Scalar(YamlScalarNode scalar)
    {
        var value = scalar.Value ?? string.Empty;
        if (scalar.Style != ScalarStyle.Plain)
        {
            return JsonValue.Create(value);
        }

        if (value is "" or "~" or "null" or "Null" or "NULL")
        {
            return null;
        }

        if (value is "true" or "True" or "TRUE")
        {
            return JsonValue.Create(true);
        }

        if (value is "false" or "False" or "FALSE")
        {
            return JsonValue.Create(false);
        }

        if (Integer().IsMatch(value) && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }

        if (Float().IsMatch(value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return JsonValue.Create(number);
        }

        return JsonValue.Create(value);
    }

    [GeneratedRegex(@"^[-+]?[0-9]+$")]
    private static partial Regex Integer();

    [GeneratedRegex(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$")]
    private static partial Regex Float();
}

/// <summary>Lenient accessors for reading other apps' files, where any field may be missing or of another type.</summary>
internal static class JsonNodes
{
    public static JsonNode? Get(this JsonNode? node, string key) =>
        node is JsonObject obj && obj.TryGetPropertyValue(key, out var value) ? value : null;

    public static JsonObject? Obj(this JsonNode? node, string key) => node.Get(key) as JsonObject;

    public static IEnumerable<JsonNode> Arr(this JsonNode? node, string key) =>
        node.Get(key) is JsonArray array ? array.Where(item => item is not null)!.Cast<JsonNode>() : [];

    /// <summary>A scalar field as text (numbers and booleans too); <c>null</c> when missing or not a scalar.</summary>
    public static string? Str(this JsonNode? node, string key) => node.Get(key).Text();

    public static string? Text(this JsonNode? node) =>
        node is JsonValue value
            ? value.GetValueKind() switch
            {
                JsonValueKind.String => value.GetValue<string>(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => value.ToJsonString(),
                _ => null,
            }
            : null;

    /// <summary>A field as text; objects and arrays are written as JSON.</summary>
    public static string AnyText(this JsonNode? node) =>
        node switch
        {
            null => string.Empty,
            JsonValue => node.Text() ?? string.Empty,
            _ => node.ToJsonString(),
        };

    public static bool Bool(this JsonNode? node, string key) =>
        node.Get(key) is JsonValue value && value.GetValueKind() == JsonValueKind.True;
}
