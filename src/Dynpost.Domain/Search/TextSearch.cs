namespace Dynpost.Domain.Search;

/// <summary>Where a search found its text in a line: <paramref name="Start"/> is an index into the line shown.</summary>
public sealed record TextRange(int Start, int Length);

/// <summary>A line of a file that contains the searched text.</summary>
/// <param name="Line">The line's number, from 1.</param>
/// <param name="Text">
/// The line as shown: without its indentation and, when it is long, cut to the part around the
/// first match (an ellipsis marks what was cut).
/// </param>
/// <param name="Ranges">Where each match is in <paramref name="Text"/>, in order and not overlapping.</param>
public sealed record LineMatch(int Line, string Text, IReadOnlyList<TextRange> Ranges);

/// <summary>Plain-text, line-by-line search, as a workspace search runs over each file.</summary>
public static class TextSearch
{
    /// <summary>The longest line shown; longer ones are cut around their first match.</summary>
    public const int MaxLineLength = 200;

    /// <summary>How much of a cut line is kept before its first match.</summary>
    private const int Lead = 40;

    private const string Ellipsis = "…";


    /// <summary>
    /// Finds every occurrence of <paramref name="query"/> in <paramref name="text"/>, line by line.
    /// Ignores case unless <paramref name="matchCase"/> is set. An empty query finds nothing.
    /// </summary>
    public static IReadOnlyList<LineMatch> Find(string text, string query, bool matchCase = false)
    {
        if (query.Length == 0 || query.Contains('\n'))
        {
            return [];
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var matches = new List<LineMatch>();
        var lines = text.Split('\n');
        for (var number = 0; number < lines.Length; number++)
        {
            var line = lines[number].TrimEnd('\r');
            var starts = new List<int>();
            for (var at = line.IndexOf(query, comparison); at >= 0; at = line.IndexOf(query, at + query.Length, comparison))
            {
                starts.Add(at);
            }

            if (starts.Count > 0)
            {
                matches.Add(Show(number + 1, line, [.. starts.Select(start => new TextRange(start, query.Length))]));
            }
        }

        return matches;
    }

    /// <summary>
    /// Shows line <paramref name="number"/> with matches found some other way (such as a fuzzy
    /// search) as <see cref="Find"/> shows its own: without indentation, cut when long.
    /// </summary>
    /// <param name="ranges">The matches, as indexes into the whole line, in order.</param>
    public static LineMatch Show(int number, string line, IReadOnlyList<TextRange> ranges)
    {
        var indent = line.Length - line.TrimStart().Length;
        var from = line.Length - indent > MaxLineLength
            ? Math.Max(indent, Math.Min(ranges[0].Start - Lead, line.Length - MaxLineLength))
            : indent;
        var to = Math.Min(line.Length, from + MaxLineLength);
        var prefix = from > indent ? Ellipsis : string.Empty;
        var suffix = to < line.Length ? Ellipsis : string.Empty;
        var shown = prefix + line[from..to] + suffix;

        var shownRanges = ranges
            .Where(range => range.Start >= from && range.Start + range.Length <= to)
            .Select(range => range with { Start = range.Start - from + prefix.Length })
            .ToList();
        return new LineMatch(number, shown, shownRanges);
    }
}
