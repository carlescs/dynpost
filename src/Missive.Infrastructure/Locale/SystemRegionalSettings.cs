using System.Globalization;
using Missive.Application.Locale;

namespace Missive.Infrastructure.Locale;

/// <summary>
/// Reads the date culture of the operating system. On Windows and macOS it is the current culture,
/// which follows the region format the user chose (on Windows, with their customized patterns). On
/// Linux .NET derives the current culture from the language (<c>LC_MESSAGES</c>), so the culture
/// is read from the locale variables that govern dates instead: <c>LC_ALL</c>, <c>LC_TIME</c>,
/// <c>LANG</c>.
/// </summary>
public sealed class SystemRegionalSettings(Func<string, string?> environment, bool isLinux) : IRegionalSettings
{
    public SystemRegionalSettings()
        : this(Environment.GetEnvironmentVariable, OperatingSystem.IsLinux())
    {
    }

    public CultureInfo DateCulture => (isLinux ? FromLocaleVariables() : null) ?? CultureInfo.CurrentCulture;

    private CultureInfo? FromLocaleVariables()
    {
        foreach (var name in new[] { "LC_ALL", "LC_TIME", "LANG" })
        {
            var value = environment(name);
            if (string.IsNullOrWhiteSpace(value)) continue;
            // The first variable that is set decides, as it does for the C library.
            return ToCulture(value);
        }
        return null;
    }

    // "es_ES.UTF-8@euro" → "es-ES". "C" and "POSIX" have no culture: fall back to the current one.
    private static CultureInfo? ToCulture(string locale)
    {
        var name = locale.Split('.', '@')[0].Replace('_', '-');
        if (name is "" or "C" or "POSIX") return null;
        try
        {
            var culture = CultureInfo.GetCultureInfo(name, predefinedOnly: true);
            return culture.Equals(CultureInfo.InvariantCulture) ? null : culture;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
