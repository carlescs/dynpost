using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Dynpost.Desktop.Bridge;
using Photino.NET;

namespace Dynpost.Desktop.Window;

/// <summary>
/// macOS: the window keeps its traffic-light buttons and native resizing, but its title bar turns
/// transparent and the web view fills the window beneath it (a full-size content view), so the
/// app's header sits where the title was. Moving is handed to AppKit with a window drag.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class CocoaChrome : WindowChrome
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const ulong FullSizeContentView = 1UL << 15;
    private const long TitleHidden = 1;
    private const ulong LeftMouseDown = 1;

    private IntPtr _window;
    private bool _styled;

    public override string Platform => "macos";

    public override void Created(PhotinoWindow window)
    {
        if (!_styled && FindWindow() is var nsWindow && nsWindow != IntPtr.Zero)
        {
            _styled = true;
            var mask = SendULong(nsWindow, Sel("styleMask"));
            Send(nsWindow, Sel("setStyleMask:"), mask | FullSizeContentView);
            SendBool(nsWindow, Sel("setTitlebarAppearsTransparent:"), 1);
            Send(nsWindow, Sel("setTitleVisibility:"), TitleHidden);
        }
    }

    // A drag must start from a mouse-down event; by the time the web view's message arrives the
    // current event is another one, so make a mouse-down where the pointer is (as other
    // web-view shells do).
    public override void StartDrag(PhotinoWindow window)
    {
        // A quick click is often released before the UI's message arrives; a drag started then would
        // wait for a release that already happened.
        var nsWindow = FindWindow();
        if (nsWindow == IntPtr.Zero || (SendULong(Class("NSEvent"), Sel("pressedMouseButtons")) & 1) == 0)
        {
            return;
        }

        var location = SendPoint(nsWindow, Sel("mouseLocationOutsideOfEventStream"));
        var windowNumber = SendLong(nsWindow, Sel("windowNumber"));
        var uptime = SendDouble(Send(Class("NSProcessInfo"), Sel("processInfo")), Sel("systemUptime"));
        var mouseDown = SendMouseEvent(
            Class("NSEvent"),
            Sel("mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:"),
            LeftMouseDown, location, 0, uptime, windowNumber, IntPtr.Zero, 0, 1, 1f);
        if (mouseDown != IntPtr.Zero)
        {
            Send(nsWindow, Sel("performWindowDragWithEvent:"), mouseDown);
        }
    }

    // The window keeps its native edges.
    public override void StartResize(PhotinoWindow window, WindowEdge edge)
    {
    }

    // Double-clicking a macOS title bar zooms the window.
    public override void ToggleMaximize(PhotinoWindow window)
    {
        if (FindWindow() is var nsWindow && nsWindow != IntPtr.Zero)
        {
            Send(nsWindow, Sel("zoom:"), IntPtr.Zero);
        }
    }

    // Photino doesn't expose its NSWindow; the app has a single window.
    private IntPtr FindWindow()
    {
        if (_window != IntPtr.Zero)
        {
            return _window;
        }

        var app = Send(Class("NSApplication"), Sel("sharedApplication"));
        var windows = Send(app, Sel("windows"));
        return windows != IntPtr.Zero && SendULong(windows, Sel("count")) > 0
            ? _window = SendIndex(windows, Sel("objectAtIndex:"), 0)
            : IntPtr.Zero;
    }

    private static IntPtr Class(string name) => objc_getClass(name);

    private static IntPtr Sel(string name) => sel_registerName(name);

    [StructLayout(LayoutKind.Sequential)]
    private struct NSPoint
    {
        public double X, Y;
    }

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void Send(IntPtr receiver, IntPtr selector, ulong argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void Send(IntPtr receiver, IntPtr selector, long argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendBool(IntPtr receiver, IntPtr selector, byte argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIndex(IntPtr receiver, IntPtr selector, ulong index);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern ulong SendULong(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern long SendLong(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern double SendDouble(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern NSPoint SendPoint(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMouseEvent(
        IntPtr receiver,
        IntPtr selector,
        ulong type,
        NSPoint location,
        ulong modifierFlags,
        double timestamp,
        long windowNumber,
        IntPtr context,
        long eventNumber,
        long clickCount,
        float pressure);
}
