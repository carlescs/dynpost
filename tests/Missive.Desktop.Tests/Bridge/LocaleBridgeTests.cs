using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Missive.Application.Locale;
using Missive.Desktop.Bridge;
using NSubstitute;

namespace Missive.Desktop.Tests.Bridge;

/// <summary>The date format message, through the real router and use case.</summary>
public sealed class LocaleBridgeTests
{
    [Fact]
    public async Task DateFormat_ReportsTheSystemPatterns()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.ShortDatePattern = "dd/MM/yyyy";
        var settings = Substitute.For<IRegionalSettings>();
        settings.DateCulture.Returns(culture);
        using var services = new ServiceCollection()
            .AddSingleton(settings)
            .AddTransient<GetDateFormat>()
            .BuildServiceProvider();
        var router = BridgeHandlers.Create(services, Substitute.For<IFileDialogs>(), Substitute.For<IAppWindow>());

        var json = await router.HandleAsync(JsonSerializer.Serialize(new { id = "1", type = "locale.dateFormat" }));
        var response = JsonDocument.Parse(json!).RootElement;

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var payload = response.GetProperty("payload");
        Assert.Equal("dd/MM/yyyy", payload.GetProperty("shortDatePattern").GetString());
        Assert.Equal(12, payload.GetProperty("monthNames").GetArrayLength());
    }
}
