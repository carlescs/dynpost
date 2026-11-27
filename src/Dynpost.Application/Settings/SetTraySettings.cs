using Dynpost.Application.Common;

namespace Dynpost.Application.Settings;

/// <summary>
/// Remembers whether minimizing or closing the window hides it to the system tray, and applies the
/// choice right away.
/// </summary>
public sealed class SetTraySettings(IAppSettingsStore store, ISystemTray tray)
{
    public const string UnsupportedCode = "settings.tray_unsupported";

    public async Task<Result<AppSettingsDto>> ExecuteAsync(TraySettings settings, CancellationToken cancellationToken = default)
    {
        if (settings.ShowsIcon() && !tray.IsSupported)
        {
            return new Error(UnsupportedCode, "This desktop can't show a tray icon.");
        }

        var saved = await store.UpdateAsync(
            current => (current.Tray ?? TraySettings.Default) == settings
                ? current
                : current with { Tray = settings.ShowsIcon() ? settings : null },
            cancellationToken);
        if (!saved.IsSuccess)
        {
            return saved.Error!;
        }

        tray.Apply(saved.Value.Tray ?? TraySettings.Default);
        return AppSettingsDto.From(saved.Value);
    }
}

/// <summary>Shows the tray icon at startup when the user turned it on.</summary>
public sealed class StartSystemTray(IAppSettingsStore store, ISystemTray tray)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var settings = (await store.LoadAsync(cancellationToken)).Tray ?? TraySettings.Default;
        if (settings.ShowsIcon() && tray.IsSupported)
        {
            tray.Apply(settings);
        }
    }
}
