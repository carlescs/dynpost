using Dynpost.Application.Common;
using Dynpost.Application.Settings;
using NSubstitute;

namespace Dynpost.Application.Tests;

public class TitleBarTests
{
    private readonly IAppSettingsStore _store = Substitute.For<IAppSettingsStore>();
    private AppSettings _saved = new("/work/api");

    public TitleBarTests()
    {
        _store.LoadAsync(Arg.Any<CancellationToken>()).Returns(_ => _saved);
        _store.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _saved = call.Arg<Func<AppSettings, AppSettings>>()(_saved);
                return Task.FromResult<Result<AppSettings>>(_saved);
            });
    }

    [Fact]
    public async Task Settings_DefaultToTheAppsOwnTitleBar()
    {
        var result = await new GetAppSettings(_store).ExecuteAsync();

        Assert.False(result.Value!.NativeTitleBar);
    }

    [Fact]
    public async Task ChoosingTheNativeTitleBar_SavesItAndKeepsOtherSettings()
    {
        var result = await new SetTitleBar(_store).ExecuteAsync(native: true);

        Assert.True(result.Value!.NativeTitleBar);
        Assert.Equal(new AppSettings("/work/api", NativeTitleBar: true), _saved);
    }

    [Fact]
    public async Task ChoosingTheAppsTitleBarAgain_OmitsTheSetting()
    {
        _saved = new AppSettings("/work/api", NativeTitleBar: true);

        var result = await new SetTitleBar(_store).ExecuteAsync(native: false);

        Assert.False(result.Value!.NativeTitleBar);
        Assert.Equal(new AppSettings("/work/api"), _saved);
    }
}
