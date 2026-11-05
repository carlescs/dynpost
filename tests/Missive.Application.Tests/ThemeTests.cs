using Missive.Application.Common;
using Missive.Application.Settings;
using NSubstitute;

namespace Missive.Application.Tests;

public class ThemeTests
{
    private readonly IAppSettingsStore _store = Substitute.For<IAppSettingsStore>();

    public ThemeTests()
    {
        _store.LoadAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings("/work/api"));
        _store.SaveAsync(default!, default).ReturnsForAnyArgs(Result<Unit>.Success(Unit.Value));
    }

    [Fact]
    public async Task Settings_DefaultToLight()
    {
        var result = await new GetAppSettings(_store).ExecuteAsync();

        Assert.Equal("light", result.Value!.Theme);
    }

    [Fact]
    public async Task SettingTheDarkTheme_SavesItAndKeepsOtherSettings()
    {
        var result = await new SetTheme(_store).ExecuteAsync("dark");

        Assert.Equal("dark", result.Value!.Theme);
        _ = _store.Received(1).SaveAsync(
            Arg.Is<AppSettings>(s => s.Theme == "dark" && s.LastWorkspacePath == "/work/api"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownTheme_IsRejected()
    {
        var result = await new SetTheme(_store).ExecuteAsync("neon");

        Assert.Equal(SetTheme.InvalidThemeCode, result.Error!.Code);
        _ = _store.DidNotReceiveWithAnyArgs().SaveAsync(default!, default);
    }
}
