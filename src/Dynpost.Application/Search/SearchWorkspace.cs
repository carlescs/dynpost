using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Search;

namespace Dynpost.Application.Search;

/// <summary>
/// Searches the text of every request file — URLs, parameters, headers, bodies, descriptions —
/// and returns the matching lines grouped by file: in the tree's order for a plain search; for a
/// fuzzy one, which forgives typos (see <see cref="FuzzyTolerance"/>), the files with the closest
/// matches first.
/// </summary>
public sealed class SearchWorkspace(IWorkspaceRepository repository, IFuzzyMatcher matcher, CurrentWorkspace current)
{
    /// <summary>The search returns at most this many matching lines, so a one-letter query stays quick to show.</summary>
    public const int MaxLines = 1000;

    public async Task<Result<WorkspaceSearchDto>> ExecuteAsync(
        string? query,
        bool matchCase = false,
        bool fuzzy = false,
        CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        var files = await RequestFiles.ListAsync(repository, root.Value, cancellationToken);
        if (!files.IsSuccess)
        {
            return files.Error!;
        }

        if (string.IsNullOrEmpty(query))
        {
            return new WorkspaceSearchDto([], 0, false);
        }

        var found = new List<(RequestFiles.Item File, IReadOnlyList<LineMatch> Lines, int Distance)>();
        foreach (var file in files.Value)
        {
            var text = await repository.ReadTextAsync(root.Value, file.Path, cancellationToken);
            if (!text.IsSuccess)
            {
                // Deleted since it was listed, or unreadable: there is nothing to search in it.
                continue;
            }

            var (lines, distance) = fuzzy ? FindFuzzy(text.Value, query, matchCase) : (TextSearch.Find(text.Value, query, matchCase), 0);
            if (lines.Count > 0)
            {
                found.Add((file, lines, distance));
            }
        }

        if (fuzzy)
        {
            // OrderBy is stable: files that match equally well keep the tree's order.
            found = [.. found.OrderBy(entry => entry.Distance)];
        }

        var results = new List<SearchFileDto>();
        var lineCount = 0;
        var matchCount = 0;
        var truncated = false;
        foreach (var (file, all, _) in found)
        {
            var lines = all;
            if (lineCount + lines.Count > MaxLines)
            {
                truncated = true;
                if (lineCount == MaxLines)
                {
                    break;
                }

                lines = [.. lines.Take(MaxLines - lineCount)];
            }

            lineCount += lines.Count;
            matchCount += lines.Sum(line => line.Ranges.Count);
            results.Add(new SearchFileDto(
                file.Path.Value,
                file.Name,
                file.Method,
                [.. lines.Select(line => new SearchLineDto(line.Line, line.Text, [.. line.Ranges.Select(r => new SearchRangeDto(r.Start, r.Length))]))]));
            if (truncated)
            {
                break;
            }
        }

        return new WorkspaceSearchDto(results, matchCount, truncated);
    }

    /// <summary>The lines with fuzzy matches, and the fewest typos any of them has.</summary>
    private (IReadOnlyList<LineMatch> Lines, int Distance) FindFuzzy(string text, string query, bool matchCase)
    {
        var lines = new List<LineMatch>();
        var distance = int.MaxValue;
        var rows = text.Split('\n');
        for (var number = 0; number < rows.Length; number++)
        {
            var line = rows[number].TrimEnd('\r');
            var matches = matcher.FindTolerant(query, line, matchCase);
            if (matches.Count == 0)
            {
                continue;
            }

            distance = Math.Min(distance, matches.Min(match => match.Distance));
            lines.Add(TextSearch.Show(number + 1, line, [.. matches.Select(match => new TextRange(match.Start, match.Length))]));
        }

        return (lines, distance);
    }
}
