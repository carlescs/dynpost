using Dynpost.Domain.Search;

namespace Dynpost.Domain.Tests.Search;

public class FuzzyMatchTests
{
    [Fact]
    public void CharactersInOrder_Match_IgnoringCase()
    {
        var match = FuzzyMatch.Score("gu", "Get user");

        Assert.NotNull(match);
        Assert.Equal([0, 4], match.Positions);
    }

    [Fact]
    public void CharactersOutOfOrder_DoNotMatch() => Assert.Null(FuzzyMatch.Score("ug", "Get"));

    [Fact]
    public void AQueryLongerThanTheText_DoesNotMatch() => Assert.Null(FuzzyMatch.Score("users", "user"));

    [Fact]
    public void AnEmptyQuery_MatchesEverything()
    {
        var match = FuzzyMatch.Score("", "anything");

        Assert.Equal(0, match!.Score);
        Assert.Empty(match.Positions);
    }

    [Fact]
    public void ConsecutiveCharacters_ScoreHigherThanScatteredOnes() =>
        Assert.True(FuzzyMatch.Score("user", "Get user")!.Score > FuzzyMatch.Score("user", "bluish waterer")!.Score);

    [Fact]
    public void WordStarts_ArePreferredOverTheFirstOccurrence()
    {
        // The greedy first "o" is in "Look"; the word start of "orders" is the better placement.
        var match = FuzzyMatch.Score("ord", "Look up orders");

        Assert.Equal([8, 9, 10], match!.Positions);
    }

    [Fact]
    public void CamelCaseHumps_CountAsWordStarts() =>
        Assert.True(FuzzyMatch.Score("gu", "getUser")!.Score > FuzzyMatch.Score("gu", "segue")!.Score);
}
