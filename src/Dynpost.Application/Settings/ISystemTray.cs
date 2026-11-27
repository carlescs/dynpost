namespace Dynpost.Application.Settings;

/// <summary>The app's icon in the system tray and what minimizing or closing the window does.</summary>
public interface ISystemTray
{
    /// <summary>Whether this system can show a tray icon (some Linux desktops can't).</summary>
    bool IsSupported { get; }

    /// <summary>Shows or removes the tray icon and changes what minimizing or closing the window does, at once.</summary>
    void Apply(TraySettings settings);
}
