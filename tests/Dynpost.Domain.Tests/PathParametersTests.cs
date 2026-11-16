using Dynpost.Domain.Http;
using Dynpost.Domain.Requests;

namespace Dynpost.Domain.Tests;

public class PathParametersTests
{
    [Fact]
    public void Names_FindsWholeColonSegmentsOfThePathOnly()
    {
        Assert.Equal(
            ["org", "id"],
            PathParameters.Names("http://localhost:8080/orgs/:org/users/:id/:org?q=/:no#/:none"));
        Assert.Equal(["file"], PathParameters.Names("{{baseUrl}}/files/:file.json"));
        Assert.Empty(PathParameters.Names("https://example.com/a:b/:1x"));
    }

    [Fact]
    public void Apply_ReplacesSegmentsWithEncodedResolvedValues()
    {
        var url = PathParameters.Apply(
            "{{baseUrl}}/users/:id/:missing/:empty?x=:id",
            [new KeyValueEntry("id", "{{user}}"), new KeyValueEntry("empty", "")],
            text => text.Replace("{{user}}", "a/b c"));

        Assert.Equal("{{baseUrl}}/users/a%2Fb%20c/:missing/:empty?x=:id", url);
    }

    [Fact]
    public void Sync_KeepsOneEntryPerNameInTheUrlsOrder()
    {
        var synced = PathParameters.Sync("/:b/:a", [new KeyValueEntry("a", "1"), new KeyValueEntry("gone", "2")]);

        Assert.Equal([new KeyValueEntry("b", ""), new KeyValueEntry("a", "1")], synced);
    }

    [Fact]
    public void Prepare_SendsThePathVariables()
    {
        var request = new HttpRequestDefinition("Get user", RequestMethod.Get, "https://{{host}}/users/:id")
        {
            PathParams = [new KeyValueEntry("id", "{{userId}}")],
        };

        var prepared = RequestPreparation.Prepare(request, new Dictionary<string, string> { ["host"] = "api.test", ["userId"] = "4 2" });

        Assert.True(prepared.IsValid, string.Join(" ", prepared.Errors));
        Assert.Equal("https://api.test/users/4%202", prepared.Request!.Url.AbsoluteUri);
    }

    [Fact]
    public void Validate_RejectsInvalidAndRepeatedNames()
    {
        var request = new HttpRequestDefinition("R", RequestMethod.Get, "/x")
        {
            PathParams = [new KeyValueEntry("a b", "1"), new KeyValueEntry("id", "1"), new KeyValueEntry("id", "2")],
        };

        var errors = request.Validate();

        Assert.Contains(errors, e => e.Contains("'a b'"));
        Assert.Contains(errors, e => e.Contains("more than once"));
    }
}
