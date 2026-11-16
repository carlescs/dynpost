using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Dynpost.Application.About;
using Dynpost.Application.Common;
using Dynpost.Desktop.Bridge;
using NSubstitute;

namespace Dynpost.Desktop.Tests.Bridge;

/// <summary>The about messages, through the real router and use cases.</summary>
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
        var router = BridgeHandlers.Create(services, Substitute.For<IFileDialogs>(), Substitute.For<IAppWindow>());

        var json = await router.HandleAsync(JsonSerializer.Serialize(new { id = "1", type = "about.get" }));
        var response = JsonDocument.Parse(json!).RootElement;

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var payload = response.GetProperty("payload");
        Assert.Equal("1.2.3", payload.GetProperty("version").GetString());
        // Every field of the client's AboutInfo.
        Assert.Equal(
            ["homepage", "operatingSystem", "repository", "runtime", "version"],
            payload.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AboutOpenLink_OpensTheNamedLinkInTheBrowser()
    {
        var browser = Substitute.For<IWebBrowser>();
        browser.Open(Arg.Any<Uri>()).Returns(Unit.Value);
        using var services = new ServiceCollection()
            .AddSingleton(browser)
            .AddTransient<OpenAboutLink>()
            .BuildServiceProvider();
        var router = BridgeHandlers.Create(services, Substitute.For<IFileDialogs>(), Substitute.For<IAppWindow>());

        var json = await router.HandleAsync(JsonSerializer.Serialize(
            new { id = "1", type = "about.openLink", payload = new { link = "repository" } }));
        var response = JsonDocument.Parse(json!).RootElement;

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        browser.Received(1).Open(new Uri(AboutLinks.Repository));
    }
}
