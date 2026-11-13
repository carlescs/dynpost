using System.Globalization;

namespace Dynpost.Domain.Requests;

/// <summary>
/// Stores a value from a successful response in a variable, e.g. <c>token: $.access_token</c>.
/// Captures are declarative: a JSONPath into the JSON body or a response header, nothing more.
/// </summary>
/// <param name="Variable">The variable the value is stored in.</param>
/// <param name="Expression">Where the value comes from: <c>$.path</c> or <c>header:Name</c>.</param>
public sealed record CaptureRule(string Variable, string Expression);

/// <summary>Where a captured value comes from.</summary>
public closed record CaptureSource;

/// <summary>A response header, by name (case-insensitive); the first one when it repeats.</summary>
public sealed record HeaderCaptureSource(string Name) : CaptureSource;

/// <summary>A value in the JSON body, reached by following <see cref="Path"/> from the root.</summary>
public sealed record JsonCaptureSource(IReadOnlyList<JsonPathStep> Path) : CaptureSource
{
    public bool Equals(JsonCaptureSource? other) => other is not null && Path.SequenceEqual(other.Path);

    public override int GetHashCode() => Path.Count;
}

/// <summary>One step of a JSONPath: an object property, or an array index when <see cref="Index"/> is set.</summary>
public sealed record JsonPathStep(string? Property, int? Index = null)
{
    public static JsonPathStep Member(string name) => new(name);

    public static JsonPathStep At(int index) => new(null, index);
}

/// <summary>Parses capture expressions.</summary>
public static class CaptureExpression
{
    public const string HeaderPrefix = "header:";

    /// <summary>
    /// Parses <c>header:Name</c>, or a JSONPath made of <c>$</c> followed by <c>.name</c>,
    /// <c>['name']</c> (or <c>["name"]</c>) and <c>[index]</c> steps. Wildcards, filters and
    /// recursive descent are not supported: a capture names exactly one value.
    /// </summary>
    public static bool TryParse(string? expression, out CaptureSource source, out string error)
    {
        source = null!;
        var text = (expression ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            error = "Enter where to capture the value from, such as $.token or header:ETag.";
            return false;
        }

        if (text.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = text[HeaderPrefix.Length..].Trim();
            if (name.Length == 0 || name.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == ':'))
            {
                error = $"'{text}' doesn't name a header; write header:Name, such as header:ETag.";
                return false;
            }

            source = new HeaderCaptureSource(name);
            error = string.Empty;
            return true;
        }

        if (text[0] != '$')
        {
            error = $"'{text}' is neither a JSONPath (starting with $) nor a header (header:Name).";
            return false;
        }

        var steps = new List<JsonPathStep>();
        var i = 1;
        while (i < text.Length)
        {
            if (text[i] == '.')
            {
                var start = ++i;
                while (i < text.Length && text[i] is not ('.' or '['))
                {
                    i++;
                }

                var name = text[start..i];
                if (name.Length == 0 || name == "*" || name.Any(c => char.IsWhiteSpace(c) || c is ']' or '\'' or '"'))
                {
                    return Invalid(text, out error);
                }

                steps.Add(JsonPathStep.Member(name));
            }
            else if (text[i] == '[')
            {
                var start = i + 1;
                if (start < text.Length && text[start] is '\'' or '"')
                {
                    // A quoted name may contain anything but its quote, ']' included.
                    var quote = text[start];
                    var end = text.IndexOf(quote, start + 1);
                    if (end < 0 || end + 1 >= text.Length || text[end + 1] != ']')
                    {
                        return Invalid(text, out error);
                    }

                    steps.Add(JsonPathStep.Member(text[(start + 1)..end]));
                    i = end + 2;
                }
                else
                {
                    var close = text.IndexOf(']', start);
                    if (close < 0
                        || !int.TryParse(text[start..close], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                    {
                        return Invalid(text, out error);
                    }

                    steps.Add(JsonPathStep.At(index));
                    i = close + 1;
                }
            }
            else
            {
                return Invalid(text, out error);
            }
        }

        source = new JsonCaptureSource(steps);
        error = string.Empty;
        return true;
    }

    private static bool Invalid(string text, out string error)
    {
        error = $"'{text}' is not a JSONPath Dynpost supports: use $ followed by .name, ['name'] or [index] steps, such as $.data.items[0].id.";
        return false;
    }
}
