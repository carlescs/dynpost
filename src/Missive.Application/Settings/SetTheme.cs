using Missive.Application.Common;

namespace Missive.Application.Settings;

/// <summary>Remembers the color theme the user picked.</summary>
public sealed class SetTheme(IAppSettingsStore store)
{
    public const string InvalidThemeCode = "settings.invalid_theme";

    public async Task<Result<AppSettingsDto>> ExecuteAsync(string? theme, CancellationToken cancellationToken = default)
    {
        if (!AppThemes.IsValid(theme))
        {
            return new Error(InvalidThemeCode, $"Unknown theme '{theme}'; use light or dark.");
        }

        var saved = await store.UpdateAsync(settings => settings with { Theme = theme }, cancellationToken);
        return saved.IsSuccess ? AppSettingsDto.From(saved.Value) : saved.Error!;
    }
}
