namespace Dynpost.Application.Locale;

/// <summary>Reads how the operating system formats dates and times, for the client to follow.</summary>
public sealed class GetDateFormat(IRegionalSettings settings)
{
    public DateFormatDto Execute() => DateFormatDto.From(settings.DateCulture);
}
