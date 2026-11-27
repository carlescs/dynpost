namespace Dynpost.Application.Settings;

/// <summary>
/// Whether the app keeps running in the system tray (the notification area, or the menu bar on
/// macOS) with its window hidden, so requests, load runs and the MCP server carry on. Kept in the
/// per-user <see cref="AppSettings"/>; the tray icon shows while either option is on.
/// </summary>
/// <param name="MinimizeToTray">Minimizing the window hides it to the tray instead of the taskbar or Dock.</param>
/// <param name="CloseToTray">Closing the window hides it to the tray; quitting (from the tray, the menu or the shortcut) ends the app.</param>
public sealed record TraySettings(bool MinimizeToTray = false, bool CloseToTray = false)
{
    public static TraySettings Default { get; } = new();

    /// <summary>Whether the tray icon shows (a method, so it isn't saved or sent as a field).</summary>
    public bool ShowsIcon() => MinimizeToTray || CloseToTray;
}
