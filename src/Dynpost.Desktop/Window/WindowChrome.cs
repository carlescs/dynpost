using Dynpost.Desktop.Bridge;
using Photino.NET;

namespace Dynpost.Desktop.Window;

/// <summary>
/// The platform's part of replacing the window's title bar with the app's own header: Photino can
/// only remove the frame, so each platform hands moving and resizing to the operating system.
/// Every member except <see cref="Configure"/> runs on the window's UI thread.
/// </summary>
internal abstract class WindowChrome
{
    /// <summary><c>windows</c>, <c>linux</c> or <c>macos</c>, as the UI knows the platform.</summary>
    public abstract string Platform { get; }

    /// <summary>Sets the window up before it is created.</summary>
    public virtual void Configure(PhotinoWindow window)
    {
    }

    /// <summary>
    /// Removes the native title bar once the window exists. Called again when the UI first asks
    /// for the window's state, in case the window wasn't ready yet; it must do its work once.
    /// </summary>
    public virtual void Created(PhotinoWindow window)
    {
    }

    /// <inheritdoc cref="IAppWindow.StartDrag"/>
    public abstract void StartDrag(PhotinoWindow window);

    /// <inheritdoc cref="IAppWindow.StartResize"/>
    public abstract void StartResize(PhotinoWindow window, WindowEdge edge);

    /// <inheritdoc cref="IAppWindow.ToggleMaximize"/>
    public virtual void ToggleMaximize(PhotinoWindow window) => window.SetMaximized(!window.Maximized);

    /// <summary>The chrome for the platform the app runs on, or <see langword="null"/> where it has none.</summary>
    public static WindowChrome? ForCurrentPlatform() =>
        OperatingSystem.IsWindows() ? new Win32Chrome()
        : OperatingSystem.IsLinux() ? new GtkChrome()
        : OperatingSystem.IsMacOS() ? new CocoaChrome()
        : null;

    /// <summary>The platform's name for the UI when the window keeps its native title bar.</summary>
    public static string CurrentPlatform =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
}
