using Dynpost.Application.Common;
using Dynpost.Application.Mcp;
using Dynpost.Application.Settings;
using NSubstitute;

namespace Dynpost.Application.Tests;

public class McpUseCaseTests
{
    private readonly IAppSettingsStore _store = Substitute.For<IAppSettingsStore>();
    private readonly IMcpServer _server = Substitute.For<IMcpServer>();
    private AppSettings _settings = new("/work/api");

    public McpUseCaseTests()
    {
        _store.LoadAsync(Arg.Any<CancellationToken>()).Returns(_ => _settings);
        _store.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _settings = call.Arg<Func<AppSettings, AppSettings>>()(_settings);
                return Task.FromResult<Result<AppSettings>>(_settings);
            });
        _server.StartAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _server.IsRunning.Returns(true);
                return Task.FromResult<Result<Unit>>(Unit.Value);
            });
    }

    [Fact]
    public async Task TheServer_IsOffByDefault()
    {
        var status = (await new GetMcpStatus(_store, _server).ExecuteAsync()).Value;

        Assert.False(status.Enabled);
        Assert.False(status.Running);
        Assert.Equal(McpSettings.DefaultPort, status.Port);
        Assert.Equal($"http://localhost:{McpSettings.DefaultPort}/mcp", status.Url);
        Assert.Null(status.Token);
    }

    [Fact]
    public async Task TurningItOn_MakesATokenAndStartsTheServer()
    {
        var status = (await new SetMcpServer(_store, _server).ExecuteAsync(true)).Value;

        Assert.True(status.Enabled);
        Assert.True(status.Running);
        Assert.Matches("^[0-9a-f]{64}$", status.Token);
        Assert.Equal(new McpSettings(true, McpSettings.DefaultPort, status.Token), _settings.Mcp);
        Assert.Equal("/work/api", _settings.LastWorkspacePath);
        await _server.Received(1).StartAsync(McpSettings.DefaultPort, status.Token!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TurningItOffAndOnAgain_KeepsTheToken()
    {
        _settings = _settings with { Mcp = new McpSettings(true, 5000, "abc") };

        await new SetMcpServer(_store, _server).ExecuteAsync(false);
        var status = (await new SetMcpServer(_store, _server).ExecuteAsync(true, 5001)).Value;

        Assert.Equal("abc", status.Token);
        Assert.Equal(5001, status.Port);
        await _server.Received(1).StopAsync();
        await _server.Received(1).StartAsync(5001, "abc", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(80)]
    [InlineData(70000)]
    public async Task APortOutOfRange_IsRejected(int port)
    {
        var result = await new SetMcpServer(_store, _server).ExecuteAsync(true, port);

        Assert.Equal(SetMcpServer.InvalidPortCode, result.Error!.Code);
        await _server.DidNotReceiveWithAnyArgs().StartAsync(default, default!, default);
    }

    [Fact]
    public async Task AServerThatCantStart_StaysOnWithTheReason()
    {
        _server.StartAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Result<Unit>>(new Error("mcp.start_failed", "in use")));
        _server.IsRunning.Returns(false);
        _server.Error.Returns("Could not listen on port 47823: in use");

        var status = (await new SetMcpServer(_store, _server).ExecuteAsync(true)).Value;

        Assert.True(status.Enabled);
        Assert.False(status.Running);
        Assert.Equal("Could not listen on port 47823: in use", status.Error);
    }

    [Fact]
    public async Task RegeneratingTheToken_RestartsARunningServerWithIt()
    {
        _settings = _settings with { Mcp = new McpSettings(true, 5000, "old") };

        var status = (await new RegenerateMcpToken(_store, _server).ExecuteAsync()).Value;

        Assert.NotEqual("old", status.Token);
        await _server.Received(1).StartAsync(5000, status.Token!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtStartup_TheServerStartsOnlyIfTurnedOn()
    {
        await new StartMcpServer(_store, _server).ExecuteAsync();
        await _server.DidNotReceiveWithAnyArgs().StartAsync(default, default!, default);

        _settings = _settings with { Mcp = new McpSettings(true, 5000, "abc") };
        await new StartMcpServer(_store, _server).ExecuteAsync();
        await _server.Received(1).StartAsync(5000, "abc", Arg.Any<CancellationToken>());
    }
}
