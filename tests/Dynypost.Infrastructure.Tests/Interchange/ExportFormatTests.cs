using System.Text.Json.Nodes;
using Dynypost.Application.Interchange;
using Dynypost.Domain.Environments;
using Dynypost.Domain.Requests;
using Dynypost.Infrastructure.Interchange;
using Dynypost.Infrastructure.Tests.Git;

namespace Dynypost.Infrastructure.Tests.Interchange;

public class ExportFormatTests
{
    private static readonly HttpRequestDefinition CreateUser = new(
        "Create user",
        RequestMethod.Post,
        "{{ baseUrl }}/users",
        [new KeyValueEntry("notify", "yes & no"), new KeyValueEntry("debug", "1", Enabled: false)],
        [new KeyValueEntry("X-Tenant", "{{tenant}}")],
        new BearerAuth("{{token}}"),
        new RequestBody(BodyType.Json, "{\n  \"name\": \"Ada\"\n}"))
    {
        Description = "Creates a user.\n\n- Needs the `admin` role.",
    };

    private static readonly HttpRequestDefinition Login = new(
        "Log in",
        RequestMethod.Post,
        "https://auth.test:8443/login",
        auth: new BasicAuth("ada", "{{password}}"),
        body: RequestBody.WithFields(BodyType.UrlEncoded, [new FormField("user", "ada@example.com", Encode: true), new FormField("remember", "1")]));

    private static readonly HttpRequestDefinition Upload = new(
        "Upload",
        RequestMethod.Put,
        "{{baseUrl}}/files",
        auth: new ApiKeyAuth("api_key", "{{key}}", ApiKeyLocation.Query),
        body: RequestBody.WithFields(BodyType.Form,
        [
            new FormField("title", "Report"),
            new FormField("kind", "pdf", Enabled: false),
            FormField.File("file", "docs/report.pdf", "application/pdf"),
            FormField.File("file", "docs/appendix.pdf"),
        ]));

    private static readonly RequestCollection Collection = new(
        new CollectionFolder(
            "my-api",
            [new CollectionFolder("users", [new CollectionFolder("admin", [], [Upload])], [CreateUser])],
            [Login]),
        [
            new EnvironmentDefinition("Development", [
                new EnvironmentVariable("baseUrl", "http://localhost"),
                new EnvironmentVariable("token", string.Empty, Secret: true),
            ]),
        ]);

    [Fact]
    public void Postman_WritesAValidV21Collection()
    {
        var output = new PostmanCollectionExporter().Export(Collection);

        var document = JsonNode.Parse(output.Content)!;
        Assert.Equal(PostmanFormat.SchemaV21, (string?)document["info"]!["schema"]);
        Assert.Equal("my-api", (string?)document["info"]!["name"]);
        Assert.EndsWith("\n", output.Content);
        Assert.DoesNotContain("\r", output.Content);

        var users = document["item"]![0]!;
        Assert.Equal("users", (string?)users["name"]);
        var create = users["item"]![1]!["request"]!;
        Assert.Equal("{{baseUrl}}/users?notify=yes%20%26%20no", (string?)create["url"]!["raw"]);
        Assert.Equal("{{baseUrl}}", (string?)create["url"]!["host"]![0]);
        Assert.True((bool?)create["url"]!["query"]![1]!["disabled"]);
        Assert.Equal("bearer", (string?)create["auth"]!["type"]);
        Assert.Equal("json", (string?)create["body"]!["options"]!["raw"]!["language"]);

        var login = document["item"]![1]!["request"]!["url"]!;
        Assert.Equal("https", (string?)login["protocol"]);
        Assert.Equal("8443", (string?)login["port"]);
        Assert.Equal(new[] { "auth", "test" }, login["host"]!.AsArray().Select(h => (string?)h));

        var environmentFile = Assert.Single(output.Companions);
        Assert.Equal("development.postman_environment.json", environmentFile.FileName);
        var environment = JsonNode.Parse(environmentFile.Content)!;
        Assert.Equal("secret", (string?)environment["values"]![1]!["type"]);
        Assert.Equal(string.Empty, (string?)environment["values"]![1]!["value"]);
    }

    [Fact]
    public void Exports_WriteDescriptionsOnlyWhenThereAreSome()
    {
        var postman = JsonNode.Parse(new PostmanCollectionExporter().Export(Collection).Content)!;
        Assert.Equal(CreateUser.Description, (string?)postman["item"]![0]!["item"]![1]!["request"]!["description"]);
        Assert.Null(postman["item"]![1]!["request"]!["description"]);

        var insomnia = JsonNode.Parse(new InsomniaExporter().Export(Collection).Content)!["resources"]!.AsArray();
        Assert.Equal(CreateUser.Description, (string?)insomnia.Single(r => (string?)r!["name"] == "Create user")!["description"]);
        Assert.Null(insomnia.Single(r => (string?)r!["name"] == "Log in")!["description"]);
    }

    [Fact]
    public void Postman_IsStableAcrossExports()
    {
        var exporter = new PostmanCollectionExporter();

        Assert.Equal(exporter.Export(Collection).Content, exporter.Export(Collection).Content);
    }

    [Fact]
    public void Postman_RoundTripsThroughTheImporter()
    {
        var output = new PostmanCollectionExporter().Export(Collection);

        var parsed = new CollectionImporter().Parse(output.Content, "my-api.postman_collection.json").Value;

        AssertSameRequests(Collection.Root, parsed.Collection.Root, normalizeVariables: true);
    }

    [Fact]
    public void Insomnia_RoundTripsWithEnvironments()
    {
        var output = new InsomniaExporter().Export(Collection);
        Assert.Contains("{{ _.tenant }}", output.Content);
        Assert.Contains(output.Warnings, w => w.Contains("Secret"));

        var parsed = new CollectionImporter().Parse(output.Content, "export.json").Value;

        AssertSameRequests(Collection.Root, parsed.Collection.Root, normalizeVariables: true);
        var environment = Assert.Single(parsed.Collection.Environments);
        Assert.Equal("Development", environment.Name);
        Assert.Equal(["baseUrl", "token"], environment.Variables.Select(v => v.Name));
    }

    [Fact]
    public void GraphqlBodies_RoundTripThroughPostmanAndInsomnia()
    {
        var request = new HttpRequestDefinition(
            "GraphQL",
            RequestMethod.Post,
            "https://example.test/graphql",
            body: new RequestBody(BodyType.Graphql, "query { me }", """{"id":"{{userId}}"}"""));
        var collection = new RequestCollection(new CollectionFolder("API", [], [request]), []);

        var postman = JsonNode.Parse(new PostmanCollectionExporter().Export(collection).Content)!;
        var postmanBody = postman["item"]![0]!["request"]!["body"]!;
        Assert.Equal("graphql", (string?)postmanBody["mode"]);
        Assert.Equal(request.Body.Content, (string?)postmanBody["graphql"]!["query"]);
        Assert.Equal(request.Body.Variables, (string?)postmanBody["graphql"]!["variables"]);
        var postmanImported = new CollectionImporter().Parse(postman.ToJsonString(), "api.json").Value;
        Assert.Equal(request.Body, Assert.Single(postmanImported.Collection.Root.Requests).Body);

        var insomnia = JsonNode.Parse(new InsomniaExporter().Export(collection).Content)!;
        var insomniaBody = insomnia["resources"]!.AsArray()
            .Single(r => (string?)r!["_type"] == "request")!["body"]!;
        Assert.Equal("application/json", (string?)insomniaBody["mimeType"]);
        var insomniaPayload = JsonNode.Parse((string?)insomniaBody["text"]!)!;
        Assert.Equal(request.Body.Content, (string?)insomniaPayload["query"]);
        Assert.Equal("""{"id":"{{ _.userId }}"}""", insomniaPayload["variables"]!.ToJsonString());
        var insomniaImported = new CollectionImporter().Parse(insomnia.ToJsonString(), "api.json").Value;
        Assert.Equal(request.Body, Assert.Single(insomniaImported.Collection.Root.Requests).Body);
    }

    [Fact]
    public async Task LocalExternalFiles_WritesAndReadsText()
    {
        using var temp = new TempDirectory();
        var files = new LocalExternalFiles();
        var path = temp.Combine("out", "a.json");

        Assert.True((await files.WriteTextAsync(path, "{}\n")).IsSuccess);

        Assert.True(files.Exists(path));
        Assert.Equal("{}\n", (await files.ReadTextAsync(path)).Value);
        Assert.Equal(Application.Common.ErrorCodes.NotFound, (await files.ReadTextAsync(temp.Combine("missing.json"))).Error?.Code);
    }

    private static void AssertSameRequests(CollectionFolder expected, CollectionFolder actual, bool normalizeVariables)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Folders.Select(f => f.Name), actual.Folders.Select(f => f.Name));
        Assert.Equal(expected.Requests.Count, actual.Requests.Count);
        for (var i = 0; i < expected.Requests.Count; i++)
        {
            var want = normalizeVariables
                ? InterchangeText.MapText(expected.Requests[i], text => InterchangeText.RenameVariables(text, name => name))
                : expected.Requests[i];
            var got = actual.Requests[i];
            Assert.Equal(want.Name, got.Name);
            Assert.Equal(want.Method, got.Method);
            Assert.Equal(want.Url, got.Url);
            Assert.Equal(want.Params, got.Params);
            Assert.Equal(want.Headers, got.Headers);
            Assert.Equal(want.Auth, got.Auth);
            Assert.Equal(want.Body, got.Body);
            Assert.Equal(want.Description, got.Description);
        }

        for (var i = 0; i < expected.Folders.Count; i++)
        {
            AssertSameRequests(expected.Folders[i], actual.Folders[i], normalizeVariables);
        }
    }
}
