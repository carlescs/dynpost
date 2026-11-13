using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Missive.Application.Common;
using Missive.Application.Environments;
using Missive.Application.History;
using Missive.Application.Http;
using Missive.Application.Requests;
using Missive.Application.Sharing;
using Missive.Application.Workspaces;
using Missive.Desktop.Bridge;
using Missive.Desktop.Mcp;
using Missive.Domain.Environments;
using Missive.Domain.Requests;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Desktop.Tests.Mcp;

/// <summary>MCP messages through the real protocol, tools and use cases, with mocked ports.</summary>
public sealed class McpProtocolTests : IDisposable
{
    private const string Root = "/work/my-api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly ILocalOnlyFiles _localFiles = Substitute.For<ILocalOnlyFiles>();
    private readonly IHistoryStore _history = Substitute.For<IHistoryStore>();
    private readonly IEnvironmentStore _environments = Substitute.For<IEnvironmentStore>();
    private readonly ISecretStore _secrets = Substitute.For<ISecretStore>();
    private readonly List<string> _events = [];
    private readonly ServiceProvider _services;
    private readonly McpProtocol _protocol;

    public McpProtocolTests()
    {
        _services = new ServiceCollection()
            .AddSingleton(_repository)
            .AddSingleton(_localFiles)
            .AddSingleton(_history)
            .AddSingleton(_environments)
            .AddSingleton(_secrets)
            .AddSingleton<CurrentWorkspace>()
            .AddTransient<LoadRequest>()
            .AddTransient<DeleteRequest>()
            .AddTransient<LoadEnvironment>()
            .BuildServiceProvider();
        _services.GetRequiredService<CurrentWorkspace>().Open(Root);

        var events = new HostEvents();
        events.Connect(message =>
        {
            _events.Add(message);
            return Task.CompletedTask;
        });
        _protocol = new McpProtocol(McpTools.Create(_services, events), "1.2.3");
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task Initialize_AgreesOnTheClientsVersionAndOffersTools()
    {
        var result = await CallAsync("initialize", """{ "protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": { "name": "test", "version": "1" } }""");

        Assert.Equal("2025-06-18", result["protocolVersion"]!.GetValue<string>());
        Assert.NotNull(result["capabilities"]!["tools"]);
        Assert.Equal("missive", result["serverInfo"]!["name"]!.GetValue<string>());
        Assert.Equal("1.2.3", result["serverInfo"]!["version"]!.GetValue<string>());
    }

    [Fact]
    public async Task Initialize_WithAnUnknownVersion_OffersTheNewest()
    {
        var result = await CallAsync("initialize", """{ "protocolVersion": "1999-01-01" }""");

        Assert.Equal(McpProtocol.ProtocolVersions[0], result["protocolVersion"]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolsList_DescribesEachToolWithAnObjectSchemaWithoutReferences()
    {
        var result = await CallAsync("tools/list");

        var tools = result["tools"]!.AsArray();
        Assert.Contains(tools, tool => tool!["name"]!.GetValue<string>() == "send_request");
        Assert.DoesNotContain(tools, tool => tool!["name"]!.GetValue<string>().StartsWith("git_commit", StringComparison.Ordinal));
        Assert.All(tools, tool =>
        {
            Assert.Equal("object", tool!["inputSchema"]!["type"]!.GetValue<string>());
            Assert.DoesNotContain("$ref", tool["inputSchema"]!.ToJsonString());
        });
        var delete = tools.Single(tool => tool!["name"]!.GetValue<string>() == "delete_request")!;
        Assert.True(delete["annotations"]!["destructiveHint"]!.GetValue<bool>());
        Assert.Equal("path", Assert.Single(delete["inputSchema"]!["required"]!.AsArray())!.GetValue<string>());
    }

    [Fact]
    public async Task ReadRequest_ReturnsTheRequestAsJson()
    {
        var path = WorkspacePath.Parse("requests/get-user.yaml");
        _repository.FileExists(Root, path).Returns(true);
        _repository.ReadRequestAsync(Root, path, Arg.Any<CancellationToken>())
            .Returns(new HttpRequestDefinition("Get user", RequestMethod.Get, "{{baseUrl}}/users/1"));

        var result = await CallToolAsync("read_request", """{ "path": "requests/get-user.yaml" }""");

        Assert.False(result["isError"]!.GetValue<bool>());
        var request = JsonNode.Parse(Text(result))!;
        Assert.Equal("Get user", request["name"]!.GetValue<string>());
        Assert.Equal("{{baseUrl}}/users/1", request["url"]!.GetValue<string>());
        Assert.Empty(_events);
    }

    [Fact]
    public async Task AFailedUseCase_IsAToolErrorWithItsCodeAndMessage()
    {
        var result = await CallToolAsync("read_request", """{ "path": "requests/missing.yaml" }""");

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.StartsWith(ErrorCodes.NotFound + ":", Text(result));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "path": 5 }""")]
    [InlineData("""{ "path": "requests/a.yaml", "extra": true }""")]
    public async Task InvalidArguments_AreAToolErrorTheAssistantCanCorrect(string arguments)
    {
        var result = await CallToolAsync("read_request", arguments);

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.StartsWith("Invalid arguments for read_request", Text(result));
    }

    [Fact]
    public async Task DeleteRequest_TellsTheUiWhatChanged()
    {
        var path = WorkspacePath.Parse("requests/get-user.yaml");
        _repository.FileExists(Root, path).Returns(true);
        _repository.DeleteFile(Root, path).Returns(Unit.Value);

        var result = await CallToolAsync("delete_request", """{ "path": "requests/get-user.yaml" }""");

        Assert.False(result["isError"]!.GetValue<bool>(), Text(result));
        Assert.Equal("Done.", Text(result));
        var message = JsonNode.Parse(Assert.Single(_events))!;
        Assert.Equal("workspace.changed", message["event"]!.GetValue<string>());
        Assert.Equal("requests/get-user.yaml", Assert.Single(message["payload"]!["paths"]!.AsArray())!.GetValue<string>());
    }

    [Fact]
    public async Task ReadEnvironment_HidesSecretValues()
    {
        _environments.ReadAsync(Root, "dev.yaml", Arg.Any<CancellationToken>()).Returns(
            new EnvironmentDefinition("Development", [new EnvironmentVariable("baseUrl", "https://dev.test"), new EnvironmentVariable("token", "", Secret: true)]));
        _secrets.LoadAsync(Root, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>.Success(
                new Dictionary<string, IReadOnlyDictionary<string, string>>
                {
                    ["dev.yaml"] = new Dictionary<string, string> { ["token"] = "s3cret" },
                }));

        var result = await CallToolAsync("read_environment", """{ "id": "dev.yaml" }""");

        var text = Text(result);
        Assert.DoesNotContain("s3cret", text);
        Assert.Contains("https://dev.test", text);
        Assert.Contains("(secret)", text);
    }

    [Fact]
    public void Responses_ShownToTheAssistant_CutLongBodiesAndLeaveOutBinaryOnes()
    {
        var text = new ResponseDto(200, "OK", [], "text/plain", new string('a', McpTools.MaxBodyChars + 10), "text", McpTools.MaxBodyChars + 10, false, 5);
        var binary = text with { ContentType = "image/png", Body = "iVBORw0KGgo=", BodyEncoding = "base64", Size = 8 };

        var cut = McpTools.ForAssistant(text);
        var omitted = McpTools.ForAssistant(binary);

        Assert.Equal(McpTools.MaxBodyChars, cut.Body.Length);
        Assert.True(cut.Truncated);
        Assert.Equal("omitted", omitted.BodyEncoding);
        Assert.DoesNotContain("iVBOR", omitted.Body);
    }

    [Fact]
    public async Task UnknownTool_IsAnInvalidParamsError()
    {
        var response = await _protocol.HandleAsync(Request("tools/call", """{ "name": "git_push" }"""));

        Assert.Equal(McpProtocol.InvalidParams, response!["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task UnknownMethod_IsAMethodNotFoundError()
    {
        var response = await _protocol.HandleAsync(Request("resources/list"));

        Assert.Equal(McpProtocol.MethodNotFound, response!["error"]!["code"]!.GetValue<int>());
        Assert.Equal(1, response["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task Notifications_GetNoAnswer()
    {
        var response = await _protocol.HandleAsync("""{ "jsonrpc": "2.0", "method": "notifications/initialized" }""");

        Assert.Null(response);
    }

    [Fact]
    public async Task Batches_AreAnsweredMessageByMessage()
    {
        var response = await _protocol.HandleAsync(
            """[{ "jsonrpc": "2.0", "id": "a", "method": "ping" }, { "jsonrpc": "2.0", "method": "notifications/initialized" }, { "jsonrpc": "2.0", "id": "b", "method": "ping" }]""");

        var answers = response!.AsArray();
        Assert.Equal(["a", "b"], answers.Select(answer => answer!["id"]!.GetValue<string>()));
    }

    [Fact]
    public async Task InvalidJson_IsAParseError()
    {
        var response = await _protocol.HandleAsync("{ not json");

        Assert.Equal(McpProtocol.ParseError, response!["error"]!["code"]!.GetValue<int>());
    }

    private async Task<JsonNode> CallAsync(string method, string parameters = "{}")
    {
        var response = await _protocol.HandleAsync(Request(method, parameters));
        Assert.Null(response!["error"]);
        return response["result"]!;
    }

    private Task<JsonNode> CallToolAsync(string name, string arguments) =>
        CallAsync("tools/call", $$"""{ "name": "{{name}}", "arguments": {{arguments}} }""");

    private static string Text(JsonNode result) => result["content"]![0]!["text"]!.GetValue<string>();

    private static string Request(string method, string parameters = "{}") =>
        $$"""{ "jsonrpc": "2.0", "id": 1, "method": "{{method}}", "params": {{parameters}} }""";
}
