using Photino.NET;

namespace Dynpost.Desktop.Window;

/// <summary>
/// The platform's part of keeping the app in the system tray: the icon with its menu, and hiding
/// and showing the window (Photino can minimize a window, but not hide it). Every member except
/// <see cref="IsSupported"/> runs on the window's UI thread.
/// </summary>
internal abstract class TrayIcon
{
    /// <summary>Whether this system can show the icon at all.</summary>
    public virtual bool IsSupported => true;

    /// <summary>
    /// Whether the icon is on screen, so a hidden window can be brought back from it. A window
    /// that would be left with no way back is minimized instead of hidden.
    /// </summary>
    public virtual bool IsVisible => IsShown;

    /// <summary>Whether <see cref="Show"/> added the icon and <see cref="Remove"/> hasn't removed it.</summary>
    public abstract bool IsShown { get; }

    /// <summary>
    /// Adds the icon. Activating it calls <paramref name="open"/>; its menu offers
    /// <paramref name="open"/> and <paramref name="quit"/>.
    /// </summary>
    /// <returns>Whether the icon was added.</returns>
    public abstract bool Show(PhotinoWindow window, TrayMenu menu);

    /// <summary>Removes the icon; does nothing when it isn't shown.</summary>
    public abstract void Remove();

    /// <summary>Hides the window, and its taskbar button or Dock icon.</summary>
    public abstract void HideWindow(PhotinoWindow window);

    /// <summary>Shows the window again (restored if it was minimized) and brings it to the front.</summary>
    public abstract void ShowWindow(PhotinoWindow window);

    /// <summary>The tray for the platform the app runs on, or <see langword="null"/> where it has none.</summary>
    public static TrayIcon? ForCurrentPlatform() =>
        OperatingSystem.IsWindows() ? new Win32Tray()
        : OperatingSystem.IsLinux() ? new GtkTray()
        : OperatingSystem.IsMacOS() ? new CocoaTray()
        : null;

    /// <summary>The app's icon for the tray: an .ico on Windows, a PNG elsewhere.</summary>
    protected static string IconPath(string extension) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", $"dynpost.{extension}");
}

/// <summary>What the tray icon's menu does, and the labels it shows.</summary>
/// <param name="Open">Shows the window (also what activating the icon does).</param>
/// <param name="Quit">Quits the app.</param>
internal sealed record TrayMenu(Action Open, Action Quit)
{
    public const string Tooltip = "Dynpost";
    public const string OpenLabel = "Show Dynpost";
    public const string QuitLabel = "Quit Dynpost";
}
