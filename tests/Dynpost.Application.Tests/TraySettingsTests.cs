using Dynpost.Application.Common;
using Dynpost.Application.Settings;
using NSubstitute;

namespace Dynpost.Application.Tests;

public class TraySettingsTests
{
    private readonly IAppSettingsStore _store = Substitute.For<IAppSettingsStore>();
    private readonly ISystemTray _tray = Substitute.For<ISystemTray>();
    private AppSettings _saved = new("/work/api");

    public TraySettingsTests()
    {
        _tray.IsSupported.Returns(true);
        _store.LoadAsync(Arg.Any<CancellationToken>()).Returns(_ => _saved);
        _store.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _saved = call.Arg<Func<AppSettings, AppSettings>>()(_saved);
                return Task.FromResult<Result<AppSettings>>(_saved);
            });
    }

    [Fact]
    public async Task Settings_DefaultToNoTray()
    {
        var result = await new GetAppSettings(_store).ExecuteAsync();

        Assert.Equal(new TraySettings(), result.Value!.Tray);
    }

    [Fact]
    public async Task TurningItOn_SavesItKeepsOtherSettingsAndShowsTheIcon()
    {
        var settings = new TraySettings(MinimizeToTray: true, CloseToTray: true);

        var result = await new SetTraySettings(_store, _tray).ExecuteAsync(settings);

        Assert.Equal(settings, result.Value!.Tray);
        Assert.Equal(new AppSettings("/work/api", Tray: settings), _saved);
        _tray.Received(1).Apply(settings);
    }

    [Fact]
    public async Task TurningItOff_OmitsTheSettingAndRemovesTheIcon()
    {
        _saved = new AppSettings("/work/api", Tray: new TraySettings(CloseToTray: true));

        var result = await new SetTraySettings(_store, _tray).ExecuteAsync(new TraySettings());

        Assert.Equal(new TraySettings(), result.Value!.Tray);
        Assert.Equal(new AppSettings("/work/api"), _saved);
        _tray.Received(1).Apply(new TraySettings());
    }

    [Fact]
    public async Task WhereTheSystemHasNoTray_RefusesToTurnItOn()
    {
        _tray.IsSupported.Returns(false);

        var result = await new SetTraySettings(_store, _tray).ExecuteAsync(new TraySettings(MinimizeToTray: true));

        Assert.Equal(SetTraySettings.UnsupportedCode, result.Error!.Code);
        Assert.Null(_saved.Tray);
        _tray.DidNotReceiveWithAnyArgs().Apply(default!);
    }

    [Fact]
    public async Task AtStartup_ShowsTheIconWhenTheUserTurnedItOn()
    {
        var settings = new TraySettings(CloseToTray: true);
        _saved = new AppSettings(Tray: settings);

        await new StartSystemTray(_store, _tray).ExecuteAsync();

        _tray.Received(1).Apply(settings);
    }

    [Fact]
    public async Task AtStartup_LeavesTheTrayAloneWhenItIsOff()
    {
        await new StartSystemTray(_store, _tray).ExecuteAsync();

        _tray.DidNotReceiveWithAnyArgs().Apply(default!);
    }
}
