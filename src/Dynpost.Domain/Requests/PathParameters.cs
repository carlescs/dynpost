using System.Text.RegularExpressions;

namespace Dynpost.Domain.Requests;

/// <summary>
/// Path variables: <c>:name</c> segments in a request URL's path (<c>/users/:id</c>), whose values
/// the request's <c>pathParams</c> set. A port (<c>host:8080</c>) or a colon inside a segment
/// (<c>/a:b</c>) is not one, and neither is anything in the query string or fragment.
/// </summary>
public static partial class PathParameters
{
    /// <summary>The path variable names in <paramref name="url"/>, in order, each once.</summary>
    public static IReadOnlyList<string> Names(string url) =>
        Segment().Matches(PathPart(url)).Select(match => match.Groups["name"].Value).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Whether <paramref name="name"/> can be used as a <c>:name</c> path variable.</summary>
    public static bool IsValidName(string name) => ValidName().IsMatch(name);

    /// <summary>
    /// Replaces each <c>:name</c> segment that has a value in <paramref name="values"/> with the
    /// value, passed through <paramref name="resolve"/> (for <c>{{variables}}</c>) and then
    /// percent-encoded, so a value can't add segments or a query. Segments without a value (or with
    /// an empty one) are left as written.
    /// </summary>
    public static string Apply(string url, IReadOnlyList<KeyValueEntry> values, Func<string, string> resolve)
    {
        if (values.Count == 0 || string.IsNullOrEmpty(url))
        {
            return url;
        }

        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in values)
        {
            if (entry.Value.Length > 0)
            {
                byName.TryAdd(entry.Key, entry.Value);
            }
        }

        var path = PathPart(url);
        var replaced = Segment().Replace(path, match =>
            byName.TryGetValue(match.Groups["name"].Value, out var value)
                ? Uri.EscapeDataString(resolve(value))
                : match.Value);
        return replaced + url[path.Length..];
    }

    /// <summary>
    /// Keeps <paramref name="current"/> in step with the names in <paramref name="url"/>: one entry
    /// per name, in the URL's order, keeping the values already set.
    /// </summary>
    public static IReadOnlyList<KeyValueEntry> Sync(string url, IReadOnlyList<KeyValueEntry> current)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in current)
        {
            values.TryAdd(entry.Key, entry.Value);
        }

        return Names(url).Select(name => new KeyValueEntry(name, values.GetValueOrDefault(name, string.Empty))).ToList();
    }

    /// <summary>The URL up to its query string or fragment.</summary>
    private static string PathPart(string url)
    {
        var end = url.IndexOfAny(['?', '#']);
        return end < 0 ? url : url[..end];
    }

    [GeneratedRegex(@"(?<=/):(?<name>[A-Za-z_][A-Za-z0-9_\-]*)(?=[/;.]|$)")]
    private static partial Regex Segment();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_\-]*$")]
    private static partial Regex ValidName();
}
