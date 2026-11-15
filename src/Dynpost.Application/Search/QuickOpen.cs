using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;

namespace Dynpost.Application.Search;

/// <summary>
/// Finds requests by their names, file paths, methods and URLs for the quick open (Ctrl+P) dialog,
/// best match first, forgiving typos (see <see cref="FuzzyTolerance"/>). Words of the query
/// separated by spaces can each match a different one of these: <c>post users</c> finds a POST
/// request named "Create user".
/// </summary>
public sealed class QuickOpen(IWorkspaceRepository repository, IFuzzyMatcher matcher, CurrentWorkspace current)
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

    /// <summary>Scores a request: every term must match its name, URL, method or path.</summary>
    private (int Score, QuickOpenItemDto Item)? Match(RequestFiles.Item file, IReadOnlyList<string> terms)
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
            // The name counts most, then the URL and the method (exactly); the path breaks ties with the folder.
            (string Text, int Weight, SortedSet<int> Positions)[] fields = [(name, 3, nameMatches), (url, 2, urlMatches), (path, 1, pathMatches)];
            var best = fields
                .SelectMany(field => matcher.FindTolerant(term, field.Text, matchCase: false)
                    .Select(match => (Positions: (SortedSet<int>?)field.Positions, Match: (FuzzyMatch?)match, Score: Score(term, field.Weight, match.Distance, IsWordStart(field.Text, match.Start)))))
                .Append(string.Equals(term, method, StringComparison.OrdinalIgnoreCase)
                    ? (null, new FuzzyMatch(0, method.Length, 0), Score(term, 2, 0, wordStart: true))
                    : (null, null, int.MinValue))
                .MaxBy(candidate => candidate.Score);
            if (best.Match is null)
            {
                return null;
            }

            total += best.Score;
            best.Positions?.UnionWith(Enumerable.Range(best.Match.Start, best.Match.Length));
        }

        return (total, new QuickOpenItemDto(path, name, file.Method, url, [.. nameMatches], [.. pathMatches], [.. urlMatches]));
    }

    /// <summary>Longer terms count more, typos cost, and matching from the start of a word helps.</summary>
    private static int Score(string term, int weight, int distance, bool wordStart) =>
        weight * (term.Length * 10 - distance * 15 + (wordStart ? 5 : 0));

    private static bool IsWordStart(string text, int index) => index == 0 || !char.IsLetterOrDigit(text[index - 1]);
}
