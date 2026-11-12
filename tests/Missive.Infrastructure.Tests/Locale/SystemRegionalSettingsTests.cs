using System.Globalization;
using Missive.Infrastructure.Locale;

namespace Missive.Infrastructure.Tests.Locale;

public sealed class SystemRegionalSettingsTests
{
    private static SystemRegionalSettings Linux(params (string Name, string Value)[] variables)
    {
        var values = variables.ToDictionary(v => v.Name, v => v.Value);
        return new SystemRegionalSettings(name => values.GetValueOrDefault(name), isLinux: true);
    }

    [Fact]
    public void Linux_UsesTheTimeLocaleRatherThanTheLanguage()
    {
        var settings = Linux(("LANG", "en_US.UTF-8"), ("LC_TIME", "de_DE.UTF-8"), ("LC_MESSAGES", "en_US.UTF-8"));

        Assert.Equal("de-DE", settings.DateCulture.Name);
    }

    [Fact]
    public void Linux_LcAllOverridesTheOthers()
    {
        var settings = Linux(("LC_ALL", "fr_FR.UTF-8@euro"), ("LC_TIME", "de_DE.UTF-8"));

        Assert.Equal("fr-FR", settings.DateCulture.Name);
    }

    [Fact]
    public void Linux_FallsBackToLang()
    {
        Assert.Equal("es-ES", Linux(("LANG", "es_ES.UTF-8")).DateCulture.Name);
    }

    [Theory]
    [InlineData("C")]
    [InlineData("POSIX")]
    [InlineData("C.UTF-8")]
    [InlineData("xx_NOPE")]
    public void Linux_FallsBackToTheCurrentCultureWithoutAUsableLocale(string locale)
    {
        Assert.Equal(CultureInfo.CurrentCulture, Linux(("LC_ALL", locale)).DateCulture);
    }

    [Fact]
    public void OtherSystems_UseTheCurrentCulture()
    {
        var settings = new SystemRegionalSettings(_ => "de_DE.UTF-8", isLinux: false);

        Assert.Equal(CultureInfo.CurrentCulture, settings.DateCulture);
    }

    [Fact]
    public void Linux_TheFirstVariableThatIsSetDecidesEvenWhenItHasNoCulture()
    {
        // As in the C library, LC_ALL=C wins over LC_TIME: the dates are not German.
        var settings = Linux(("LC_ALL", "C"), ("LC_TIME", "de_DE.UTF-8"));

        Assert.Equal(CultureInfo.CurrentCulture, settings.DateCulture);
    }

    [Fact]
    public void Linux_SkipsVariablesThatAreBlank()
    {
        var settings = Linux(("LC_ALL", ""), ("LC_TIME", "  "), ("LANG", "it_IT.UTF-8"));

        Assert.Equal("it-IT", settings.DateCulture.Name);
    }

    [Fact]
    public void Linux_UsesTheCurrentCultureWhenNoVariableIsSet()
    {
        Assert.Equal(CultureInfo.CurrentCulture, Linux().DateCulture);
    }

    [Theory]
    [InlineData("pt_BR", "pt-BR")]
    [InlineData("nl_NL@euro", "nl-NL")]
    [InlineData("ja_JP.eucJP", "ja-JP")]
    [InlineData("fr", "fr")]
    public void Linux_ReadsTheCultureFromEveryLocaleForm(string locale, string culture)
    {
        Assert.Equal(culture, Linux(("LC_TIME", locale)).DateCulture.Name);
    }

    [Fact]
    public void Linux_TheCultureCarriesTheSystemPatterns()
    {
        var format = Linux(("LC_TIME", "de_DE.UTF-8")).DateCulture.DateTimeFormat;

        Assert.Equal(CultureInfo.GetCultureInfo("de-DE").DateTimeFormat.ShortDatePattern, format.ShortDatePattern);
    }
}
