using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Dynypost.Infrastructure.Yaml;

/// <summary>A workspace file whose content doesn't have the expected shape.</summary>
public sealed class WorkspaceFileFormatException(string message) : Exception(message);

/// <summary>Reads Dynypost's small YAML files. Failures throw <see cref="WorkspaceFileFormatException"/>.</summary>
internal static class YamlNodes
{
    /// <summary>Parses a file whose root is a mapping; an empty file is an empty mapping.</summary>
    public static YamlMappingNode LoadMapping(string yaml)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count == 0 || IsNull(stream.Documents[0].RootNode))
            {
                return new YamlMappingNode();
            }

            return stream.Documents[0].RootNode as YamlMappingNode
                ?? throw new WorkspaceFileFormatException("The file must contain a YAML mapping (key: value pairs).");
        }
        catch (YamlException ex)
        {
            throw new WorkspaceFileFormatException($"Invalid YAML at line {ex.Start.Line}: {ex.Message}");
        }
    }

    public static YamlNode? Optional(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var node) && !IsNull(node) ? node : null;

    public static string? OptionalString(YamlMappingNode map, string key) =>
        Optional(map, key) is { } node ? ScalarText(node, key) : null;

    /// <summary>An optional boolean (<c>true</c> or <c>false</c>); a missing one is <paramref name="fallback"/>.</summary>
    public static bool OptionalBoolean(YamlMappingNode map, string key, bool fallback = false) => OptionalString(map, key) switch
    {
        null => fallback,
        var text when text.Equals("true", StringComparison.OrdinalIgnoreCase) => true,
        var text when text.Equals("false", StringComparison.OrdinalIgnoreCase) => false,
        _ => throw new WorkspaceFileFormatException($"'{key}' must be true or false."),
    };

    /// <summary>An optional mapping; a missing or empty one is <c>null</c>.</summary>
    public static YamlMappingNode? OptionalMapping(YamlMappingNode map, string key) => Optional(map, key) switch
    {
        null => null,
        YamlMappingNode mapping => mapping,
        _ => throw new WorkspaceFileFormatException($"'{key}' must be a mapping of names to values."),
    };

    /// <summary>The pairs of a mapping of names to single values, in order.</summary>
    public static IEnumerable<KeyValuePair<string, string>> StringPairs(YamlMappingNode map, string key) =>
        map.Children.Select(pair =>
        {
            var name = ScalarText(pair.Key, key);
            return KeyValuePair.Create(name, ScalarText(pair.Value, $"{key}.{name}"));
        });

    /// <summary>An optional list of single values; a missing or empty one is empty.</summary>
    public static IReadOnlyList<string> StringList(YamlMappingNode map, string key) => Optional(map, key) switch
    {
        null => [],
        YamlSequenceNode sequence => sequence.Children.Select(item => ScalarText(item, key)).ToList(),
        _ => throw new WorkspaceFileFormatException($"'{key}' must be a list."),
    };

    public static bool IsNull(YamlNode node) =>
        node is YamlScalarNode { Style: ScalarStyle.Plain or ScalarStyle.Any } scalar
        && scalar.Tag.IsEmpty
        && scalar.Value is null or "" or "~" or "null" or "Null" or "NULL";

    public static string ScalarText(YamlNode node, string key) => node switch
    {
        YamlScalarNode scalar when IsNull(scalar) => string.Empty,
        YamlScalarNode scalar => scalar.Value ?? string.Empty,
        _ => throw new WorkspaceFileFormatException($"'{key}' must be a single value."),
    };
}
