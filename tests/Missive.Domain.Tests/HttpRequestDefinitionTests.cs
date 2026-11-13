using Missive.Domain.Requests;

namespace Missive.Domain.Tests;

public class HttpRequestDefinitionTests
{
    [Fact]
    public void Validate_AcceptsAMinimalRequest()
    {
        var request = new HttpRequestDefinition("Health", RequestMethod.Get, "https://example.com/health");

        Assert.Empty(request.Validate());
        Assert.Same(RequestAuth.Inherit, request.Auth);
        Assert.Same(RequestBody.None, request.Body);
    }

    [Fact]
    public void Validate_RequiresAName()
    {
        var request = new HttpRequestDefinition(" ", RequestMethod.Get, "");

        Assert.Single(request.Validate());
    }

    [Fact]
    public void Validate_RejectsDuplicateHeadersIgnoringCase()
    {
        var request = new HttpRequestDefinition("x", RequestMethod.Get, "", headers:
        [
            new KeyValueEntry("Accept", "a"),
            new KeyValueEntry("accept", "b", Enabled: false),
        ]);

        Assert.Contains(request.Validate(), e => e.Contains("defined more than once"));
    }

    [Fact]
    public void Validate_RejectsEntriesWithoutName()
    {
        var request = new HttpRequestDefinition("x", RequestMethod.Get, "", @params: [new KeyValueEntry("", "1")]);

        Assert.NotEmpty(request.Validate());
    }

    [Fact]
    public void Validate_RejectsNamesStartingWithTheDisabledPrefix()
    {
        var request = new HttpRequestDefinition("x", RequestMethod.Get, "", headers: [new KeyValueEntry("~X", "1")]);

        Assert.NotEmpty(request.Validate());
    }

    [Theory]
    [InlineData("get", RequestMethod.Get)]
    [InlineData("PATCH", RequestMethod.Patch)]
    [InlineData(" options ", RequestMethod.Options)]
    public void RequestMethods_ParsesNamesCaseInsensitively(string name, RequestMethod expected)
    {
        Assert.True(RequestMethods.TryParse(name, out var method));
        Assert.Equal(expected, method);
        Assert.False(RequestMethods.TryParse("FETCH", out _));
    }
}
