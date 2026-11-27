using System.Diagnostics;
using Dynpost.Desktop.Bridge;
using Photino.NET;

namespace Dynpost.Desktop.Window;

/// <summary>
/// The Photino window, with the app's header as its title bar unless the user chose the
/// operating system's. Bridge messages arrive off the UI thread, so every call moves onto it.
/// </summary>
internal sealed class AppWindow : IAppWindow
{
    private readonly PhotinoWindow _window;
    private readonly WindowChrome? _chrome;
    private readonly WindowTray _tray;

    private AppWindow(PhotinoWindow window, WindowChrome? chrome, WindowTray tray)
    {
        _window = window;
        _chrome = chrome;
        _tray = tray;
    }

    /// <summary>
    /// Sets up <paramref name="window"/> (before it is created) with the app's title bar, or the
    /// native one when <paramref name="nativeTitleBar"/> is set or the platform has no chrome, and
    /// the system tray icon <paramref name="tray"/> (attached to the window separately).
    /// </summary>
    public static AppWindow Create(PhotinoWindow window, bool nativeTitleBar, WindowTray tray)
    {
        var chrome = nativeTitleBar ? null : WindowChrome.ForCurrentPlatform();
        if (chrome is not null)
        {
            chrome.Configure(window);
            window.RegisterWindowCreatedHandler((_, _) => Safely(() => chrome.Created(window)));
        }

        return new AppWindow(window, chrome, tray);
    }

    /// <summary>Calls <paramref name="changed"/> with the new state when the window is maximized or restored.</summary>
    public void OnStateChanged(Action<WindowStateDto> changed)
    {
        // Windows reports "restored" on every step of a resize: only pass real changes on.
        bool? last = null;
        void Report(bool maximized)
        {
            if (last != maximized)
            {
                last = maximized;
                changed(State(maximized));
            }
        }

        _window.RegisterMaximizedHandler((_, _) => Report(true));
        _window.RegisterRestoredHandler((_, _) => Report(false));
    }

    public void Close()
    {
        _tray.Quitting();
        _window.Invoke(_window.Close);
    }

    public void HideToTray()
    {
        if (_tray.ClosesToTray)
        {
            _tray.HideWindow();
        }
    }

    public WindowStateDto GetState()
    {
        var maximized = false;
        OnUiThread(() =>
        {
            _chrome?.Created(_window);
            maximized = _window.Maximized;
        });
        return State(maximized);
    }

    public void Minimize() => OnUiThread(() => _window.SetMinimized(true));

    public void ToggleMaximize() => OnUiThread(() =>
    {
        if (_chrome is null)
        {
            _window.SetMaximized(!_window.Maximized);
        }
        else
        {
            _chrome.ToggleMaximize(_window);
        }
    });

    public void StartDrag()
    {
        if (_chrome is { } chrome)
        {
            OnUiThread(() => chrome.StartDrag(_window));
        }
    }

    public void StartResize(WindowEdge edge)
    {
        if (_chrome is { } chrome)
        {
            OnUiThread(() => chrome.StartResize(_window, edge));
        }
    }

    private WindowStateDto State(bool maximized) =>
        new(
            _chrome is null ? WindowFrames.Native : WindowFrames.Custom,
            _chrome?.Platform ?? WindowChrome.CurrentPlatform,
            maximized,
            _tray.IsSupported);

    private void OnUiThread(Action action) => _window.Invoke(() => Safely(action));

    // A window that can't be moved or restyled is still usable: never let native calls end the app.
    private static void Safely(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Window call failed: {ex}");
        }
    }
}
