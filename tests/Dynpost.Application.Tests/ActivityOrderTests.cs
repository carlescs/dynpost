using Dynpost.Application.Common;
using Dynpost.Application.Settings;
using NSubstitute;

namespace Dynpost.Application.Tests;

public class ActivityOrderTests
{
    private readonly IAppSettingsStore _store = Substitute.For<IAppSettingsStore>();
    private AppSettings _saved = new("/work/api", "dark");

    public ActivityOrderTests()
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
    public async Task Settings_DefaultToNoOrder()
    {
        var result = await new GetAppSettings(_store).ExecuteAsync();

        Assert.Empty(result.Value!.ActivityOrder);
    }

    [Fact]
    public async Task SettingAnOrder_SavesItAndKeepsOtherSettings()
    {
        var result = await new SetActivityOrder(_store).ExecuteAsync(["git", "explorer", "runs", "environments"]);

        Assert.Equal(["git", "explorer", "runs", "environments"], result.Value!.ActivityOrder);
        Assert.Equal("dark", result.Value.Theme);
        Assert.Equal("/work/api", _saved.LastWorkspacePath);
        Assert.Equal(["git", "explorer", "runs", "environments"], _saved.ActivityOrder);
    }

    [Fact]
    public async Task SettingTheSameOrder_LeavesTheSettingsUnchanged()
    {
        _saved = _saved with { ActivityOrder = ["git", "explorer"] };
        var before = _saved;

        await new SetActivityOrder(_store).ExecuteAsync(["git", "explorer"]);

        Assert.Same(before, _saved);
    }

    [Fact]
    public async Task AnEmptyOrder_RestoresTheDefault()
    {
        _saved = _saved with { ActivityOrder = ["git", "explorer"] };

        var result = await new SetActivityOrder(_store).ExecuteAsync([]);

        Assert.Empty(result.Value!.ActivityOrder);
        Assert.Null(_saved.ActivityOrder);
    }

    [Theory]
    [InlineData("git", "git")]
    [InlineData("git", "")]
    [InlineData(" ", "git")]
    public async Task InvalidOrders_AreRejected(string first, string second)
    {
        var result = await new SetActivityOrder(_store).ExecuteAsync([first, second]);

        Assert.Equal(SetActivityOrder.InvalidOrderCode, result.Error!.Code);
        _ = _store.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }
}
