using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Dynpost.Desktop.Bridge;
using Photino.NET;

namespace Dynpost.Desktop.Window;

/// <summary>
/// Linux: the GTK window is undecorated, so it has no frame at all; moving and resizing (from any
/// edge) are handed to the window manager with GTK's move and resize drags.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class GtkChrome : WindowChrome
{
    private const string Gtk = "libgtk-3.so.0";
    private const string Gdk = "libgdk-3.so.0";
    private const int PrimaryButton = 1;
    private const uint Button1Mask = 1 << 8;

    private IntPtr _window;

    public override string Platform => "linux";

    public override void Configure(PhotinoWindow window) => window.SetChromeless(true);

    public override void StartDrag(PhotinoWindow window)
    {
        if (FindWindow() is var gtkWindow && gtkWindow != IntPtr.Zero && PointerPosition() is var (x, y))
        {
            gtk_window_begin_move_drag(gtkWindow, PrimaryButton, x, y, gtk_get_current_event_time());
        }
    }

    public override void StartResize(PhotinoWindow window, WindowEdge edge)
    {
        // GdkWindowEdge.
        var gdkEdge = edge switch
        {
            WindowEdge.TopLeft => 0,
            WindowEdge.Top => 1,
            WindowEdge.TopRight => 2,
            WindowEdge.Left => 3,
            WindowEdge.Right => 4,
            WindowEdge.BottomLeft => 5,
            WindowEdge.Bottom => 6,
            WindowEdge.BottomRight => 7,
            _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, null),
        };
        if (FindWindow() is var gtkWindow && gtkWindow != IntPtr.Zero && PointerPosition() is var (x, y))
        {
            gtk_window_begin_resize_drag(gtkWindow, gdkEdge, PrimaryButton, x, y, gtk_get_current_event_time());
        }
    }

    private IntPtr FindWindow() => _window != IntPtr.Zero ? _window : _window = GtkWindows.FindMain();

    /// <summary>
    /// Where the pointer is, while its primary button is still down; <see langword="null"/> once it
    /// is released (a quick click is often over before the UI's message arrives), as a drag started
    /// then would wait for a release that already happened and take the next click.
    /// </summary>
    private static (int X, int Y)? PointerPosition()
    {
        var display = gdk_display_get_default();
        var seat = display == IntPtr.Zero ? IntPtr.Zero : gdk_display_get_default_seat(display);
        var pointer = seat == IntPtr.Zero ? IntPtr.Zero : gdk_seat_get_pointer(seat);
        var root = gdk_get_default_root_window();
        if (pointer == IntPtr.Zero || root == IntPtr.Zero)
        {
            return null;
        }

        gdk_window_get_device_position(root, pointer, out var x, out var y, out var mask);
        return (mask & Button1Mask) != 0 ? (x, y) : null;
    }

    [DllImport(Gtk)]
    private static extern uint gtk_get_current_event_time();

    [DllImport(Gtk)]
    private static extern void gtk_window_begin_move_drag(IntPtr window, int button, int rootX, int rootY, uint timestamp);

    [DllImport(Gtk)]
    private static extern void gtk_window_begin_resize_drag(IntPtr window, int edge, int button, int rootX, int rootY, uint timestamp);

    [DllImport(Gdk)]
    private static extern IntPtr gdk_display_get_default();

    [DllImport(Gdk)]
    private static extern IntPtr gdk_display_get_default_seat(IntPtr display);

    [DllImport(Gdk)]
    private static extern IntPtr gdk_seat_get_pointer(IntPtr seat);

    [DllImport(Gdk)]
    private static extern IntPtr gdk_get_default_root_window();

    [DllImport(Gdk)]
    private static extern IntPtr gdk_window_get_device_position(IntPtr window, IntPtr device, out int x, out int y, out uint mask);
}
