using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Dynypost.Application.Locale;
using Dynypost.Desktop.Bridge;
using NSubstitute;

namespace Dynypost.Desktop.Tests.Bridge;

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

    [Fact]
    public async Task DateFormat_SendsEveryFieldTheClientReads()
    {
        var settings = Substitute.For<IRegionalSettings>();
        settings.DateCulture.Returns(CultureInfo.InvariantCulture);
        using var services = new ServiceCollection()
            .AddSingleton(settings)
            .AddTransient<GetDateFormat>()
            .BuildServiceProvider();
        var router = BridgeHandlers.Create(services, Substitute.For<IFileDialogs>(), Substitute.For<IAppWindow>());

        var json = await router.HandleAsync(JsonSerializer.Serialize(new { id = "2", type = "locale.dateFormat" }));
        var payload = JsonDocument.Parse(json!).RootElement.GetProperty("payload");

        // The names of models/messages.ts DateFormat, in the client's camel case.
        string[] fields =
        [
            "culture", "shortDatePattern", "shortTimePattern", "longTimePattern", "dateSeparator",
            "timeSeparator", "amDesignator", "pmDesignator", "monthNames", "abbreviatedMonthNames",
            "monthGenitiveNames", "abbreviatedMonthGenitiveNames", "dayNames", "abbreviatedDayNames",
        ];
        Assert.Equal(fields.Order(), payload.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(7, payload.GetProperty("abbreviatedDayNames").GetArrayLength());
    }
}
