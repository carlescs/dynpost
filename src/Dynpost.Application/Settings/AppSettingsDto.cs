using Dynpost.Application.History;

namespace Dynpost.Application.Settings;

/// <summary>The user settings the UI can read and change.</summary>
/// <param name="ActivityOrder">The order of the activity bar's views; empty for the default order.</param>
/// <param name="NativeTitleBar">Whether the window keeps the operating system's title bar (from the next start).</param>
/// <param name="Tray">Whether minimizing or closing the window hides it to the system tray; <c>null</c> reads as never.</param>
public sealed record AppSettingsDto(
    string Theme,
    HistorySettingsDto History,
    IReadOnlyList<string> ActivityOrder,
    bool NativeTitleBar = false,
    TraySettings? Tray = null)
{
    public static AppSettingsDto From(AppSettings settings) =>
        new(
            AppThemes.IsValid(settings.Theme) ? settings.Theme! : AppThemes.Light,
            HistorySettingsDto.From(settings.History ?? HistorySettings.Default),
            settings.ActivityOrder ?? [],
            settings.NativeTitleBar ?? false,
            settings.Tray ?? TraySettings.Default);
}

/// <summary>How responses are kept in the requests' history.</summary>
/// <param name="MaxBodyBytes">Bodies larger than this are cut off (text) or not kept (binary).</param>
public sealed record HistorySettingsDto(bool Enabled, int MaxEntries, long MaxBodyBytes)
{
    public static HistorySettingsDto From(HistorySettings settings)
    {
        var normalized = settings.Normalized();
        return new(normalized.Enabled, normalized.MaxEntries, normalized.MaxBodyBytes);
    }
}
