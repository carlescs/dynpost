using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Dynpost.Application.Common;
using Dynpost.Application.Updates;
using Dynpost.Desktop.Bridge;
using NSubstitute;

namespace Dynpost.Desktop.Tests.Bridge;

/// <summary>The update messages, through the real router and use cases.</summary>
public sealed class UpdateBridgeTests : IDisposable
{
    private readonly IAppUpdater _updater = Substitute.For<IAppUpdater>();
    private readonly ServiceProvider _services;
    private readonly BridgeRouter _router;

    public UpdateBridgeTests()
    {
        _services = new ServiceCollection()
            .AddSingleton(_updater)
            .AddTransient<CheckForUpdate>()
            .AddTransient<RestartToUpdate>()
            .BuildServiceProvider();
        _router = BridgeHandlers.Create(_services, Substitute.For<IFileDialogs>(), Substitute.For<IAppWindow>());
        _updater.IsSupported.Returns(true);
        _updater.CurrentVersion.Returns("1.0.0");
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task UpdateCheck_ReportsTheDownloadedVersion()
    {
        _updater.FindUpdateAsync(Arg.Any<CancellationToken>()).Returns(Result<string?>.Success("1.1.0"));
        _updater.DownloadUpdateAsync(Arg.Any<CancellationToken>()).Returns(Result<Unit>.Success(Unit.Value));

        var response = await SendAsync("update.check");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var payload = response.GetProperty("payload");
        Assert.Equal("ready", payload.GetProperty("state").GetString());
        Assert.Equal("1.0.0", payload.GetProperty("currentVersion").GetString());
        Assert.Equal("1.1.0", payload.GetProperty("version").GetString());
    }

    [Fact]
    public async Task UpdateRestart_WithNothingDownloaded_ReportsTheError()
    {
        _updater.RestartToUpdate().Returns(Result<Unit>.Failure(ErrorCodes.UpdateFailed, "No update has been downloaded."));

        var response = await SendAsync("update.restart");

        Assert.False(response.GetProperty("ok").GetBoolean());
        Assert.Equal(ErrorCodes.UpdateFailed, response.GetProperty("error").GetProperty("code").GetString());
    }

    private async Task<JsonElement> SendAsync(string type)
    {
        var json = await _router.HandleAsync(JsonSerializer.Serialize(new { id = "1", type }));
        return JsonDocument.Parse(json!).RootElement;
    }
}
