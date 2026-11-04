using Missive.Domain.Git;

namespace Missive.Infrastructure.Git;

/// <summary>
/// Parses <c>git status --porcelain=v2 --branch -z</c> output. Paths in that format are relative to
/// the repository root; they are converted to paths relative to the workspace folder, and entries
/// outside it are dropped.
/// </summary>
internal static class PorcelainStatusParser
{
    /// <param name="output">The NUL-separated status output.</param>
    /// <param name="workspacePrefix">
    /// The workspace folder relative to the repository root with a trailing <c>/</c>
    /// (<c>git rev-parse --show-prefix</c>), or empty when the workspace is the repository root.
    /// </param>
    /// <param name="isMerging">Whether a merge is in progress.</param>
    public static GitStatus Parse(string output, string workspacePrefix, bool isMerging)
    {
        string? branch = null;
        string? upstream = null;
        var ahead = 0;
        var behind = 0;
        var changes = new List<GitChange>();

        var records = output.Split('\0');
        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (record.Length < 2)
            {
                continue;
            }

            switch (record[0])
            {
                case '#':
                    ParseHeader(record, ref branch, ref upstream, ref ahead, ref behind);
                    break;

                // 1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path>
                case '1':
                {
                    var fields = record.Split(' ', 9);
                    if (fields.Length == 9)
                    {
                        Add(changes, workspacePrefix, fields[8], OrdinaryStatus(fields[1]));
                    }

                    break;
                }

                // 2 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <X><score> <path>, then NUL and <origPath>
                case '2':
                {
                    var fields = record.Split(' ', 10);
                    var originalPath = i + 1 < records.Length ? records[++i] : null;
                    if (fields.Length == 10)
                    {
                        AddRenameOrCopy(changes, workspacePrefix, fields[9], originalPath, fields[8]);
                    }

                    break;
                }

                // u <XY> <sub> <m1> <m2> <m3> <mW> <h1> <h2> <h3> <path>
                case 'u':
                {
                    var fields = record.Split(' ', 11);
                    if (fields.Length == 11)
                    {
                        Add(changes, workspacePrefix, fields[10], GitFileStatus.Conflicted);
                    }

                    break;
                }

                // ? <path>
                case '?':
                    Add(changes, workspacePrefix, record[2..], GitFileStatus.Untracked);
                    break;
            }
        }

        changes.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return new GitStatus(branch, upstream, ahead, behind, isMerging, changes);
    }

    private static void ParseHeader(string record, ref string? branch, ref string? upstream, ref int ahead, ref int behind)
    {
        var fields = record.Split(' ', 3);
        if (fields.Length < 3)
        {
            return;
        }

        switch (fields[1])
        {
            case "branch.head":
                branch = fields[2] == "(detached)" ? null : fields[2];
                break;
            case "branch.upstream":
                upstream = fields[2];
                break;
            case "branch.ab":
                var counts = fields[2].Split(' ');
                if (counts.Length == 2)
                {
                    _ = int.TryParse(counts[0].TrimStart('+'), out ahead);
                    _ = int.TryParse(counts[1].TrimStart('-'), out behind);
                }

                break;
        }
    }

    /// <summary>Maps the index (X) and worktree (Y) codes of an ordinary entry.</summary>
    private static GitFileStatus OrdinaryStatus(string xy)
    {
        var index = xy[0];
        var worktree = xy.Length > 1 ? xy[1] : '.';

        if (index == 'A')
        {
            return GitFileStatus.Added;
        }

        if (index == 'D' || worktree == 'D')
        {
            return GitFileStatus.Deleted;
        }

        return GitFileStatus.Modified;
    }

    private static void AddRenameOrCopy(
        List<GitChange> changes,
        string prefix,
        string path,
        string? originalRepoPath,
        string score)
    {
        var originalPath = originalRepoPath is null ? null : ToWorkspacePath(originalRepoPath, prefix);
        if (score.StartsWith('C') || originalPath is null)
        {
            // A copy leaves its source untouched, and a rename from outside the workspace looks like
            // a new file from inside it.
            Add(changes, prefix, path, GitFileStatus.Added);
            return;
        }

        if (ToWorkspacePath(path, prefix) is { } workspacePath)
        {
            changes.Add(new GitChange(workspacePath, GitFileStatus.Renamed, originalPath));
        }
        else
        {
            // Renamed out of the workspace: from inside it, the file was deleted.
            changes.Add(new GitChange(originalPath, GitFileStatus.Deleted));
        }
    }

    private static void Add(List<GitChange> changes, string prefix, string repoPath, GitFileStatus status)
    {
        if (ToWorkspacePath(repoPath, prefix) is { } path)
        {
            changes.Add(new GitChange(path, status));
        }
    }

    private static string? ToWorkspacePath(string repoPath, string prefix)
    {
        if (prefix.Length == 0)
        {
            return repoPath;
        }

        return repoPath.StartsWith(prefix, StringComparison.Ordinal) && repoPath.Length > prefix.Length
            ? repoPath[prefix.Length..]
            : null;
    }
}
