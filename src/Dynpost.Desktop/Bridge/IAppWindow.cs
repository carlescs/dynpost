namespace Dynpost.Desktop.Bridge;

/// <summary>The app's window, as the bridge handlers see it.</summary>
internal interface IAppWindow
{
    /// <summary>Closes the window, which ends the app (even when closing it would hide it to the tray).</summary>
    void Close();

    /// <summary>
    /// Hides the window to the system tray, as closing it does when the user chose that; does
    /// nothing otherwise. The app keeps running.
    /// </summary>
    void HideToTray();

    /// <summary>Which title bar the window has, on which platform, and whether it is maximized.</summary>
    WindowStateDto GetState();

    /// <summary>Minimizes the window.</summary>
    void Minimize();

    /// <summary>Maximizes the window, or restores it when it is maximized (zooms it on macOS).</summary>
    void ToggleMaximize();

    /// <summary>
    /// Moves the window with the mouse, whose button the user is holding down on the app's title
    /// bar; the operating system tracks the move (and snapping) until the button is released.
    /// </summary>
    void StartDrag();

    /// <summary>Resizes the window from <paramref name="edge"/>, like <see cref="StartDrag"/>.</summary>
    void StartResize(WindowEdge edge);
}

/// <summary>The edge or corner of the window a resize starts from.</summary>
internal enum WindowEdge
{
    Top,
    Bottom,
    Left,
    Right,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>The window's title bar and state, as the UI needs them to draw its own title bar.</summary>
/// <param name="Frame"><see cref="WindowFrames.Custom"/> when the app draws the title bar, else <see cref="WindowFrames.Native"/>.</param>
/// <param name="Platform"><c>windows</c>, <c>linux</c> or <c>macos</c>.</param>
/// <param name="Maximized">Whether the window is maximized.</param>
/// <param name="Tray">Whether the system can show the app's tray icon, so the tray settings apply.</param>
internal sealed record WindowStateDto(string Frame, string Platform, bool Maximized, bool Tray = false);

/// <summary>The kinds of title bar a window can have.</summary>
internal static class WindowFrames
{
    /// <summary>The operating system's title bar.</summary>
    public const string Native = "native";

    /// <summary>The app's header is the title bar.</summary>
    public const string Custom = "custom";
}
