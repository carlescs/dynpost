namespace Missive.Application.Settings;

/// <summary>The user settings the UI can read and change.</summary>
public sealed record AppSettingsDto(string Theme)
{
    public static AppSettingsDto From(AppSettings settings) =>
        new(AppThemes.IsValid(settings.Theme) ? settings.Theme! : AppThemes.Light);
}
