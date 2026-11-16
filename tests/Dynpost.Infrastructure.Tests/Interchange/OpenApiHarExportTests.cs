using System.Text.Json.Nodes;
using Dynpost.Application.History;
using Dynpost.Application.Http;
using Dynpost.Application.Interchange;
using Dynpost.Application.Requests;
using Dynpost.Domain.Environments;
using Dynpost.Domain.Requests;
using Dynpost.Infrastructure.Interchange;

namespace Dynpost.Infrastructure.Tests.Interchange;

public class OpenApiHarExportTests
{
    private static readonly HttpRequestDefinition GetUser = new(
        "Get user",
        RequestMethod.Get,
        "{{baseUrl}}/users/:id",
        [new KeyValueEntry("expand", "orders"), new KeyValueEntry("debug", "1", Enabled: false)],
        [new KeyValueEntry("X-Tenant", "acme"), new KeyValueEntry("Accept", "application/json")],
        new BearerAuth("{{token}}"))
    {
        PathParams = [new KeyValueEntry("id", "42")],
        Description = "Reads a user.",
    };

    private static readonly HttpRequestDefinition CreateUser = new(
        "Create user",
        RequestMethod.Post,
        "{{baseUrl}}/users",
        auth: new ApiKeyAuth("X-Key", "{{key}}"),
        body: new RequestBody(BodyType.Json, "{\"name\": \"Ada\"}"));

    private static readonly HttpRequestDefinition Health = new("Health", RequestMethod.Get, "https://status.test/health", auth: RequestAuth.None);

    private static readonly RequestCollection Collection = new(
        new CollectionFolder("my-api", [new CollectionFolder("users", [], [GetUser, CreateUser])], [Health]),
        [new EnvironmentDefinition("Dev", [new EnvironmentVariable("baseUrl", "https://dev.test/v1")])]);

    [Fact]
    public void OpenApi_WritesOperationsServersAndSecurity()
    {
        var output = new OpenApiExporter().Export(Collection);
        var document = JsonNode.Parse(output.Content)!;

        Assert.Equal("3.0.3", (string?)document["openapi"]);
        Assert.Equal("https://dev.test/v1", (string?)document["servers"]![0]!["url"]);

        var get = document["paths"]!["/users/{id}"]!["get"]!;
        Assert.Equal("get-user", (string?)get["operationId"]);
        Assert.Equal("users", (string?)get["tags"]![0]);
        var parameters = get["parameters"]!.AsArray();
        Assert.Contains(parameters, p => (string?)p!["name"] == "id" && (string?)p["in"] == "path" && (string?)p["example"] == "42");
        Assert.Contains(parameters, p => (string?)p!["name"] == "expand" && (string?)p["in"] == "query");
        Assert.DoesNotContain(parameters, p => (string?)p!["name"] is "debug" or "Accept");
        Assert.Equal("bearerAuth", get["security"]![0]!.AsObject().Single().Key);

        var post = document["paths"]!["/users"]!["post"]!;
        Assert.Equal("Ada", (string?)post["requestBody"]!["content"]!["application/json"]!["example"]!["name"]);
        Assert.Equal("X-Key", (string?)document["components"]!["securitySchemes"]!["apiKey_x-key"]!["name"]);

        var health = document["paths"]!["/health"]!["get"]!;
        Assert.Equal("https://status.test", (string?)health["servers"]![0]!["url"]);
        Assert.Empty(health["security"]!.AsArray());

        Assert.Equal(output.Content, new OpenApiExporter().Export(Collection).Content);
    }

    [Fact]
    public void OpenApi_ExportReadsBackAsTheSameRequests()
    {
        var content = new OpenApiExporter().Export(Collection).Content;

        var parsed = new CollectionImporter().Parse(content, "api.openapi.json").Value.Collection;

        var all = parsed.Root.Requests.Concat(parsed.Root.Folders.SelectMany(f => f.Requests)).ToList();
        Assert.Contains(all, r => r.Name == "Get user" && r.Url == "{{baseUrl}}/users/:id");
        Assert.Contains(all, r => r.Name == "Create user" && r.Method == RequestMethod.Post);
    }

    [Fact]
    public void Har_WritesEachExchangeWithItsLatestResponse()
    {
        var command = new CurlCommand(
            "POST",
            "https://dev.test/v1/users?x=1",
            [KeyValuePair.Create("Content-Type", "application/json"), KeyValuePair.Create("Authorization", "Bearer ••••••")],
            new CurlTextBody("{}"));
        var response = new ResponseDto(201, "Created", [new KeyValueDto("Content-Type", "application/json")], "application/json", "{\"id\":1}", "text", 8, false, 120);
        var sentAt = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        var latest = new HistoryEntryDetailsDto(new HistoryEntryDto("e1", sentAt, "POST", command.Url, 201, "Created", 120, 8, "application/json"), response);
        var collection = Collection with { };
        collection = collection with
        {
            Exchanges =
            [
                new ExportedExchange("Create user", "requests/users/create-user.yaml", command, latest),
                new ExportedExchange("Get user", "requests/users/get-user.yaml", null, null, "The request uses variables that aren't defined: token."),
            ],
        };

        var output = new HarExporter().Export(collection);
        var entries = JsonNode.Parse(output.Content)!["log"]!["entries"]!.AsArray();

        var entry = Assert.Single(entries)!;
        Assert.Equal("2026-11-07T12:00:00.000Z", (string?)entry["startedDateTime"]);
        Assert.Equal("POST", (string?)entry["request"]!["method"]);
        Assert.Equal("1", (string?)entry["request"]!["queryString"]![0]!["value"]);
        Assert.Equal("{}", (string?)entry["request"]!["postData"]!["text"]);
        Assert.Equal(201, (int?)entry["response"]!["status"]);
        Assert.Equal("{\"id\":1}", (string?)entry["response"]!["content"]!["text"]);
        Assert.Contains(output.Warnings, w => w.Contains("get-user.yaml"));
        Assert.True(new HarExporter().NeedsExchanges);
    }
}
