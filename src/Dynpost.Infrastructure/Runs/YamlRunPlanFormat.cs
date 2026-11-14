using Dynpost.Application.Common;
using Dynpost.Application.Runs;
using Dynpost.Domain.Runs;
using Dynpost.Domain.Workspaces;
using Dynpost.Infrastructure.Yaml;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Dynpost.Infrastructure.Runs;

/// <summary>
/// Reads run plan files. Keys, in order: <c>name</c>, <c>requests</c>, <c>disabled</c> (request
/// paths kept in the list but not sent), <c>order</c>
/// (<c>sequential</c>, <c>shuffle</c>, <c>random</c> or <c>roundRobin</c>), <c>weights</c> (per
/// request path), <c>environments</c> (environment files the run uses instead of the selected
/// one), <c>environmentOrder</c> (like <c>order</c>), <c>environmentWeights</c> (per environment
/// path), <c>load</c>
/// (<c>threads</c>, <c>iterations</c> or <c>duration</c>, <c>rampUp</c>, <c>thinkTime</c>,
/// <c>maxRps</c>), <c>data</c> (<c>file</c>, <c>mode</c>, <c>onEof</c>), <c>bodies</c> (per
/// request path: <c>folder</c>, <c>pattern</c>, <c>mode</c>, <c>onEof</c>), <c>saveResponses</c>
/// (<c>true</c> to keep each logged response body in a file), <c>assert</c>
/// (per request path, or <c>"*"</c> for all: <c>status</c>, <c>maxTime</c>, <c>bodyContains</c>,
/// <c>bodyNotContains</c>) and <c>thresholds</c> (<c>p50</c>, <c>p90</c>, <c>p95</c>, <c>p99</c>,
/// <c>average</c>, <c>errorRate</c>, <c>throughput</c>).
/// </summary>
public sealed partial class YamlRunPlanFormat : IRunPlanFormat
{
    public string RewriteRequestPaths(string text, IReadOnlyDictionary<string, string> moves)
    {
        YamlMappingNode root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode mapping)
            {
                return text;
            }

            root = mapping;
        }
        catch (YamlException)
        {
            return text;
        }

        var edits = new List<(int Start, int End, string Replacement)>();
        foreach (var (key, value) in root.Children)
        {
            IEnumerable<YamlNode> references = (key as YamlScalarNode)?.Value switch
            {
                "requests" or "disabled" when value is YamlSequenceNode list => list.Children,
                "weights" or "bodies" or "assert" when value is YamlMappingNode map => map.Children.Keys,
                _ => [],
            };

            foreach (var node in references)
            {
                if (node is YamlScalarNode { Value: { } item } scalar
                    && WorkspacePath.TryParse(item, out var path)
                    && moves.TryGetValue(path.Value, out var to)
                    && Quote(scalar.Style, to) is { } replacement)
                {
                    edits.Add(((int)scalar.Start.Index, (int)scalar.End.Index, replacement));
                }
            }
        }

        // From the end, so earlier positions stay valid.
        var builder = new StringBuilder(text);
        foreach (var (start, end, replacement) in edits.OrderByDescending(e => e.Start))
        {
            builder.Remove(start, end - start).Insert(start, replacement);
        }

        return builder.ToString();
    }

    /// <summary>A path written in the same style as the scalar it replaces; <c>null</c> for block scalars, which are left alone.</summary>
    private static string? Quote(ScalarStyle style, string value) => style switch
    {
        ScalarStyle.Plain when PlainPath().IsMatch(value) => value,
        ScalarStyle.Plain or ScalarStyle.SingleQuoted => $"'{value.Replace("'", "''")}'",
        ScalarStyle.DoubleQuoted => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
        _ => null,
    };

    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_./-]*$")]
    private static partial Regex PlainPath();

    public Result<RunPlan> Parse(string text)
    {
        try
        {
            return ParseMapping(YamlNodes.LoadMapping(text));
        }
        catch (WorkspaceFileFormatException ex)
        {
            return new Error(ErrorCodes.InvalidFile, $"Not a valid run plan. {ex.Message}");
        }
    }

    private static RunPlan ParseMapping(YamlMappingNode root)
    {
        var requests = new List<WorkspacePath>();
        foreach (var item in YamlNodes.StringList(root, "requests"))
        {
            if (!WorkspacePath.TryParse(item, out var path) || path.IsRoot)
            {
                throw new WorkspaceFileFormatException($"'{item}' in 'requests' is not a valid workspace path.");
            }

            requests.Add(path);
        }

        return new RunPlan(
            YamlNodes.OptionalString(root, "name") ?? string.Empty,
            requests,
            ParseLoad(YamlNodes.OptionalMapping(root, "load")),
            ParseData(YamlNodes.OptionalMapping(root, "data")),
            ParseAssertions(YamlNodes.OptionalMapping(root, "assert")),
            ParseThresholds(YamlNodes.OptionalMapping(root, "thresholds")),
            ParseOrder(YamlNodes.OptionalString(root, "order")),
            ParseWeights(YamlNodes.OptionalMapping(root, "weights")),
            ParseDisabled(root),
            ParseBodies(YamlNodes.OptionalMapping(root, "bodies")),
            YamlNodes.OptionalBoolean(root, "saveResponses"),
            ParseEnvironments(root),
            ParseOrder(YamlNodes.OptionalString(root, "environmentOrder"), "environmentOrder"),
            ParseWeights(YamlNodes.OptionalMapping(root, "environmentWeights"), "environmentWeights"));
    }

    private static List<WorkspacePath> ParseEnvironments(YamlMappingNode root)
    {
        var environments = new List<WorkspacePath>();
        foreach (var item in YamlNodes.StringList(root, "environments"))
        {
            if (!WorkspacePath.TryParse(item.Trim(), out var path) || path.IsRoot)
            {
                throw new WorkspaceFileFormatException($"'{item}' in 'environments' is not a valid workspace path.");
            }

            environments.Add(path);
        }

        return environments;
    }

    private static List<string> ParseDisabled(YamlMappingNode root) =>
        YamlNodes.StringList(root, "disabled")
            .Select(item => WorkspacePath.TryParse(item, out var path) ? path.Value : item)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static Dictionary<string, RunBodySource> ParseBodies(YamlMappingNode? bodies)
    {
        var result = new Dictionary<string, RunBodySource>(StringComparer.Ordinal);
        if (bodies is null)
        {
            return result;
        }

        foreach (var (keyNode, valueNode) in bodies.Children)
        {
            var key = YamlNodes.ScalarText(keyNode, "bodies");
            var path = WorkspacePath.TryParse(key, out var parsed) ? parsed.Value : key;
            var where = $"bodies.{key}";
            if (valueNode is not YamlMappingNode map)
            {
                throw new WorkspaceFileFormatException($"'{where}' must be a mapping with a 'folder'.");
            }

            var folderText = YamlNodes.OptionalString(map, "folder");
            if (string.IsNullOrWhiteSpace(folderText) || !WorkspacePath.TryParse(folderText, out var folder))
            {
                throw new WorkspaceFileFormatException($"'{where}.folder' must be a workspace path to a folder.");
            }

            var pattern = YamlNodes.OptionalString(map, "pattern");
            if (pattern is not null && (pattern.Trim().Length == 0 || pattern.IndexOfAny(['/', '\\']) >= 0))
            {
                throw new WorkspaceFileFormatException($"'{where}.pattern' must be a file name pattern such as *.json.");
            }

            result[path] = new RunBodySource(
                folder,
                pattern?.Trim(),
                ParseMode(YamlNodes.OptionalString(map, "mode"), $"{where}.mode"),
                ParseOnEof(YamlNodes.OptionalString(map, "onEof"), $"{where}.onEof"));
        }

        return result;
    }

    private static SelectionMode ParseMode(string? text, string where) => text switch
    {
        null or "sequential" => SelectionMode.Sequential,
        "random" => SelectionMode.Random,
        "perThread" => SelectionMode.PerThread,
        var other => throw new WorkspaceFileFormatException($"'{where}' must be sequential, random or perThread, not '{other}'."),
    };

    private static EndBehavior ParseOnEof(string? text, string where) => text switch
    {
        null or "recycle" => EndBehavior.Recycle,
        "stop" => EndBehavior.Stop,
        var other => throw new WorkspaceFileFormatException($"'{where}' must be recycle or stop, not '{other}'."),
    };

    private static RequestOrder ParseOrder(string? text, string where = "order") => text switch
    {
        null or "sequential" => RequestOrder.Sequential,
        "shuffle" => RequestOrder.Shuffle,
        "random" => RequestOrder.Random,
        "roundRobin" => RequestOrder.RoundRobin,
        var other => throw new WorkspaceFileFormatException($"'{where}' must be sequential, shuffle, random or roundRobin, not '{other}'."),
    };

    private static Dictionary<string, double> ParseWeights(YamlMappingNode? weights, string where = "weights")
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        if (weights is null)
        {
            return result;
        }

        foreach (var (keyNode, _) in weights.Children)
        {
            var key = YamlNodes.ScalarText(keyNode, where);
            var path = WorkspacePath.TryParse(key, out var parsed) ? parsed.Value : key;
            result[path] = Number(weights, key, $"{where}.{key}") ?? throw new WorkspaceFileFormatException($"'{where}.{key}' must be a number.");
        }

        return result;
    }

    private static RunLoad ParseLoad(YamlMappingNode? load)
    {
        if (load is null)
        {
            throw new WorkspaceFileFormatException("'load' is required: set 'threads' and 'iterations' or 'duration'.");
        }

        var think = YamlNodes.Optional(load, "thinkTime");
        TimeSpan thinkMin = TimeSpan.Zero, thinkMax = TimeSpan.Zero;
        if (think is YamlMappingNode range)
        {
            thinkMin = Duration(range, "min", "load.thinkTime.min") ?? TimeSpan.Zero;
            thinkMax = Duration(range, "max", "load.thinkTime.max") ?? thinkMin;
        }
        else if (think is not null)
        {
            thinkMin = thinkMax = ParseDuration(YamlNodes.ScalarText(think, "load.thinkTime"), "load.thinkTime");
        }

        return new RunLoad(
            Integer(load, "threads", "load.threads") ?? 1,
            Integer(load, "iterations", "load.iterations"),
            Duration(load, "duration", "load.duration"),
            Duration(load, "rampUp", "load.rampUp") ?? TimeSpan.Zero,
            thinkMin,
            thinkMax,
            Number(load, "maxRps", "load.maxRps"));
    }

    private static RunDataSource? ParseData(YamlMappingNode? data)
    {
        if (data is null)
        {
            return null;
        }

        var file = YamlNodes.OptionalString(data, "file");
        if (string.IsNullOrWhiteSpace(file) || !WorkspacePath.TryParse(file, out var path) || path.IsRoot)
        {
            throw new WorkspaceFileFormatException("'data.file' must be a workspace path to a CSV file.");
        }

        return new RunDataSource(
            path,
            ParseMode(YamlNodes.OptionalString(data, "mode"), "data.mode"),
            ParseOnEof(YamlNodes.OptionalString(data, "onEof"), "data.onEof"));
    }

    private static Dictionary<string, RunAssertion> ParseAssertions(YamlMappingNode? assert)
    {
        var result = new Dictionary<string, RunAssertion>(StringComparer.Ordinal);
        if (assert is null)
        {
            return result;
        }

        foreach (var (keyNode, valueNode) in assert.Children)
        {
            var key = YamlNodes.ScalarText(keyNode, "assert");
            var path = key == RunPlan.AllRequests ? key : WorkspacePath.TryParse(key, out var parsed) ? parsed.Value : key;
            var where = $"assert.{key}";
            if (valueNode is not YamlMappingNode map)
            {
                throw new WorkspaceFileFormatException($"'{where}' must be a mapping of checks.");
            }

            var status = new List<string>();
            switch (YamlNodes.Optional(map, "status"))
            {
                case null:
                    break;
                case YamlSequenceNode sequence:
                    status.AddRange(sequence.Children.Select(child => YamlNodes.ScalarText(child, $"{where}.status")));
                    break;
                case var scalar:
                    status.AddRange(YamlNodes.ScalarText(scalar, $"{where}.status").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                    break;
            }

            foreach (var pattern in status.Where(p => !RunText.IsValidStatusPattern(p)))
            {
                throw new WorkspaceFileFormatException($"'{where}.status' has '{pattern}': use a status code such as 201 or a class such as 2xx.");
            }

            result[path] = new RunAssertion(
                status.Select(p => p.Trim()).ToList(),
                Duration(map, "maxTime", $"{where}.maxTime"),
                YamlNodes.OptionalString(map, "bodyContains"),
                YamlNodes.OptionalString(map, "bodyNotContains"));
        }

        return result;
    }

    private static RunThresholds ParseThresholds(YamlMappingNode? map)
    {
        if (map is null)
        {
            return RunThresholds.None;
        }

        double? errorRate = null;
        if (YamlNodes.OptionalString(map, "errorRate") is { } text)
        {
            errorRate = RunText.TryParsePercent(text, out var percent)
                ? percent
                : throw new WorkspaceFileFormatException("'thresholds.errorRate' must be a percentage between 0% and 100%.");
        }

        return new RunThresholds(
            Duration(map, "p50", "thresholds.p50"),
            Duration(map, "p90", "thresholds.p90"),
            Duration(map, "p95", "thresholds.p95"),
            Duration(map, "p99", "thresholds.p99"),
            Duration(map, "average", "thresholds.average"),
            errorRate,
            Number(map, "throughput", "thresholds.throughput"));
    }

    private static TimeSpan? Duration(YamlMappingNode map, string key, string where) =>
        YamlNodes.OptionalString(map, key) is { } text ? ParseDuration(text, where) : null;

    private static TimeSpan ParseDuration(string text, string where) =>
        RunText.TryParseDuration(text, out var value)
            ? value
            : throw new WorkspaceFileFormatException($"'{where}' must be a duration such as 500ms, 10s, 2m or 1h.");

    private static int? Integer(YamlMappingNode map, string key, string where) =>
        YamlNodes.OptionalString(map, key) is { } text
            ? int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new WorkspaceFileFormatException($"'{where}' must be a whole number.")
            : null;

    private static double? Number(YamlMappingNode map, string key, string where) =>
        YamlNodes.OptionalString(map, key) is { } text
            ? double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
                && double.IsFinite(value)
                ? value
                : throw new WorkspaceFileFormatException($"'{where}' must be a number.")
            : null;
}
