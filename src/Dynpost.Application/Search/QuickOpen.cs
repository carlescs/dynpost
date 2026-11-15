using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Search;

namespace Dynpost.Application.Search;

/// <summary>
/// Finds requests by a fuzzy query over their names, file paths, methods and URLs, best match first,
/// for the quick open (Ctrl+P) dialog. Words of the query separated by spaces can each match a
/// different one of these: <c>post users</c> finds a POST request named "Create user".
/// </summary>
public sealed class QuickOpen(IWorkspaceRepository repository, CurrentWorkspace current)
{
    /// <summary>How many requests are returned at most.</summary>
    public const int MaxResults = 50;

    public async Task<Result<IReadOnlyList<QuickOpenItemDto>>> ExecuteAsync(string? query, CancellationToken cancellationToken = default)
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

        var terms = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return files.Value
            .Select((file, order) => (Match: Match(file, terms), Order: order))
            .Where(candidate => candidate.Match is not null)
            .OrderByDescending(candidate => candidate.Match!.Value.Score)
            // Among equal matches the shorter name is the closer one; without a query, the tree's order stands.
            .ThenBy(candidate => terms.Length > 0 ? candidate.Match!.Value.Item.Name.Length : 0)
            .ThenBy(candidate => candidate.Order)
            .Take(MaxResults)
            .Select(candidate => candidate.Match!.Value.Item)
            .ToList();
    }

    /// <summary>Scores a request: every term must match its name, path, method or URL.</summary>
    private static (int Score, QuickOpenItemDto Item)? Match(RequestFiles.Item file, IReadOnlyList<string> terms)
    {
        var name = file.Name;
        var path = file.Path.Value;
        var method = file.Method ?? string.Empty;
        var url = file.Request?.Url ?? string.Empty;
        var nameMatches = new SortedSet<int>();
        var pathMatches = new SortedSet<int>();
        var urlMatches = new SortedSet<int>();
        var total = 0;

        foreach (var term in terms)
        {
            // The name counts most, then the URL and method; the path breaks ties with the folder.
            var candidates = new (FuzzyMatchResult? Match, int Weight, SortedSet<int>? Positions)[]
            {
                (FuzzyMatch.Score(term, name), 3, nameMatches),
                (FuzzyMatch.Score(term, url), 2, urlMatches),
                (string.Equals(term, method, StringComparison.OrdinalIgnoreCase) ? new FuzzyMatchResult(term.Length * 10, []) : null, 2, null),
                (FuzzyMatch.Score(term, path), 1, pathMatches),
            };

            var best = candidates
                .Where(candidate => candidate.Match is not null)
                .Select(candidate => (candidate.Match, candidate.Positions, Score: candidate.Match!.Score * candidate.Weight))
                .OrderByDescending(candidate => candidate.Score)
                .FirstOrDefault();
            if (best.Match is null)
            {
                return null;
            }

            total += best.Score;
            best.Positions?.UnionWith(best.Match.Positions);
        }

        return (total, new QuickOpenItemDto(path, name, file.Method, url, [.. nameMatches], [.. pathMatches], [.. urlMatches]));
    }
}
