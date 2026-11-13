using System.Globalization;
using Dynpost.Domain.Git;

namespace Dynpost.Infrastructure.Git;

/// <summary>Formats and parses the output of <c>git log</c>, <c>git show</c> and <c>git diff-tree</c>.</summary>
internal static class CommitLog
{
    /// <summary>Separates the fields of a commit (ASCII unit separator, which commit text doesn't contain).</summary>
    public const string FieldSeparator = "%x1f";

    /// <summary>Ends each commit in a log (ASCII record separator).</summary>
    public const string RecordSeparator = "%x1e";

    /// <summary>Id, parents, author name, author email, author date (strict ISO 8601) and subject.</summary>
    public const string Format = "%H%x1f%P%x1f%an%x1f%ae%x1f%aI%x1f%s";

    private const int FieldCount = 6;

    public static IReadOnlyList<GitCommitInfo> ParseLog(string output) =>
        output
            .Split('\x1e')
            .Select(record => record.Trim('\r', '\n'))
            .Where(record => record.Length > 0)
            .Select(record => ParseInfo(record.Split('\x1f', FieldCount)))
            .ToList();

    /// <summary>Parses <see cref="Format"/> followed by a field separator and the message body.</summary>
    public static (GitCommitInfo Commit, string Body) ParseCommit(string output)
    {
        var fields = output.Split('\x1f', FieldCount + 1);
        var body = fields.Length > FieldCount ? fields[FieldCount].ReplaceLineEndings("\n").Trim('\n') : string.Empty;
        return (ParseInfo(fields), body);
    }

    private static GitCommitInfo ParseInfo(string[] fields)
    {
        if (fields.Length < FieldCount)
        {
            throw new FormatException("Unexpected git log output.");
        }

        return new GitCommitInfo(
            fields[0].Trim(),
            fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
            fields[2],
            fields[3],
            DateTimeOffset.Parse(fields[4].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None),
            fields[5].TrimEnd('\r', '\n'));
    }

    /// <summary>
    /// Parses <c>git diff-tree -z --name-status</c>: a status (with a score for renames and copies)
    /// followed by one path, or two for renames and copies, each NUL-terminated.
    /// </summary>
    public static IReadOnlyList<GitChange> ParseNameStatus(string output)
    {
        var parts = output.Split('\0');
        var changes = new List<GitChange>();
        var i = 0;
        while (i < parts.Length && parts[i].Length > 0)
        {
            var code = parts[i][0];
            if (code is 'R' or 'C')
            {
                if (i + 2 >= parts.Length)
                {
                    break;
                }

                var from = parts[i + 1];
                var to = parts[i + 2];
                changes.Add(code == 'R'
                    ? new GitChange(to, GitFileStatus.Renamed, from)
                    : new GitChange(to, GitFileStatus.Added));
                i += 3;
                continue;
            }

            if (i + 1 >= parts.Length)
            {
                break;
            }

            var status = code switch
            {
                'A' => GitFileStatus.Added,
                'D' => GitFileStatus.Deleted,
                _ => GitFileStatus.Modified,
            };
            changes.Add(new GitChange(parts[i + 1], status));
            i += 2;
        }

        return changes;
    }
}
