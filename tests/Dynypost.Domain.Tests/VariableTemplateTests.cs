using Dynypost.Domain.Requests;

namespace Dynypost.Domain.Tests;

public class VariableTemplateTests
{
    private static readonly Dictionary<string, string> Variables = new()
    {
        ["baseUrl"] = "https://api.example.com",
        ["id"] = "42",
    };

    [Fact]
    public void Resolve_ReplacesKnownVariables()
    {
        var missing = new HashSet<string>();

        var result = VariableTemplate.Resolve("{{baseUrl}}/users/{{ id }}", Variables, missing);

        Assert.Equal("https://api.example.com/users/42", result);
        Assert.Empty(missing);
    }

    [Fact]
    public void Resolve_KeepsAndReportsUnknownVariables()
    {
        var missing = new HashSet<string>();

        var result = VariableTemplate.Resolve("Bearer {{apiToken}}", Variables, missing);

        Assert.Equal("Bearer {{apiToken}}", result);
        Assert.Equal(["apiToken"], missing);
    }

    [Theory]
    [InlineData("no variables")]
    [InlineData("{ {single} }")]
    [InlineData("{{}}")]
    [InlineData("")]
    public void Resolve_LeavesTextWithoutReferencesAlone(string text)
    {
        var missing = new HashSet<string>();

        Assert.Equal(text, VariableTemplate.Resolve(text, Variables, missing));
        Assert.Empty(missing);
    }
}
