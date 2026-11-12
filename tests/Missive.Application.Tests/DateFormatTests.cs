using System.Globalization;
using Missive.Application.Locale;
using NSubstitute;

namespace Missive.Application.Tests;

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
}
