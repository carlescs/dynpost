using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Dynpost.Desktop.Window;

/// <summary>Finds Photino's GtkWindow, which Photino doesn't expose.</summary>
[SupportedOSPlatform("linux")]
internal static class GtkWindows
{
    public const string Gtk = "libgtk-3.so.0";
    public const string GLib = "libglib-2.0.so.0";
    private const int ToplevelWindow = 0;

    /// <summary>
    /// The app's main window: its only top-level window that isn't a dialog (dialogs are transient
    /// for it), or <see cref="IntPtr.Zero"/> before it exists. Call it on the UI thread.
    /// </summary>
    public static IntPtr FindMain()
    {
        var list = gtk_window_list_toplevels();
        try
        {
            for (var node = list; node != IntPtr.Zero; node = Marshal.ReadIntPtr(node, IntPtr.Size))
            {
                var candidate = Marshal.ReadIntPtr(node);
                if (gtk_window_get_window_type(candidate) == ToplevelWindow
                    && gtk_window_get_transient_for(candidate) == IntPtr.Zero)
                {
                    return candidate;
                }
            }
        }
        finally
        {
            g_list_free(list);
        }

        return IntPtr.Zero;
    }

    [DllImport(Gtk)]
    private static extern IntPtr gtk_window_list_toplevels();

    [DllImport(Gtk)]
    private static extern int gtk_window_get_window_type(IntPtr window);

    [DllImport(Gtk)]
    private static extern IntPtr gtk_window_get_transient_for(IntPtr window);

    [DllImport(GLib)]
    private static extern void g_list_free(IntPtr list);
}
