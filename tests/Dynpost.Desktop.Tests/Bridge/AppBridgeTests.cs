using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Dynpost.Desktop.Bridge;
using NSubstitute;

namespace Dynpost.Desktop.Tests.Bridge;

/// <summary>The messages about the app window itself.</summary>
public sealed class AppBridgeTests
{
    [Fact]
    public async Task AppQuit_ClosesTheWindow()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var window = Substitute.For<IAppWindow>();
        var router = BridgeHandlers.Create(services, Substitute.For<IFileDialogs>(), window);

        var json = await router.HandleAsync(JsonSerializer.Serialize(new { id = "1", type = "app.quit" }));
        var response = JsonDocument.Parse(json!).RootElement;

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        window.Received(1).Close();
    }
}
