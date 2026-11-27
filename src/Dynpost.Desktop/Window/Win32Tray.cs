using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Photino.NET;

namespace Dynpost.Desktop.Window;

/// <summary>
/// Windows: an icon in the notification area (<c>Shell_NotifyIcon</c>), whose clicks go to a hidden
/// window of its own. Clicking it shows the app; right-clicking opens its menu. Hiding the window
/// also removes its taskbar button.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class Win32Tray : TrayIcon
{
    private const string ClassName = "DynpostTrayWindow";
    private const uint IconId = 1;
    private const uint CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const uint NimAdd = 0, NimDelete = 2;
    private const uint NifMessage = 0x1, NifIcon = 0x2, NifTip = 0x4;
    private const uint WmNull = 0x0000, WmLButtonUp = 0x0202, WmRButtonUp = 0x0205, WmContextMenu = 0x007B;
    private const uint MfString = 0x0, MfSeparator = 0x800;
    private const uint TpmRightButton = 0x2, TpmReturnCmd = 0x100, TpmNoNotify = 0x80;
    private const int SwHide = 0, SwShow = 5, SwRestore = 9;
    private const uint ImageIcon = 1, LrLoadFromFile = 0x10;
    private const int SmCxSmIcon = 49, SmCySmIcon = 50;
    private const int OpenCommand = 1, QuitCommand = 2;

    private IntPtr _messageWindow;
    private IntPtr _icon;
    private uint _taskbarCreated;
    private TrayMenu? _menu;
    // Kept in a field so the delegate Windows calls is never collected.
    private WndProc? _proc;

    public override bool IsShown => _menu is not null;

    public override bool Show(PhotinoWindow window, TrayMenu menu)
    {
        if (_messageWindow == IntPtr.Zero && !CreateMessageWindow())
        {
            return false;
        }

        if (_icon == IntPtr.Zero)
        {
            _icon = LoadImage(IntPtr.Zero, IconPath("ico"), ImageIcon,
                GetSystemMetrics(SmCxSmIcon), GetSystemMetrics(SmCySmIcon), LrLoadFromFile);
        }

        _menu = menu;
        if (!AddIcon())
        {
            _menu = null;
            return false;
        }

        return true;
    }

    public override void Remove()
    {
        if (_menu is null)
        {
            return;
        }

        var data = IconData();
        Shell_NotifyIcon(NimDelete, ref data);
        _menu = null;
    }

    public override void HideWindow(PhotinoWindow window) => ShowWindow(window.WindowHandle, SwHide);

    public override void ShowWindow(PhotinoWindow window)
    {
        var hwnd = window.WindowHandle;
        ShowWindow(hwnd, IsIconic(hwnd) ? SwRestore : SwShow);
        SetForegroundWindow(hwnd);
    }

    private bool CreateMessageWindow()
    {
        _proc = WindowProc;
        var instance = GetModuleHandle(null);
        var windowClass = new WndClassEx
        {
            Size = (uint)Marshal.SizeOf<WndClassEx>(),
            WindowProc = Marshal.GetFunctionPointerForDelegate(_proc),
            Instance = instance,
            ClassName = ClassName,
        };
        // Registering twice fails harmlessly; the window is what matters.
        RegisterClassEx(ref windowClass);
        // A plain hidden window rather than a message-only one: only top-level windows hear that
        // Explorer restarted (TaskbarCreated), when the icon must be added again.
        _messageWindow = CreateWindowEx(0, ClassName, "Dynpost tray", 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        return _messageWindow != IntPtr.Zero;
    }

    private bool AddIcon()
    {
        var data = IconData();
        data.Flags = NifMessage | NifIcon | NifTip;
        data.CallbackMessage = CallbackMessage;
        data.Icon = _icon;
        data.Tip = TrayMenu.Tooltip;
        return Shell_NotifyIcon(NimAdd, ref data);
    }

    private NotifyIconData IconData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = _messageWindow,
        Id = IconId,
        Tip = string.Empty,
        Info = string.Empty,
        InfoTitle = string.Empty,
    };

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == CallbackMessage && _menu is { } menu)
        {
            switch ((uint)(lParam.ToInt64() & 0xFFFF))
            {
                case WmLButtonUp:
                    menu.Open();
                    break;
                case WmRButtonUp:
                case WmContextMenu:
                    ShowMenu(hwnd, menu);
                    break;
            }

            return IntPtr.Zero;
        }

        if (message == _taskbarCreated && _taskbarCreated != 0 && _menu is not null)
        {
            AddIcon();
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static void ShowMenu(IntPtr hwnd, TrayMenu menu)
    {
        var popup = CreatePopupMenu();
        try
        {
            AppendMenu(popup, MfString, OpenCommand, TrayMenu.OpenLabel);
            AppendMenu(popup, MfSeparator, 0, null);
            AppendMenu(popup, MfString, QuitCommand, TrayMenu.QuitLabel);
            GetCursorPos(out var cursor);
            // Without this the menu stays open when the user clicks elsewhere.
            SetForegroundWindow(hwnd);
            var command = TrackPopupMenu(popup, TpmRightButton | TpmReturnCmd | TpmNoNotify,
                cursor.X, cursor.Y, 0, hwnd, IntPtr.Zero);
            PostMessage(hwnd, WmNull, IntPtr.Zero, IntPtr.Zero);
            if (command == OpenCommand)
            {
                menu.Open();
            }
            else if (command == QuitCommand)
            {
                menu.Quit();
            }
        }
        finally
        {
            DestroyMenu(popup);
        }
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public uint InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW")]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadImageW")]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, nint id, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);
}
