using System.Globalization;

namespace Dynpost.Application.Locale;

/// <summary>The regional settings of the operating system the app runs on.</summary>
public interface IRegionalSettings
{
    /// <summary>
    /// The culture whose date and time formats the user chose for the operating system, including
    /// any patterns they customized.
    /// </summary>
    CultureInfo DateCulture { get; }
}
