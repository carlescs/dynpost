using System.Text;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Infrastructure.Sharing;

/// <summary>
/// Reads and edits the <c>.gitignore</c> lines that make single workspace files local-only: one
/// anchored, literal path per line, such as <c>/environments/local.yaml</c>. Other lines (patterns,
/// comments, negations) are never changed, and an edit changes as few lines as it can.
/// </summary>
internal static class GitIgnoreEntries
{
    private const string GlobCharacters = "\\*?[";

    /// <summary>The line that ignores exactly <paramref name="path"/>.</summary>
    public static string Format(WorkspacePath path)
    {
        var builder = new StringBuilder("/");
        foreach (var c in path.Value)
        {
            if (GlobCharacters.Contains(c))
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        // git drops trailing spaces unless they are escaped.
        if (builder[^1] == ' ')
        {
            builder.Insert(builder.Length - 1, '\\');
        }

        return builder.ToString();
    }

    /// <summary>The file a line ignores, when it names exactly one workspace file; otherwise <c>null</c>.</summary>
    public static WorkspacePath? Parse(string line)
    {
        line = line.TrimEnd('\r');
        if (!line.StartsWith('/') || line.EndsWith('/'))
        {
            return null;
        }

        var builder = new StringBuilder();
        for (var i = 1; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\\')
            {
                if (i + 1 >= line.Length)
                {
                    return null;
                }

                builder.Append(line[++i]);
            }
            else if (GlobCharacters.Contains(c))
            {
                return null;
            }
            else
            {
                builder.Append(c);
            }
        }

        // Unescaped trailing spaces are not part of the pattern.
        var value = builder.ToString();
        if (!line.EndsWith("\\ ", StringComparison.Ordinal))
        {
            value = value.TrimEnd(' ');
        }

        return WorkspacePath.TryParse(value, out var path) && !path.IsRoot && path.Value == value ? path : null;
    }

    /// <summary>Every file the content's lines make local-only.</summary>
    public static IReadOnlySet<WorkspacePath> List(string content) =>
        Lines(content).Select(Parse).OfType<WorkspacePath>().ToHashSet();

    /// <summary>The content with a line for <paramref name="path"/> appended, unless it has one.</summary>
    public static string Add(string content, WorkspacePath path) => AddAll(content, [path]);

    /// <summary>The content without the lines that name <paramref name="path"/> or a file inside it.</summary>
    public static string Remove(string content, WorkspacePath path) =>
        Rewrite(content, entry => IsAtOrInside(entry, path) ? null : entry);

    /// <summary>The content with the lines for files at or inside <paramref name="from"/> moved to <paramref name="to"/>.</summary>
    public static string Move(string content, WorkspacePath from, WorkspacePath to) =>
        Rewrite(content, entry => IsAtOrInside(entry, from) ? Relocate(entry, from, to) : entry);

    /// <summary>The content with lines added for the copies, under <paramref name="to"/>, of local-only files at or inside <paramref name="from"/>.</summary>
    public static string Copy(string content, WorkspacePath from, WorkspacePath to) =>
        AddAll(content, List(content).Where(entry => IsAtOrInside(entry, from)).Select(entry => Relocate(entry, from, to)).ToList());

    private static string AddAll(string content, IReadOnlyList<WorkspacePath> paths)
    {
        var existing = List(content);
        var missing = paths.Where(p => !existing.Contains(p)).Distinct().OrderBy(p => p.Value, StringComparer.Ordinal).ToList();
        if (missing.Count == 0)
        {
            return content;
        }

        // Match the file's line endings, so the change is only the added lines.
        var newline = Newline(content);
        var separator = content.Length == 0 || content.EndsWith('\n') ? string.Empty : newline;
        return content + separator + string.Concat(missing.Select(p => Format(p) + newline));
    }

    /// <summary>Applies <paramref name="map"/> to each entry line; <c>null</c> drops the line.</summary>
    private static string Rewrite(string content, Func<WorkspacePath, WorkspacePath?> map)
    {
        var newline = Newline(content);
        var lines = Lines(content);
        var kept = new List<string>(lines.Count);
        var seen = new HashSet<WorkspacePath>();
        var changed = false;
        foreach (var line in lines)
        {
            if (Parse(line) is not { } entry)
            {
                kept.Add(line);
                continue;
            }

            var mapped = map(entry);
            if (mapped is null || !seen.Add(mapped))
            {
                changed = true;
                continue;
            }

            if (mapped != entry)
            {
                changed = true;
                kept.Add(Format(mapped) + (line.EndsWith('\r') ? "\r" : string.Empty));
            }
            else
            {
                kept.Add(line);
            }
        }

        if (!changed)
        {
            return content;
        }

        if (kept.Count == 0)
        {
            return string.Empty;
        }

        // Lines keep their own \r, so only a last line that lacked one needs the file's full newline.
        var last = kept[^1].EndsWith('\r') ? "\n" : newline;
        return string.Join('\n', kept) + last;
    }

    private static List<string> Lines(string content)
    {
        var lines = content.Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static string Newline(string content) => content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static bool IsAtOrInside(WorkspacePath entry, WorkspacePath path) =>
        entry == path || entry.Value.StartsWith(path.Value + "/", StringComparison.Ordinal);

    private static WorkspacePath Relocate(WorkspacePath entry, WorkspacePath from, WorkspacePath to) =>
        WorkspacePath.Parse(to.Value + entry.Value[from.Value.Length..]);
}
