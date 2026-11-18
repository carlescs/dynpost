using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Dynpost.Desktop.Bridge;
using Photino.NET;

namespace Dynpost.Desktop.Window;

/// <summary>
/// Windows: the window keeps its normal frame (so the shadow, rounded corners, snapping and the
/// side and bottom resize borders stay native) but has no caption: <c>WM_NCCALCSIZE</c> gives the
/// caption's space to the web view. Moving and resizing from the top edge are handed to Windows as
/// if the caption or border had been pressed.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class Win32Chrome : WindowChrome
{
    private const int GwlpWndProc = -4;
    private const uint WmNcCalcSize = 0x0083;
    private const uint WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private const int SmCyFrame = 33;
    private const int SmCxPaddedBorder = 92;
    private const int SmSwapButton = 23;
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    private IntPtr _hwnd;
    private IntPtr _previousProc;
    // Kept in a field so the delegate Windows calls is never collected.
    private WndProc? _proc;

    public override string Platform => "windows";

    public override void Created(PhotinoWindow window)
    {
        if (_proc is not null)
        {
            return;
        }

        _hwnd = window.WindowHandle;
        _proc = WindowProc;
        _previousProc = SetWindowLongPtr(_hwnd, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(_proc));

        // Recalculate the frame now that the caption is gone (the same size, so the web view refits).
        if (GetWindowRect(_hwnd, out var rect))
        {
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top,
                SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }
    }

    public override void StartDrag(PhotinoWindow window) => SendNonClientPress(HtCaption);

    public override void StartResize(PhotinoWindow window, WindowEdge edge) => SendNonClientPress(edge switch
    {
        WindowEdge.Left => 10,
        WindowEdge.Right => 11,
        WindowEdge.Top => 12,
        WindowEdge.TopLeft => 13,
        WindowEdge.TopRight => 14,
        WindowEdge.Bottom => 15,
        WindowEdge.BottomLeft => 16,
        WindowEdge.BottomRight => 17,
        _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, null),
    });

    // The web view holds the mouse capture; releasing it and posting a press on the non-client area
    // starts Windows' own move or size loop, which follows the mouse until its button is released.
    // A quick click is often released before the UI's message arrives; a move started then would
    // wait for a release that already happened.
    private void SendNonClientPress(int hitTest)
    {
        var primary = GetSystemMetrics(SmSwapButton) != 0 ? VkRButton : VkLButton;
        if (_hwnd == IntPtr.Zero || (GetAsyncKeyState(primary) & 0x8000) == 0 || !GetCursorPos(out var cursor))
        {
            return;
        }

        ReleaseCapture();
        var position = (IntPtr)((cursor.Y & 0xFFFF) << 16 | (cursor.X & 0xFFFF));
        PostMessage(_hwnd, WmNcLButtonDown, hitTest, position);
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message != WmNcCalcSize || wParam == IntPtr.Zero)
        {
            return CallWindowProc(_previousProc, hwnd, message, wParam, lParam);
        }

        // lParam is NCCALCSIZE_PARAMS, whose first RECT becomes the client area: let Windows
        // compute the frame, then move the client's top back up over the caption. A maximized
        // window reaches past the screen by its frame, which the top must still leave out.
        const int topOffset = 4;
        var top = Marshal.ReadInt32(lParam, topOffset);
        var result = CallWindowProc(_previousProc, hwnd, message, wParam, lParam);
        Marshal.WriteInt32(lParam, topOffset, IsZoomed(hwnd) ? top + FrameThickness(hwnd) : top);
        return result;
    }

    private static int FrameThickness(IntPtr hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        return GetSystemMetricsForDpi(SmCyFrame, dpi) + GetSystemMetricsForDpi(SmCxPaddedBorder, dpi);
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, uint message, nint wParam, IntPtr lParam);
}
