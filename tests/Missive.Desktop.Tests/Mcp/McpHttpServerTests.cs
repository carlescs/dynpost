using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Missive.Application.About;
using Missive.Desktop.Bridge;
using Missive.Desktop.Mcp;
using NSubstitute;

namespace Missive.Desktop.Tests.Mcp;

/// <summary>The MCP server over real HTTP on a free local port.</summary>
public sealed class McpHttpServerTests : IAsyncLifetime
{
    private const string Token = "0123456789abcdef";

    private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
    private readonly McpHttpServer _server;
    private readonly HttpClient _client = new();
    private readonly int _port = FreePort();

    public McpHttpServerTests()
    {
        var info = Substitute.For<IAppInfo>();
        info.Version.Returns("1.0.0");
        _server = new McpHttpServer(_services, new HostEvents(), info);
    }

    public async Task InitializeAsync() => Assert.True((await _server.StartAsync(_port, Token)).IsSuccess, _server.Error);

    public async Task DisposeAsync()
    {
        _server.Dispose();
        _client.Dispose();
        await _services.DisposeAsync();
    }

    [Fact]
    public async Task AnAuthorizedPost_IsAnsweredWithJson()
    {
        using var response = await PostAsync("""{ "jsonrpc": "2.0", "id": 7, "method": "ping" }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(7, body["id"]!.GetValue<int>());
        Assert.NotNull(body["result"]);
    }

    [Fact]
    public async Task ANotification_IsAccepted()
    {
        using var response = await PostAsync("""{ "jsonrpc": "2.0", "method": "notifications/initialized" }""");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-token")]
    public async Task WithoutTheToken_TheRequestIsRefused(string? token)
    {
        using var response = await PostAsync("""{ "jsonrpc": "2.0", "id": 1, "method": "ping" }""", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RequestsFromWebPages_AreRefused()
    {
        using var response = await PostAsync("""{ "jsonrpc": "2.0", "id": 1, "method": "ping" }""", Token, origin: "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_IsNotAllowed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task OtherPaths_AreNotFound()
    {
        using var response = await _client.PostAsync($"http://localhost:{_port}/other", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Stop_StopsListening()
    {
        await _server.StopAsync();

        Assert.False(_server.IsRunning);
        await Assert.ThrowsAsync<HttpRequestException>(() => PostAsync("""{ "jsonrpc": "2.0", "id": 1, "method": "ping" }"""));
    }

    [Fact]
    public async Task APortInUse_IsReported()
    {
        var info = Substitute.For<IAppInfo>();
        using var other = new McpHttpServer(_services, new HostEvents(), info);

        var started = await other.StartAsync(_port, Token);

        Assert.False(started.IsSuccess);
        Assert.False(other.IsRunning);
        Assert.Contains(_port.ToString(), other.Error);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("http://localhost:4200", true)]
    [InlineData("http://127.0.0.1:8080", true)]
    [InlineData("https://evil.example", false)]
    [InlineData("null", false)]
    public void Origins_FromThisComputerOnly(string? origin, bool allowed) =>
        Assert.Equal(allowed, McpHttpServer.IsAllowedOrigin(origin));

    private string Url => $"http://localhost:{_port}/mcp";

    private async Task<HttpResponseMessage> PostAsync(string json, string? token = Token, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return await _client.SendAsync(request);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
