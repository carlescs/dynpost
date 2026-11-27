using System.Diagnostics;
using Dynpost.Application.Settings;
using Photino.NET;

namespace Dynpost.Desktop.Window;

/// <summary>
/// The app's icon in the system tray. While the user has it on, minimizing or closing the window
/// (as they chose) hides the window instead, and the app keeps running: sends, load runs and the
/// MCP server carry on until the user quits from the tray, the menu or the shortcut.
/// </summary>
internal sealed class WindowTray : ISystemTray
{
    private readonly TrayIcon? _icon;
    private PhotinoWindow? _window;
    private Action _quitRequested = () => { };
    private volatile TraySettings _settings = TraySettings.Default;
    private volatile bool _created;
    private volatile bool _quitting;

    public WindowTray()
        : this(TrayIcon.ForCurrentPlatform())
    {
    }

    internal WindowTray(TrayIcon? icon) => _icon = icon;

    public bool IsSupported => _icon?.IsSupported ?? false;

    /// <summary>Whether closing the window hides it rather than ending the app, given the current settings.</summary>
    public bool ClosesToTray => !_quitting && _settings.CloseToTray && IsSupported;

    /// <summary>
    /// Follows <paramref name="window"/> (before it is created): hides it when it is minimized or
    /// closed, as the settings say. The tray's Quit calls <paramref name="quitRequested"/> with the
    /// window showing, so the UI can ask about unsaved changes before it quits.
    /// </summary>
    public void Attach(PhotinoWindow window, Action quitRequested)
    {
        _window = window;
        _quitRequested = quitRequested;
        window.RegisterWindowCreatedHandler((_, _) =>
        {
            _created = true;
            Safely(Update);
        });
        window.RegisterMinimizedHandler((_, _) =>
        {
            if (!_quitting && _settings.MinimizeToTray && IsSupported)
            {
                // Not from inside the minimize notification itself: queue it on the UI thread.
                _ = Task.Run(() => OnUiThread(Hide));
            }
        });
        window.RegisterWindowClosingHandler((_, _) =>
        {
            if (!ClosesToTray)
            {
                return false;
            }

            Safely(Hide);
            return true;
        });
    }

    public void Apply(TraySettings settings)
    {
        _settings = settings;
        if (_created)
        {
            OnUiThread(Update);
        }
    }

    /// <summary>Hides the window to the tray, as closing it would when the user chose that.</summary>
    public void HideWindow() => OnUiThread(Hide);

    /// <summary>The window is about to close for good: closing must no longer hide it.</summary>
    public void Quitting() => _quitting = true;

    /// <summary>Removes the icon as the app ends.</summary>
    public void Remove()
    {
        if (_icon is { IsShown: true } && _window is not null)
        {
            Safely(_icon.Remove);
        }
    }

    private void Update()
    {
        if (_icon is null || _window is null)
        {
            return;
        }

        if (_settings.ShowsIcon() && !_icon.IsShown)
        {
            _icon.Show(_window, new TrayMenu(
                () => Safely(Open),
                () => Safely(() =>
                {
                    Open();
                    _quitRequested();
                })));
        }
        else if (!_settings.ShowsIcon() && _icon.IsShown)
        {
            // Never strand a hidden window without its icon.
            Open();
            _icon.Remove();
        }
    }

    private void Hide()
    {
        if (_icon is null || _window is null)
        {
            return;
        }

        if (!_icon.IsShown)
        {
            Update();
        }

        if (_icon.IsVisible)
        {
            _icon.HideWindow(_window);
        }
        else if (!_window.Minimized)
        {
            // The desktop shows no tray icon to come back from: minimize rather than hide.
            _window.SetMinimized(true);
        }
    }

    private void Open()
    {
        if (_icon is not null && _window is not null)
        {
            _icon.ShowWindow(_window);
        }
    }

    private void OnUiThread(Action action)
    {
        if (_window is { } window)
        {
            window.Invoke(() => Safely(action));
        }
    }

    // A tray that fails must never end the app.
    private static void Safely(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Tray call failed: {ex}");
        }
    }
}
