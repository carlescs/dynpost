namespace Missive.Domain.Git;

/// <summary>Detects the markers git writes into a file when a merge conflicts.</summary>
public static class ConflictMarkers
{
    /// <summary>
    /// Whether <paramref name="text"/> has a complete conflict block: a <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c>
    /// line, then <c>=======</c>, then <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c>, each at the start of a line.
    /// </summary>
    public static bool Contains(string text)
    {
        var stage = 0;
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            stage = stage switch
            {
                0 when line.StartsWith("<<<<<<<", StringComparison.Ordinal) => 1,
                1 when line.StartsWith("=======", StringComparison.Ordinal) => 2,
                2 when line.StartsWith(">>>>>>>", StringComparison.Ordinal) => 3,
                _ => stage,
            };

            if (stage == 3)
            {
                return true;
            }
        }

        return false;
    }
}
