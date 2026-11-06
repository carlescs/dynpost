using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Missive.Application.Common;
using Missive.Application.Environments;
using Missive.Application.Http;
using Missive.Application.Workspaces;
using Missive.Desktop.Bridge;
using Missive.Domain.Http;
using NSubstitute;

namespace Missive.Desktop.Tests.Bridge;

/// <summary>The request.send and request.cancel messages, through the real router and use cases.</summary>
public sealed class SendBridgeTests : IDisposable
{
    private const string RequestJson = """
        {
          "name": "Get user", "method": "GET", "url": "https://api.example.com/users/1",
          "params": [], "headers": [{ "key": "Accept", "value": "application/json", "enabled": true }],
          "auth": { "type": "none" }, "body": { "type": "none", "content": "" }
        }
        """;

    private readonly IHttpSender _sender = Substitute.For<IHttpSender>();
    private readonly ServiceProvider _services;
    private readonly BridgeRouter _router;

    public SendBridgeTests()
    {
        _services = new ServiceCollection()
            .AddSingleton(_sender)
            .AddSingleton<InFlightRequests>()
            .AddSingleton<CurrentWorkspace>()
            .AddSingleton(Substitute.For<IEnvironmentStore>())
            .AddSingleton(Substitute.For<ISecretStore>())
            .AddSingleton(Substitute.For<IWorkspaceStateStore>())
            .AddTransient<ActiveEnvironment>()
            .AddTransient<SendRequest>()
            .AddTransient<CancelRequest>()
            .BuildServiceProvider();
        _router = BridgeHandlers.Create(_services, Substitute.For<IFileDialogs>());
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task Send_ReturnsTheResponse()
    {
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>()).Returns(new HttpResponseData(
            201,
            "Created",
            [KeyValuePair.Create("Content-Type", "application/json")],
            Encoding.UTF8.GetBytes("""{"id":1}"""),
            false,
            TimeSpan.FromMilliseconds(42)));

        var response = await SendAsync("request.send", $$"""{ "id": "s1", "request": {{RequestJson}} }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var payload = response.GetProperty("payload");
        Assert.Equal(201, payload.GetProperty("status").GetInt32());
        Assert.Equal("Created", payload.GetProperty("statusText").GetString());
        Assert.Equal("""{"id":1}""", payload.GetProperty("body").GetString());
        Assert.Equal("text", payload.GetProperty("bodyEncoding").GetString());
        Assert.Equal(42, payload.GetProperty("durationMs").GetInt64());
        Assert.Equal("Content-Type", payload.GetProperty("headers")[0].GetProperty("key").GetString());
        await _sender.Received(1).SendAsync(
            Arg.Is<OutgoingRequest>(r => r.Url.AbsoluteUri == "https://api.example.com/users/1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Send_NetworkError_IsReported()
    {
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<HttpResponseData>.Failure(HttpErrors.NetworkError("api.example.com", "Connection refused")));

        var response = await SendAsync("request.send", $$"""{ "id": "s1", "request": {{RequestJson}} }""");

        Assert.False(response.GetProperty("ok").GetBoolean());
        Assert.Equal(HttpErrors.NetworkErrorCode, response.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Cancel_UnknownId_ReturnsFalse()
    {
        var response = await SendAsync("request.cancel", """{ "id": "nope" }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.False(response.GetProperty("payload").GetBoolean());
    }

    private async Task<JsonElement> SendAsync(string type, string payload)
    {
        var json = await _router.HandleAsync($$"""{ "id": "1", "type": "{{type}}", "payload": {{payload}} }""");
        Assert.NotNull(json);
        return JsonDocument.Parse(json).RootElement;
    }
}
