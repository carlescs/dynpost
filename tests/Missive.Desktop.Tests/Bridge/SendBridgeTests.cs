using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Missive.Application.Common;
using Missive.Application.Cookies;
using Missive.Application.Environments;
using Missive.Application.Http;
using Missive.Application.Interchange;
using Missive.Application.OAuth2;
using Missive.Application.Requests;
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
    private readonly IFileDialogs _dialogs = Substitute.For<IFileDialogs>();
    private readonly IExternalFiles _files = Substitute.For<IExternalFiles>();
    private readonly ICookieJarStore _cookies = Substitute.For<ICookieJarStore>();
    private readonly CurrentWorkspace _current = new();
    private readonly ServiceProvider _services;
    private readonly BridgeRouter _router;

    public SendBridgeTests()
    {
        _services = new ServiceCollection()
            .AddSingleton(_sender)
            .AddSingleton<InFlightRequests>()
            .AddSingleton<ResponseBodies>()
            .AddSingleton(_current)
            .AddSingleton(_files)
            .AddSingleton(_cookies)
            .AddSingleton(TimeProvider.System)
            .AddSingleton(Substitute.For<IEnvironmentStore>())
            .AddSingleton(Substitute.For<ISecretStore>())
            .AddSingleton(Substitute.For<ICapturedValueStore>())
            .AddSingleton(Substitute.For<IWorkspaceStateStore>())
            .AddSingleton(Substitute.For<IOAuth2TokenStore>())
            .AddSingleton(Substitute.For<IAuthorizationBrowser>())
            .AddSingleton<OAuth2Tokens>()
            .AddTransient<ActiveEnvironment>()
            .AddTransient<SendRequest>()
            .AddTransient<InspectRequestFiles>()
            .AddTransient<CancelRequest>()
            .AddTransient<SaveResponseBody>()
            .AddTransient<ListCookies>()
            .AddTransient<DeleteCookie>()
            .AddTransient<ClearCookies>()
            .AddTransient<GetOAuth2Token>()
            .AddTransient<GetOAuth2TokenStatus>()
            .AddTransient<ClearOAuth2Token>()
            .AddTransient<CancelOAuth2SignIn>()
            .BuildServiceProvider();
        _router = BridgeHandlers.Create(_services, _dialogs);
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task PickRequestFile_ReturnsWorkspaceRelativePathsAndMarksAbsolutePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _current.Open(root);
        var workspaceFile = Path.Combine(root, "fixtures", "avatar.png");
        _dialogs.PickFileAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<FileFilter>>()).Returns(workspaceFile);

        var relative = await SendAsync("request.pickFile", "null");

        Assert.Equal("fixtures/avatar.png", relative.GetProperty("payload").GetProperty("path").GetString());
        Assert.False(relative.GetProperty("payload").GetProperty("isAbsolute").GetBoolean());

        var externalFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "avatar.png");
        _dialogs.PickFileAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<FileFilter>>()).Returns(externalFile);

        var absolute = await SendAsync("request.pickFile", "null");

        Assert.Equal(Path.GetFullPath(externalFile), absolute.GetProperty("payload").GetProperty("path").GetString());
        Assert.True(absolute.GetProperty("payload").GetProperty("isAbsolute").GetBoolean());
    }

    [Fact]
    public async Task PickRequestFiles_ReturnsEveryPickedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _current.Open(root);
        _dialogs.PickFilesAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<FileFilter>>())
            .Returns([Path.Combine(root, "a.png"), Path.Combine(root, "docs", "b.pdf")]);

        var picked = await SendAsync("request.pickFiles", """{ "multiple": true }""");

        Assert.Equal(
            ["a.png", "docs/b.pdf"],
            picked.GetProperty("payload").EnumerateArray().Select(file => file.GetProperty("path").GetString()));
    }

    [Fact]
    public async Task InspectRequestFiles_ReportsSizesMissingFilesAndPathsOutsideTheWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _current.Open(root);
        _files.FileSize(Path.Combine(Path.GetFullPath(root), "a.png")).Returns(1234L);

        var result = await SendAsync("request.inspectFiles", """{ "paths": ["a.png", "missing.png", "../out.png"] }""");

        var files = result.GetProperty("payload").EnumerateArray().ToList();
        Assert.Equal(1234, files[0].GetProperty("size").GetInt64());
        Assert.True(files[0].GetProperty("exists").GetBoolean());
        Assert.False(files[1].GetProperty("exists").GetBoolean());
        Assert.Equal(JsonValueKind.Null, files[1].GetProperty("error").ValueKind);
        Assert.Contains("inside the workspace", files[2].GetProperty("error").GetString());
    }

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

    [Fact]
    public async Task SaveBody_AsksWhereAndWritesTheBytesReceived()
    {
        byte[] bytes = [0x89, 0x50, 0x4e, 0x47];
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>()).Returns(new HttpResponseData(
            200, "OK", [KeyValuePair.Create("Content-Type", "image/png")], bytes, false, TimeSpan.Zero));
        _dialogs.PickSaveFileAsync(default!, default!).ReturnsForAnyArgs("/home/me/logo.png");
        _files.WriteBytesAsync(default!, default!, default).ReturnsForAnyArgs(Unit.Value);
        var sent = await SendAsync("request.send", $$"""{ "id": "s1", "request": {{RequestJson}} }""");
        Assert.Equal("1.png", sent.GetProperty("payload").GetProperty("fileName").GetString());

        var response = await SendAsync("response.saveBody", """{ "id": "s1" }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Equal("/home/me/logo.png", response.GetProperty("payload").GetProperty("path").GetString());
        await _dialogs.Received(1).PickSaveFileAsync(
            "Save response as 1.png",
            Arg.Is<IReadOnlyList<FileFilter>>(f => f[0].Patterns.Single() == "*.png"));
        await _files.Received(1).WriteBytesAsync("/home/me/logo.png", bytes, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveBody_CancelledDialog_ReturnsNull()
    {
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>()).Returns(new HttpResponseData(
            200, "OK", [], [1], false, TimeSpan.Zero));
        _dialogs.PickSaveFileAsync(default!, default!).ReturnsForAnyArgs((string?)null);
        await SendAsync("request.send", $$"""{ "id": "s1", "request": {{RequestJson}} }""");

        var response = await SendAsync("response.saveBody", """{ "id": "s1" }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Equal(JsonValueKind.Null, response.GetProperty("payload").ValueKind);
        await _files.DidNotReceiveWithAnyArgs().WriteBytesAsync(default!, default!, default);
    }

    [Fact]
    public async Task SaveBody_UnknownResponse_IsNotFound()
    {
        var response = await SendAsync("response.saveBody", """{ "id": "gone" }""");

        Assert.Equal(ErrorCodes.NotFound, response.GetProperty("error").GetProperty("code").GetString());
        await _dialogs.DidNotReceiveWithAnyArgs().PickSaveFileAsync(default!, default!);
    }

    [Fact]
    public async Task Cookies_AreListedDeletedAndCleared()
    {
        _current.Open("/work/api");
        IReadOnlyDictionary<string, IReadOnlyList<StoredCookie>> jars = new Dictionary<string, IReadOnlyList<StoredCookie>>
        {
            ["dev.yaml"] = [new StoredCookie("a", "1", "x.test", "/"), new StoredCookie("b", "2", "x.test", "/", Secure: true)],
        };
        _cookies.LoadAsync("/work/api", Arg.Any<CancellationToken>())
            .Returns(_ => Result<IReadOnlyDictionary<string, IReadOnlyList<StoredCookie>>>.Success(jars));
        _cookies.SaveAsync("/work/api", Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<StoredCookie>>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                jars = call.Arg<IReadOnlyDictionary<string, IReadOnlyList<StoredCookie>>>();
                return Result<Unit>.Success(Unit.Value);
            });

        var listed = await SendAsync("cookie.list", """{ "environment": "dev.yaml" }""");
        Assert.Equal(2, listed.GetProperty("payload").GetArrayLength());
        Assert.True(listed.GetProperty("payload")[1].GetProperty("secure").GetBoolean());

        var deleted = await SendAsync("cookie.delete", """{ "environment": "dev.yaml", "name": "a", "domain": "x.test", "path": "/" }""");
        Assert.Equal("b", deleted.GetProperty("payload").EnumerateArray().Single().GetProperty("name").GetString());

        var cleared = await SendAsync("cookie.clear", """{ "environment": "dev.yaml" }""");
        Assert.True(cleared.GetProperty("ok").GetBoolean(), cleared.ToString());
        Assert.Empty(jars);
    }

    [Fact]
    public async Task OAuth2Token_IsObtainedReportedAndCleared()
    {
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>()).Returns(new HttpResponseData(
            200, "OK", [KeyValuePair.Create("Content-Type", "application/json")],
            Encoding.UTF8.GetBytes("""{"access_token":"tok","expires_in":60,"refresh_token":"r"}"""), false, TimeSpan.Zero));
        const string auth = """
            { "auth": { "type": "oauth2", "grant": "clientCredentials", "tokenUrl": "https://auth.test/token", "clientId": "app", "scope": "read" } }
            """;

        var none = await SendAsync("oauth2.token.status", auth);
        Assert.True(none.GetProperty("ok").GetBoolean(), none.ToString());
        Assert.Equal(JsonValueKind.Null, none.GetProperty("payload").ValueKind);

        var got = await SendAsync("oauth2.token.get", auth);
        Assert.True(got.GetProperty("ok").GetBoolean(), got.ToString());
        Assert.True(got.GetProperty("payload").GetProperty("refreshable").GetBoolean());
        Assert.False(got.GetProperty("payload").GetProperty("expired").GetBoolean());
        Assert.Equal("read", got.GetProperty("payload").GetProperty("scope").GetString());
        Assert.False(got.GetProperty("payload").TryGetProperty("accessToken", out _));

        var status = await SendAsync("oauth2.token.status", auth);
        Assert.Equal(JsonValueKind.Object, status.GetProperty("payload").ValueKind);

        Assert.True((await SendAsync("oauth2.token.clear", auth)).GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, (await SendAsync("oauth2.token.status", auth)).GetProperty("payload").ValueKind);
    }

    [Fact]
    public async Task OAuth2Token_IncompleteSettings_AreReportedOnGet()
    {
        var got = await SendAsync("oauth2.token.get", """{ "auth": { "type": "oauth2", "grant": "authorizationCode" } }""");

        Assert.False(got.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_request", got.GetProperty("error").GetProperty("code").GetString());
    }

    private async Task<JsonElement> SendAsync(string type, string payload)
    {
        var json = await _router.HandleAsync($$"""{ "id": "1", "type": "{{type}}", "payload": {{payload}} }""");
        Assert.NotNull(json);
        return JsonDocument.Parse(json).RootElement;
    }
}
