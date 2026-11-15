namespace Dynpost.Domain.Search;

/// <summary>How well a fuzzy query matched a text, and which characters of the text it matched.</summary>
/// <param name="Score">Higher is better; only comparable between matches of the same query.</param>
/// <param name="Positions">The indexes of the matched characters, in increasing order.</param>
public sealed record FuzzyMatchResult(int Score, IReadOnlyList<int> Positions);

/// <summary>
/// Fuzzy matching for quick open: the characters of the query must appear in the text in order,
/// ignoring case, but not necessarily next to each other. Runs of consecutive characters and
/// characters at the start of a word score higher, and gaps cost.
/// </summary>
public static class FuzzyMatch
{
    private const int Match = 1;
    private const int WordStart = 8;
    private const int TextStart = 4;
    private const int Consecutive = 6;
    private const int Gap = 3;

    /// <summary>
    /// Matches <paramref name="query"/> against <paramref name="text"/>, choosing the best-scoring
    /// placement; <c>null</c> when the text does not contain the query's characters in order.
    /// An empty query matches everything with a score of zero.
    /// </summary>
    public static FuzzyMatchResult? Score(string query, string text)
    {
        var m = query.Length;
        var n = text.Length;
        if (m == 0)
        {
            return new FuzzyMatchResult(0, []);
        }

        if (m > n)
        {
            return null;
        }

        const int none = int.MinValue;
        var best = new int[m, n];
        var from = new int[m, n];
        for (var i = 0; i < m; i++)
        {
            var q = char.ToLowerInvariant(query[i]);

            // The best score of the previous query character at any position before j - 1, and where.
            var runningBest = none;
            var runningAt = -1;
            for (var j = 0; j < n; j++)
            {
                if (i > 0 && j >= 2 && best[i - 1, j - 2] > runningBest)
                {
                    runningBest = best[i - 1, j - 2];
                    runningAt = j - 2;
                }

                best[i, j] = none;
                if (char.ToLowerInvariant(text[j]) != q)
                {
                    continue;
                }

                var bonus = Match + Bonus(text, j);
                if (i == 0)
                {
                    // A match further into the text costs a little, so earlier ones win ties.
                    best[i, j] = bonus - Math.Min(j, Gap);
                    from[i, j] = -1;
                    continue;
                }

                var adjacent = j >= 1 && best[i - 1, j - 1] != none ? best[i - 1, j - 1] + Consecutive : none;
                var gapped = runningBest != none ? runningBest - Gap : none;
                if (adjacent == none && gapped == none)
                {
                    continue;
                }

                if (adjacent >= gapped)
                {
                    best[i, j] = adjacent + bonus;
                    from[i, j] = j - 1;
                }
                else
                {
                    best[i, j] = gapped + bonus;
                    from[i, j] = runningAt;
                }
            }
        }

        var score = none;
        var end = -1;
        for (var j = 0; j < n; j++)
        {
            if (best[m - 1, j] > score)
            {
                score = best[m - 1, j];
                end = j;
            }
        }

        if (end < 0)
        {
            return null;
        }

        var positions = new int[m];
        for (int i = m - 1, j = end; i >= 0; i--)
        {
            positions[i] = j;
            j = from[i, j];
        }

        return new FuzzyMatchResult(score, positions);
    }

    /// <summary>Extra points for a character that starts the text or a word in it.</summary>
    private static int Bonus(string text, int index)
    {
        if (index == 0)
        {
            return WordStart + TextStart;
        }

        var previous = text[index - 1];
        var current = text[index];
        if (!char.IsLetterOrDigit(previous))
        {
            return WordStart;
        }

        return char.IsUpper(current) && char.IsLower(previous) ? WordStart : 0;
    }
}
