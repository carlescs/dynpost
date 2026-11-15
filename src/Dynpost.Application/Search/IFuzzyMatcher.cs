namespace Dynpost.Application.Search;

/// <summary>Where a fuzzy search found its pattern in a text, and with how many typos.</summary>
/// <param name="Distance">The edits (characters substituted, inserted or deleted) between the pattern and the text found.</param>
public sealed record FuzzyMatch(int Start, int Length, int Distance);

/// <summary>Approximate (typo-tolerant) substring search, as quick open and the fuzzy workspace search use it.</summary>
public interface IFuzzyMatcher
{
    /// <summary>
    /// Finds the places in <paramref name="text"/> that differ from <paramref name="pattern"/> by at most
    /// <paramref name="maxDistance"/> edits, in order and not overlapping. Letter case counts.
    /// </summary>
    IReadOnlyList<FuzzyMatch> Find(string pattern, string text, int maxDistance);
}

/// <summary>How many typos a fuzzy search forgives, by the length of what was typed.</summary>
public static class FuzzyTolerance
{
    /// <summary>None for 1–3 characters, one for 4–6, two for longer patterns.</summary>
    public static int MaxDistance(int patternLength) => patternLength switch
    {
        <= 3 => 0,
        <= 6 => 1,
        _ => 2,
    };

    /// <summary>
    /// Finds <paramref name="pattern"/> in <paramref name="text"/> with the tolerance its length allows,
    /// ignoring case unless <paramref name="matchCase"/> is set.
    /// </summary>
    public static IReadOnlyList<FuzzyMatch> FindTolerant(this IFuzzyMatcher matcher, string pattern, string text, bool matchCase)
    {
        if (pattern.Length == 0 || text.Length == 0)
        {
            return [];
        }

        // Invariant lower-casing maps character by character, so indexes stay the same.
        return matchCase
            ? matcher.Find(pattern, text, MaxDistance(pattern.Length))
            : matcher.Find(pattern.ToLowerInvariant(), text.ToLowerInvariant(), MaxDistance(pattern.Length));
    }
}
