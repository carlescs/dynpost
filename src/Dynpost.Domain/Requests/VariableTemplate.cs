using System.Text.RegularExpressions;

namespace Dynpost.Domain.Requests;

/// <summary>Replaces <c>{{variable}}</c> references in request values.</summary>
public static partial class VariableTemplate
{
    /// <summary>
    /// Replaces each <c>{{name}}</c> (spaces inside the braces are allowed, and a built-in <c>{{$name arg …}}</c> may have arguments) with its value.
    /// Names without a value are left as written and added to <paramref name="missing"/>.
    /// </summary>
    public static string Resolve(string text, IReadOnlyDictionary<string, string> variables, ISet<string> missing) =>
        Replace(text, name =>
        {
            if (variables.TryGetValue(name, out var value))
            {
                return value;
            }

            missing.Add(name);
            return null;
        });

    /// <summary>
    /// Replaces each <c>{{name}}</c> with what <paramref name="valueOf"/> returns for the name;
    /// a reference is left as written when it returns <c>null</c>.
    /// </summary>
    public static string Replace(string text, Func<string, string?> valueOf)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{", StringComparison.Ordinal))
        {
            return text;
        }

        return Reference().Replace(text, match => valueOf(match.Groups["name"].Value) ?? match.Value);
    }

    /// <summary>The names referenced in <paramref name="text"/>, in order, possibly repeated.</summary>
    public static IEnumerable<string> References(string text) =>
        string.IsNullOrEmpty(text)
            ? []
            : Reference().Matches(text).Select(match => match.Groups["name"].Value);

    /// <summary>Whether <paramref name="name"/> can be referenced as <c>{{name}}</c>.</summary>
    public static bool IsValidName(string name) => Name().IsMatch(name);

    // A built-in variable ($name) may take arguments separated by spaces: {{$randomInt 1 100}}.
    [GeneratedRegex(@"\{\{\s*(?<name>\$[^{}\s]+(?:[ \t]+[^{}\s]+)*|[^{}\s]+)\s*\}\}")]
    private static partial Regex Reference();

    [GeneratedRegex(@"^[^{}\s]+$")]
    private static partial Regex Name();
}
