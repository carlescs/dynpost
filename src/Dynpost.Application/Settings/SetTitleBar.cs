using Dynpost.Application.Common;

namespace Dynpost.Application.Settings;

/// <summary>
/// Remembers whether the window uses the operating system's title bar or the app's own. The window
/// reads it when it opens, so the change applies the next time the app starts.
/// </summary>
public sealed class SetTitleBar(IAppSettingsStore store)
{
    public async Task<Result<AppSettingsDto>> ExecuteAsync(bool native, CancellationToken cancellationToken = default)
    {
        var saved = await store.UpdateAsync(
            settings => (settings.NativeTitleBar ?? false) == native
                ? settings
                : settings with { NativeTitleBar = native ? true : null },
            cancellationToken);
        return saved.IsSuccess ? AppSettingsDto.From(saved.Value) : saved.Error!;
    }
}
