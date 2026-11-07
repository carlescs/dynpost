using Missive.Domain.Http;

namespace Missive.Domain.Tests.Http;

public class SetCookieHeaderTests
{
    [Fact]
    public void Parse_ReadsTheNameValueAndAttributes()
    {
        var cookie = SetCookieHeader.Parse("session=abc123; Domain=example.com; Path=/api; Expires=Wed, 09 Jun 2027 10:18:14 GMT; Max-Age=3600; Secure; HttpOnly; SameSite=Strict");

        Assert.Equal(
            new ResponseCookie("session", "abc123", "example.com", "/api", new DateTimeOffset(2027, 6, 9, 10, 18, 14, TimeSpan.Zero), 3600, true, true, "Strict"),
            cookie);
    }

    [Fact]
    public void Parse_KeepsValuesWithEqualsSignsAndRemovesQuotes()
    {
        Assert.Equal("a=b", SetCookieHeader.Parse("token=a=b")!.Value);
        Assert.Equal("quoted", SetCookieHeader.Parse("q=\"quoted\"; path=/")!.Value);
    }

    [Theory]
    [InlineData("noequals")]
    [InlineData("=value")]
    public void Parse_RefusesHeadersWithoutAName(string header) => Assert.Null(SetCookieHeader.Parse(header));

    [Fact]
    public void Parse_IgnoresInvalidAttributes()
    {
        var cookie = SetCookieHeader.Parse("a=1; Path=relative; Max-Age=soon; Expires=never; Unknown=x")!;

        Assert.Equal(new ResponseCookie("a", "1"), cookie);
    }
}
