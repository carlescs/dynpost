using Dynpost.Application.Search;
using Dynpost.Infrastructure.Search;

namespace Dynpost.Infrastructure.Tests.Search;

public class FuzzySearchNetMatcherTests
{
    private readonly FuzzySearchNetMatcher _matcher = new();

    [Fact]
    public void FindsTheTextWithATypo_AndWhereItIs()
    {
        var match = Assert.Single(_matcher.Find("authorisation", "authorization: Bearer x", 1));

        Assert.Equal(new FuzzyMatch(0, 13, 1), match);
    }

    [Fact]
    public void FindsEveryOccurrence_WithoutOverlaps()
    {
        Assert.Equal(
            [new FuzzyMatch(6, 5, 0), new FuzzyMatch(19, 5, 0)],
            _matcher.Find("users", "url: /users/{{id}}/users", 0));
    }

    [Fact]
    public void TooManyTypos_FindNothing() => Assert.Empty(_matcher.Find("usres", "url: /users/1", 1));

    [Fact]
    public void LetterCaseCounts_UnlessTheCallerLowerCasesBothSides()
    {
        Assert.Empty(_matcher.Find("GET", "method: get", 0));
        Assert.Single(_matcher.FindTolerant("GET", "method: get", matchCase: false));
    }

    [Fact]
    public void EmptyTexts_FindNothing()
    {
        Assert.Empty(_matcher.Find("", "text", 1));
        Assert.Empty(_matcher.Find("text", "", 1));
    }
}
