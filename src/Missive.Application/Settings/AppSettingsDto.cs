using Missive.Application.History;

namespace Missive.Application.Settings;

/// <summary>The user settings the UI can read and change.</summary>
/// <param name="ActivityOrder">The order of the activity bar's views; empty for the default order.</param>
public sealed record AppSettingsDto(string Theme, HistorySettingsDto History, IReadOnlyList<string> ActivityOrder)
{
    public static AppSettingsDto From(AppSettings settings) =>
        new(
            AppThemes.IsValid(settings.Theme) ? settings.Theme! : AppThemes.Light,
            HistorySettingsDto.From(settings.History ?? HistorySettings.Default),
            settings.ActivityOrder ?? []);
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
