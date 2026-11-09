using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Missive.Application.About;
using Missive.Desktop.Bridge;
using NSubstitute;

namespace Missive.Desktop.Tests.Bridge;

/// <summary>The about message, through the real router and use case.</summary>
public sealed class AboutBridgeTests
{
    [Fact]
    public async Task AboutGet_ReportsTheVersionAndPlatform()
    {
        var info = Substitute.For<IAppInfo>();
        info.Version.Returns("1.2.3");
        info.Runtime.Returns(".NET 10");
        info.OperatingSystem.Returns("Linux");
        using var services = new ServiceCollection()
            .AddSingleton(info)
            .AddTransient<GetAboutInfo>()
            .BuildServiceProvider();
        var router = BridgeHandlers.Create(services, Substitute.For<IFileDialogs>());

        var json = await router.HandleAsync(JsonSerializer.Serialize(new { id = "1", type = "about.get" }));
        var response = JsonDocument.Parse(json!).RootElement;

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Equal("1.2.3", response.GetProperty("payload").GetProperty("version").GetString());
    }
}
