using System.Globalization;
using Dynypost.Application.Locale;
using NSubstitute;

namespace Dynypost.Application.Tests;

public sealed class DateFormatTests
{
    [Fact]
    public void Execute_ReportsTheSystemPatternsIncludingCustomizedOnes()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.ShortDatePattern = "yyyy-MM-dd";
        culture.DateTimeFormat.LongTimePattern = "HH.mm.ss";
        var settings = Substitute.For<IRegionalSettings>();
        settings.DateCulture.Returns(culture);

        var format = new GetDateFormat(settings).Execute();

        Assert.Equal("yyyy-MM-dd", format.ShortDatePattern);
        Assert.Equal("HH.mm.ss", format.LongTimePattern);
        Assert.Equal("AM", format.AmDesignator);
    }

    [Fact]
    public void Execute_ReportsTwelveMonthsAndSevenDays()
    {
        var settings = Substitute.For<IRegionalSettings>();
        settings.DateCulture.Returns(CultureInfo.InvariantCulture);

        var format = new GetDateFormat(settings).Execute();

        Assert.Equal(12, format.MonthNames.Count);
        Assert.Equal("December", format.MonthNames[11]);
        Assert.Equal(12, format.AbbreviatedMonthGenitiveNames.Count);
        Assert.Equal(7, format.DayNames.Count);
        Assert.Equal("Sunday", format.DayNames[0]);
    }

    [Fact]
    public void Execute_ReportsTheCultureNameSeparatorsAndDesignators()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.DateSeparator = ".";
        culture.DateTimeFormat.TimeSeparator = "h";
        culture.DateTimeFormat.PMDesignator = "pm";
        culture.DateTimeFormat.ShortTimePattern = "H:mm";
        var settings = Substitute.For<IRegionalSettings>();
        settings.DateCulture.Returns(culture);

        var format = new GetDateFormat(settings).Execute();

        Assert.Equal(culture.Name, format.Culture);
        Assert.Equal(".", format.DateSeparator);
        Assert.Equal("h", format.TimeSeparator);
        Assert.Equal("pm", format.PmDesignator);
        Assert.Equal("H:mm", format.ShortTimePattern);
    }

    [Fact]
    public void Execute_ReportsGenitiveMonthNamesApartFromTheOthers()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        string[] genitive = [.. Enumerable.Range(1, 12).Select(m => $"of month {m}"), ""];
        culture.DateTimeFormat.MonthGenitiveNames = genitive;
        culture.DateTimeFormat.AbbreviatedMonthGenitiveNames = [.. genitive.Select(n => n.Length > 0 ? n[..5] : n)];
        var settings = Substitute.For<IRegionalSettings>();
        settings.DateCulture.Returns(culture);

        var format = new GetDateFormat(settings).Execute();

        Assert.Equal("January", format.MonthNames[0]);
        Assert.Equal("of month 1", format.MonthGenitiveNames[0]);
        Assert.Equal("of month 12", format.MonthGenitiveNames[11]);
        Assert.Equal("of mo", format.AbbreviatedMonthGenitiveNames[0]);
        Assert.Equal(12, format.MonthGenitiveNames.Count);
    }

    [Fact]
    public void Execute_ReadsTheCultureEachTime()
    {
        var settings = Substitute.For<IRegionalSettings>();
        var first = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        first.DateTimeFormat.ShortDatePattern = "dd/MM/yyyy";
        var second = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        second.DateTimeFormat.ShortDatePattern = "yyyy-MM-dd";
        settings.DateCulture.Returns(first, second);
        var useCase = new GetDateFormat(settings);

        Assert.Equal("dd/MM/yyyy", useCase.Execute().ShortDatePattern);
        Assert.Equal("yyyy-MM-dd", useCase.Execute().ShortDatePattern);
    }
}
