using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Photino.NET;

namespace Dynpost.Desktop.Window;

/// <summary>
/// macOS: an item in the menu bar's status area, whose menu shows the window or quits. A hidden
/// window also takes the app's Dock icon away (an accessory app) until it shows again.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class CocoaTray : TrayIcon
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const double VariableLength = -1;
    private const long ActivationPolicyRegular = 0, ActivationPolicyAccessory = 1;
    private const double IconSize = 18;

    // The menu items' target: a class made at run time, whose methods call the open tray's menu.
    private static readonly MenuAction OpenAction = (_, _, _) => s_actions?.Open();
    private static readonly MenuAction QuitAction = (_, _, _) => s_actions?.Quit();
    private static IntPtr s_targetClass;
    private static TrayMenu? s_actions;

    private IntPtr _item;
    private IntPtr _target;

    public override bool IsShown => _item != IntPtr.Zero;

    public override bool Show(PhotinoWindow window, TrayMenu menu)
    {
        var targetClass = TargetClass();
        if (targetClass == IntPtr.Zero)
        {
            return false;
        }

        s_actions = menu;
        var statusBar = Send(Class("NSStatusBar"), Sel("systemStatusBar"));
        _item = Send(SendDouble(statusBar, Sel("statusItemWithLength:"), VariableLength), Sel("retain"));
        if (_item == IntPtr.Zero)
        {
            return false;
        }

        var button = Send(_item, Sel("button"));
        var path = IconPath("png");
        var image = File.Exists(path)
            ? Send(Send(Class("NSImage"), Sel("alloc")), Sel("initWithContentsOfFile:"), NSString(path))
            : IntPtr.Zero;
        if (image != IntPtr.Zero)
        {
            SendSize(image, Sel("setSize:"), new NSSize { Width = IconSize, Height = IconSize });
            Send(button, Sel("setImage:"), image);
            Send(image, Sel("release"));
        }
        else
        {
            Send(button, Sel("setTitle:"), NSString(TrayMenu.Tooltip));
        }

        Send(button, Sel("setToolTip:"), NSString(TrayMenu.Tooltip));

        _target = Send(Send(targetClass, Sel("alloc")), Sel("init"));
        var nsMenu = Send(Send(Class("NSMenu"), Sel("alloc")), Sel("init"));
        AddItem(nsMenu, TrayMenu.OpenLabel, "dynpostOpen:");
        Send(nsMenu, Sel("addItem:"), Send(Class("NSMenuItem"), Sel("separatorItem")));
        AddItem(nsMenu, TrayMenu.QuitLabel, "dynpostQuit:");
        Send(_item, Sel("setMenu:"), nsMenu);
        Send(nsMenu, Sel("release"));
        return true;
    }

    public override void Remove()
    {
        if (_item == IntPtr.Zero)
        {
            return;
        }

        Send(Send(Class("NSStatusBar"), Sel("systemStatusBar")), Sel("removeStatusItem:"), _item);
        Send(_item, Sel("release"));
        Send(_target, Sel("release"));
        _item = IntPtr.Zero;
        _target = IntPtr.Zero;
        s_actions = null;
    }

    public override void HideWindow(PhotinoWindow window)
    {
        if (FindWindow() is var nsWindow && nsWindow != IntPtr.Zero)
        {
            Send(nsWindow, Sel("orderOut:"), IntPtr.Zero);
            Send(App(), Sel("setActivationPolicy:"), ActivationPolicyAccessory);
        }
    }

    public override void ShowWindow(PhotinoWindow window)
    {
        if (FindWindow() is var nsWindow && nsWindow != IntPtr.Zero)
        {
            var app = App();
            Send(app, Sel("setActivationPolicy:"), ActivationPolicyRegular);
            if (SendByte(nsWindow, Sel("isMiniaturized")) != 0)
            {
                Send(nsWindow, Sel("deminiaturize:"), IntPtr.Zero);
            }

            Send(nsWindow, Sel("makeKeyAndOrderFront:"), IntPtr.Zero);
            SendBool(app, Sel("activateIgnoringOtherApps:"), 1);
        }
    }

    private void AddItem(IntPtr menu, string title, string action)
    {
        var item = Send(Send(Class("NSMenuItem"), Sel("alloc")), Sel("initWithTitle:action:keyEquivalent:"),
            NSString(title), Sel(action), NSString(string.Empty));
        Send(item, Sel("setTarget:"), _target);
        Send(menu, Sel("addItem:"), item);
        Send(item, Sel("release"));
    }

    private static IntPtr TargetClass()
    {
        if (s_targetClass != IntPtr.Zero)
        {
            return s_targetClass;
        }

        var existing = objc_getClass("DynpostTrayTarget");
        if (existing != IntPtr.Zero)
        {
            return s_targetClass = existing;
        }

        var targetClass = objc_allocateClassPair(Class("NSObject"), "DynpostTrayTarget", 0);
        if (targetClass == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        class_addMethod(targetClass, Sel("dynpostOpen:"), Marshal.GetFunctionPointerForDelegate(OpenAction), "v@:@");
        class_addMethod(targetClass, Sel("dynpostQuit:"), Marshal.GetFunctionPointerForDelegate(QuitAction), "v@:@");
        objc_registerClassPair(targetClass);
        return s_targetClass = targetClass;
    }

    // Photino doesn't expose its NSWindow; the app has a single window.
    private static IntPtr FindWindow()
    {
        var windows = Send(App(), Sel("windows"));
        return windows != IntPtr.Zero && SendULong(windows, Sel("count")) > 0
            ? SendIndex(windows, Sel("objectAtIndex:"), 0)
            : IntPtr.Zero;
    }

    private static IntPtr App() => Send(Class("NSApplication"), Sel("sharedApplication"));

    // Autoreleased, like every string AppKit hands out.
    private static IntPtr NSString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return Send(Class("NSString"), Sel("stringWithUTF8String:"), utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    private static IntPtr Class(string name) => objc_getClass(name);

    private static IntPtr Sel(string name) => sel_registerName(name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MenuAction(IntPtr self, IntPtr selector, IntPtr sender);

    [StructLayout(LayoutKind.Sequential)]
    private struct NSSize
    {
        public double Width, Height;
    }

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC)]
    private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nuint extraBytes);

    [DllImport(ObjC)]
    private static extern void objc_registerClassPair(IntPtr cls);

    [DllImport(ObjC)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr implementation, string types);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second, IntPtr third);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void Send(IntPtr receiver, IntPtr selector, long argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendDouble(IntPtr receiver, IntPtr selector, double argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendSize(IntPtr receiver, IntPtr selector, NSSize size);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendBool(IntPtr receiver, IntPtr selector, byte argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern byte SendByte(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern ulong SendULong(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIndex(IntPtr receiver, IntPtr selector, ulong index);
}
