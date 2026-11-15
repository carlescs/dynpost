using Dynpost.Domain.Search;

namespace Dynpost.Domain.Tests.Search;

public class TextSearchTests
{
    [Fact]
    public void FindsEveryOccurrence_LineByLine_IgnoringCase()
    {
        var lines = TextSearch.Find("name: Get user\nurl: /users/{{id}}\nmethod: GET\n", "user");

        Assert.Equal([1, 2], lines.Select(l => l.Line));
        Assert.Equal("name: Get user", lines[0].Text);
        Assert.Equal([new TextRange(10, 4)], lines[0].Ranges);
        Assert.Equal([new TextRange(6, 4)], lines[1].Ranges);
    }

    [Fact]
    public void MatchCase_KeepsOnlyExactCase()
    {
        var lines = TextSearch.Find("name: Get\nmethod: GET\n", "GET", matchCase: true);

        Assert.Equal(2, Assert.Single(lines).Line);
    }

    [Fact]
    public void SeveralOccurrencesInALine_AreAllReported()
    {
        var line = Assert.Single(TextSearch.Find("aXaXa", "a"));

        Assert.Equal([new TextRange(0, 1), new TextRange(2, 1), new TextRange(4, 1)], line.Ranges);
    }

    [Fact]
    public void Indentation_IsLeftOut_AndRangesFollow()
    {
        var line = Assert.Single(TextSearch.Find("headers:\r\n  Accept: application/json\r\n", "json"));

        Assert.Equal("Accept: application/json", line.Text);
        Assert.Equal([new TextRange(20, 4)], line.Ranges);
    }

    [Fact]
    public void ALongLine_IsCutAroundTheFirstMatch()
    {
        var text = new string('a', 500) + "needle" + new string('b', 500);

        var line = Assert.Single(TextSearch.Find(text, "needle"));

        Assert.StartsWith("…", line.Text);
        Assert.EndsWith("…", line.Text);
        var range = Assert.Single(line.Ranges);
        Assert.Equal("needle", line.Text.Substring(range.Start, range.Length));
        Assert.True(line.Text.Length <= TextSearch.MaxLineLength + 2);
    }

    [Fact]
    public void AnEmptyQuery_FindsNothing() => Assert.Empty(TextSearch.Find("text", ""));
}
