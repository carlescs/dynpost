using System.Globalization;

namespace Dynpost.Application.Locale;

// Wire format of the date format message. Keep in sync with src/Dynpost.Client/src/app/models/messages.ts.

/// <summary>
/// How the operating system formats dates and times: .NET custom format patterns plus the names
/// and separators they refer to, so the client can format dates exactly as the system does.
/// </summary>
public sealed record DateFormatDto(
    string Culture,
    string ShortDatePattern,
    string ShortTimePattern,
    string LongTimePattern,
    string DateSeparator,
    string TimeSeparator,
    string AmDesignator,
    string PmDesignator,
    IReadOnlyList<string> MonthNames,
    IReadOnlyList<string> AbbreviatedMonthNames,
    IReadOnlyList<string> MonthGenitiveNames,
    IReadOnlyList<string> AbbreviatedMonthGenitiveNames,
    IReadOnlyList<string> DayNames,
    IReadOnlyList<string> AbbreviatedDayNames)
{
    public static DateFormatDto From(CultureInfo culture)
    {
        var format = culture.DateTimeFormat;
        return new DateFormatDto(
            culture.Name,
            format.ShortDatePattern,
            format.ShortTimePattern,
            format.LongTimePattern,
            format.DateSeparator,
            format.TimeSeparator,
            format.AMDesignator,
            format.PMDesignator,
            Months(format.MonthNames),
            Months(format.AbbreviatedMonthNames),
            Months(format.MonthGenitiveNames),
            Months(format.AbbreviatedMonthGenitiveNames),
            format.DayNames,
            format.AbbreviatedDayNames);
    }

    // .NET month name arrays have a 13th entry for 13-month calendars; the Gregorian one is empty.
    private static string[] Months(string[] names) => names[..12];
}
