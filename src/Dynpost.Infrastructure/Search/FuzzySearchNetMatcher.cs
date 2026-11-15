using Dynpost.Application.Search;
using FuzzySearchNet;

namespace Dynpost.Infrastructure.Search;

/// <summary>Typo-tolerant substring search with the FuzzySearch.Net library (Levenshtein distance).</summary>
public sealed class FuzzySearchNetMatcher : IFuzzyMatcher
{
    public IReadOnlyList<FuzzyMatch> Find(string pattern, string text, int maxDistance) =>
        pattern.Length == 0 || text.Length == 0
            ? []
            : [.. FuzzySearch.Find(pattern, text, maxDistance)
                .Select(match => new FuzzyMatch(match.StartIndex, match.EndIndex - match.StartIndex, match.Distance))];
}
