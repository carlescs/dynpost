using Dynpost.Application.Common;
using Dynpost.Application.Settings;
using NSubstitute;

namespace Dynpost.Application.Tests;

public class ThemeTests
{
    private readonly IAppSettingsStore _store = Substitute.For<IAppSettingsStore>();

    public ThemeTests()
    {
        _store.LoadAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings("/work/api"));
        _store.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var update = call.Arg<Func<AppSettings, AppSettings>>();
                return Task.FromResult<Result<AppSettings>>(update(new AppSettings("/work/api")));
            });
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
        _ = _store.Received(1).UpdateAsync(
            Arg.Is<Func<AppSettings, AppSettings>>(update =>
                update(new AppSettings("/work/api")) == new AppSettings("/work/api", "dark")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownTheme_IsRejected()
    {
        var result = await new SetTheme(_store).ExecuteAsync("neon");

        Assert.Equal(SetTheme.InvalidThemeCode, result.Error!.Code);
        _ = _store.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }
}
