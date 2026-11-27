using System.Text.Json;
using Dynpost.Application.Common;
using Dynpost.Application.Settings;
using Dynpost.Desktop.Bridge;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Dynpost.Desktop.Tests.Bridge;

/// <summary>The app's own title bar: window buttons, moving and resizing, and the setting that turns it off.</summary>
public sealed class WindowBridgeTests : IDisposable
{
    private readonly IAppWindow _window = Substitute.For<IAppWindow>();
    private readonly IAppSettingsStore _settings = Substitute.For<IAppSettingsStore>();
    private readonly ISystemTray _tray = Substitute.For<ISystemTray>();
    private readonly ServiceProvider _services;
    private readonly BridgeRouter _router;
    private AppSettings _saved = AppSettings.Default;

    public WindowBridgeTests()
    {
        _settings.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _saved = call.Arg<Func<AppSettings, AppSettings>>()(_saved);
                return Task.FromResult<Result<AppSettings>>(_saved);
            });
        _services = new ServiceCollection()
            .AddSingleton(_settings)
            .AddSingleton(_tray)
            .AddTransient<SetTitleBar>()
            .AddTransient<SetTraySettings>()
            .BuildServiceProvider();
        _router = BridgeHandlers.Create(_services, Substitute.For<IFileDialogs>(), _window);
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task Get_SendsEveryFieldTheClientReads()
    {
        _window.GetState().Returns(new WindowStateDto(WindowFrames.Custom, "windows", Maximized: true, Tray: true));

        var response = await SendAsync("window.get", "null");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var payload = response.GetProperty("payload");
        // The names of models/messages.ts WindowStateDto.
        Assert.Equal(["frame", "maximized", "platform", "tray"], payload.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("custom", payload.GetProperty("frame").GetString());
        Assert.True(payload.GetProperty("maximized").GetBoolean());
        Assert.True(payload.GetProperty("tray").GetBoolean());
    }

    [Theory]
    [InlineData("window.minimize")]
    [InlineData("window.toggleMaximize")]
    [InlineData("window.startDrag")]
    [InlineData("window.hide")]
    public async Task WindowButtons_CallTheWindow(string type)
    {
        var response = await SendAsync(type, "null");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        switch (type)
        {
            case "window.minimize": _window.Received(1).Minimize(); break;
            case "window.toggleMaximize": _window.Received(1).ToggleMaximize(); break;
            case "window.hide": _window.Received(1).HideToTray(); break;
            default: _window.Received(1).StartDrag(); break;
        }
    }

    [Theory]
    [InlineData("top", (int)WindowEdge.Top)]
    [InlineData("bottomRight", (int)WindowEdge.BottomRight)]
    [InlineData("topLeft", (int)WindowEdge.TopLeft)]
    public async Task StartResize_ResizesFromTheEdgeTheUiNames(string edge, int expectedEdge)
    {
        var expected = (WindowEdge)expectedEdge;
        var response = await SendAsync("window.startResize", $$"""{ "edge": "{{edge}}" }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        _window.Received(1).StartResize(expected);
    }

    [Theory]
    [InlineData("middle")]
    [InlineData("3")]
    public async Task StartResize_RejectsUnknownEdges(string edge)
    {
        var response = await SendAsync("window.startResize", $$"""{ "edge": "{{edge}}" }""");

        Assert.False(response.GetProperty("ok").GetBoolean());
        _window.DidNotReceiveWithAnyArgs().StartResize(default);
    }

    [Fact]
    public async Task SetTitleBar_RemembersTheChoice()
    {
        var response = await SendAsync("settings.setTitleBar", """{ "native": true }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.True(response.GetProperty("payload").GetProperty("nativeTitleBar").GetBoolean());
        Assert.True(_saved.NativeTitleBar);
    }

    [Fact]
    public async Task SetTray_RemembersTheChoiceAndAppliesIt()
    {
        _tray.IsSupported.Returns(true);

        var response = await SendAsync("settings.setTray", """{ "minimizeToTray": true, "closeToTray": false }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var tray = response.GetProperty("payload").GetProperty("tray");
        // The names of models/messages.ts TraySettingsDto.
        Assert.Equal(["closeToTray", "minimizeToTray"], tray.EnumerateObject().Select(p => p.Name).Order());
        Assert.True(tray.GetProperty("minimizeToTray").GetBoolean());
        Assert.Equal(new TraySettings(MinimizeToTray: true), _saved.Tray);
        _tray.Received(1).Apply(new TraySettings(MinimizeToTray: true));
    }

    private async Task<JsonElement> SendAsync(string type, string payload)
    {
        var json = await _router.HandleAsync($$"""{ "id": "1", "type": "{{type}}", "payload": {{payload}} }""");
        return JsonDocument.Parse(json!).RootElement;
    }
}
