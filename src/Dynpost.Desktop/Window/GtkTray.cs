using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Photino.NET;

namespace Dynpost.Desktop.Window;

/// <summary>
/// Linux: a StatusNotifierItem through libayatana-appindicator (or libappindicator) where it is
/// installed, which modern desktops show (GNOME with its AppIndicator extension); otherwise GTK's
/// own status icon, which only desktops with an XEmbed tray show. Indicators show their menu on any
/// click; GTK's icon shows the window on a click and its menu on a right-click.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class GtkTray : TrayIcon
{
    private const string Gtk = GtkWindows.Gtk;
    private const string GObject = "libgobject-2.0.so.0";
    private const int IndicatorCategoryApplicationStatus = 0;
    private const int IndicatorStatusPassive = 0, IndicatorStatusActive = 1;

    private static readonly string[] IndicatorLibraries =
        ["libayatana-appindicator3.so.1", "libappindicator3.so.1"];

    private readonly AppIndicator? _indicator = AppIndicator.Load();
    private IntPtr _indicatorHandle;
    private IntPtr _statusIcon;
    private IntPtr _menu;
    private TrayMenu? _actions;

    // Kept in fields so the callbacks GTK calls are never collected.
    private readonly ActivateCallback _onOpen;
    private readonly ActivateCallback _onQuit;
    private readonly PopupCallback _onPopup;

    public GtkTray()
    {
        _onOpen = (_, _) => _actions?.Open();
        _onQuit = (_, _) => _actions?.Quit();
        _onPopup = (_, _, _, _) =>
        {
            if (_menu != IntPtr.Zero)
            {
                gtk_menu_popup_at_pointer(_menu, IntPtr.Zero);
            }
        };
    }

    public override bool IsShown => _actions is not null;

    // An indicator falls back to an XEmbed icon itself when no StatusNotifier host is running, so
    // it can't tell; GTK's own icon knows whether a tray took it.
    public override bool IsVisible =>
        IsShown && (_indicatorHandle != IntPtr.Zero || (_statusIcon != IntPtr.Zero && gtk_status_icon_is_embedded(_statusIcon)));

    public override bool Show(PhotinoWindow window, TrayMenu menu)
    {
        _actions = menu;
        _menu = BuildMenu();
        var iconPath = IconPath("png");
        if (_indicator is { } indicator)
        {
            _indicatorHandle = indicator.New("dynpost", Path.GetFileNameWithoutExtension(iconPath), IndicatorCategoryApplicationStatus);
            if (_indicatorHandle != IntPtr.Zero)
            {
                indicator.SetIconThemePath(_indicatorHandle, Path.GetDirectoryName(iconPath)!);
                indicator.SetTitle?.Invoke(_indicatorHandle, TrayMenu.Tooltip);
                indicator.SetMenu(_indicatorHandle, _menu);
                indicator.SetStatus(_indicatorHandle, IndicatorStatusActive);
                return true;
            }
        }

        _statusIcon = File.Exists(iconPath) ? gtk_status_icon_new_from_file(iconPath) : gtk_status_icon_new_from_icon_name("application-x-executable");
        if (_statusIcon == IntPtr.Zero)
        {
            Remove();
            return false;
        }

        gtk_status_icon_set_tooltip_text(_statusIcon, TrayMenu.Tooltip);
        gtk_status_icon_set_title(_statusIcon, TrayMenu.Tooltip);
        Connect(_statusIcon, "activate", Marshal.GetFunctionPointerForDelegate(_onOpen));
        Connect(_statusIcon, "popup-menu", Marshal.GetFunctionPointerForDelegate(_onPopup));
        gtk_status_icon_set_visible(_statusIcon, true);
        return true;
    }

    public override void Remove()
    {
        if (_indicatorHandle != IntPtr.Zero)
        {
            _indicator!.SetStatus(_indicatorHandle, IndicatorStatusPassive);
            g_object_unref(_indicatorHandle);
            _indicatorHandle = IntPtr.Zero;
        }

        if (_statusIcon != IntPtr.Zero)
        {
            gtk_status_icon_set_visible(_statusIcon, false);
            g_object_unref(_statusIcon);
            _statusIcon = IntPtr.Zero;
        }

        if (_menu != IntPtr.Zero)
        {
            gtk_widget_destroy(_menu);
            _menu = IntPtr.Zero;
        }

        _actions = null;
    }

    public override void HideWindow(PhotinoWindow window)
    {
        if (GtkWindows.FindMain() is var gtkWindow && gtkWindow != IntPtr.Zero)
        {
            gtk_widget_hide(gtkWindow);
        }
    }

    public override void ShowWindow(PhotinoWindow window)
    {
        if (GtkWindows.FindMain() is var gtkWindow && gtkWindow != IntPtr.Zero)
        {
            gtk_widget_show(gtkWindow);
            gtk_window_deiconify(gtkWindow);
            gtk_window_present(gtkWindow);
        }
    }

    private IntPtr BuildMenu()
    {
        var menu = gtk_menu_new();
        var open = gtk_menu_item_new_with_label(TrayMenu.OpenLabel);
        Connect(open, "activate", Marshal.GetFunctionPointerForDelegate(_onOpen));
        gtk_menu_shell_append(menu, open);
        gtk_menu_shell_append(menu, gtk_separator_menu_item_new());
        var quit = gtk_menu_item_new_with_label(TrayMenu.QuitLabel);
        Connect(quit, "activate", Marshal.GetFunctionPointerForDelegate(_onQuit));
        gtk_menu_shell_append(menu, quit);
        gtk_widget_show_all(menu);
        return menu;
    }

    private static void Connect(IntPtr instance, string signal, IntPtr callback) =>
        g_signal_connect_data(instance, signal, callback, IntPtr.Zero, IntPtr.Zero, 0);

    /// <summary>The functions of an indicator library, which may not be installed.</summary>
    private sealed class AppIndicator
    {
        public required IndicatorNew New { get; init; }
        public required IndicatorSetInt SetStatus { get; init; }
        public required IndicatorSetPointer SetMenu { get; init; }
        public required IndicatorSetString SetIconThemePath { get; init; }
        public IndicatorSetString? SetTitle { get; init; }

        public static AppIndicator? Load()
        {
            foreach (var name in IndicatorLibraries)
            {
                try
                {
                    if (!NativeLibrary.TryLoad(name, out var library))
                    {
                        continue;
                    }

                    T Get<T>(string export) where T : Delegate =>
                        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, export));
                    return new AppIndicator
                    {
                        New = Get<IndicatorNew>("app_indicator_new"),
                        SetStatus = Get<IndicatorSetInt>("app_indicator_set_status"),
                        SetMenu = Get<IndicatorSetPointer>("app_indicator_set_menu"),
                        SetIconThemePath = Get<IndicatorSetString>("app_indicator_set_icon_theme_path"),
                        SetTitle = NativeLibrary.TryGetExport(library, "app_indicator_set_title", out var title)
                            ? Marshal.GetDelegateForFunctionPointer<IndicatorSetString>(title)
                            : null,
                    };
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Could not load {name}: {ex.Message}");
                }
            }

            return null;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ActivateCallback(IntPtr instance, IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PopupCallback(IntPtr instance, uint button, uint activateTime, IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr IndicatorNew([MarshalAs(UnmanagedType.LPUTF8Str)] string id, [MarshalAs(UnmanagedType.LPUTF8Str)] string iconName, int category);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void IndicatorSetInt(IntPtr indicator, int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void IndicatorSetPointer(IntPtr indicator, IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void IndicatorSetString(IntPtr indicator, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(Gtk)]
    private static extern IntPtr gtk_menu_new();

    [DllImport(Gtk)]
    private static extern IntPtr gtk_menu_item_new_with_label([MarshalAs(UnmanagedType.LPUTF8Str)] string label);

    [DllImport(Gtk)]
    private static extern IntPtr gtk_separator_menu_item_new();

    [DllImport(Gtk)]
    private static extern void gtk_menu_shell_append(IntPtr menu, IntPtr item);

    [DllImport(Gtk)]
    private static extern void gtk_menu_popup_at_pointer(IntPtr menu, IntPtr triggerEvent);

    [DllImport(Gtk)]
    private static extern void gtk_widget_show_all(IntPtr widget);

    [DllImport(Gtk)]
    private static extern void gtk_widget_show(IntPtr widget);

    [DllImport(Gtk)]
    private static extern void gtk_widget_hide(IntPtr widget);

    [DllImport(Gtk)]
    private static extern void gtk_widget_destroy(IntPtr widget);

    [DllImport(Gtk)]
    private static extern void gtk_window_deiconify(IntPtr window);

    [DllImport(Gtk)]
    private static extern void gtk_window_present(IntPtr window);

    [DllImport(Gtk)]
    private static extern IntPtr gtk_status_icon_new_from_file([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(Gtk)]
    private static extern IntPtr gtk_status_icon_new_from_icon_name([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Gtk)]
    private static extern void gtk_status_icon_set_tooltip_text(IntPtr icon, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

    [DllImport(Gtk)]
    private static extern void gtk_status_icon_set_title(IntPtr icon, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

    [DllImport(Gtk)]
    private static extern void gtk_status_icon_set_visible(IntPtr icon, [MarshalAs(UnmanagedType.Bool)] bool visible);

    [DllImport(Gtk)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool gtk_status_icon_is_embedded(IntPtr icon);

    [DllImport(GObject)]
    private static extern ulong g_signal_connect_data(IntPtr instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal,
        IntPtr handler, IntPtr data, IntPtr destroyData, int flags);

    [DllImport(GObject)]
    private static extern void g_object_unref(IntPtr instance);
}
