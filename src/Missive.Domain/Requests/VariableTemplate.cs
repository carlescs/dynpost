using System.Text.RegularExpressions;

namespace Missive.Domain.Requests;

/// <summary>Replaces <c>{{variable}}</c> references in request values.</summary>
public static partial class VariableTemplate
{
    /// <summary>
    /// Replaces each <c>{{name}}</c> (spaces inside the braces are allowed) with its value.
    /// Names without a value are left as written and added to <paramref name="missing"/>.
    /// </summary>
    public static string Resolve(string text, IReadOnlyDictionary<string, string> variables, ISet<string> missing)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{", StringComparison.Ordinal))
        {
            return text;
        }

        return Reference().Replace(text, match =>
        {
            var name = match.Groups["name"].Value;
            if (variables.TryGetValue(name, out var value))
            {
                return value;
            }

            missing.Add(name);
            return match.Value;
        });
    }

    [GeneratedRegex(@"\{\{\s*(?<name>[^{}\s]+)\s*\}\}")]
    private static partial Regex Reference();
}
