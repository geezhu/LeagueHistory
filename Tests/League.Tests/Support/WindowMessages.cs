using System.Runtime.InteropServices;

namespace League.Tests.Support;

/// <summary>
/// Drives a control through its window messages, the way Windows does. A message is the public interface of a Win32
/// control, so a test can click a painted button or press a key without reflecting into the protected
/// <c>OnMouseDown</c> / <c>OnKeyDown</c> overrides. Call it from inside <see cref="UiHost.Run"/>, on the window's own
/// thread.
/// </summary>
public static class WindowMessages
{
    private const int MouseMove = 0x0200;
    private const int MouseLeave = 0x02A3;
    private const int LeftButtonDown = 0x0201;
    private const int LeftButtonUp = 0x0202;
    private const int LeftButtonDoubleClick = 0x0203;
    private const int KeyDown = 0x0100;
    private const int KeyUp = 0x0101;
    private const int LeftButton = 0x0001;

    private const int MouseWheel = 0x020A;
    private const int WheelDelta = 120;

    public static void Move(Control control, Point point) => Send(control, MouseMove, 0, Pack(point));

    public static void Click(Control control, Point point)
    {
        Send(control, LeftButtonDown, LeftButton, Pack(point));
        Send(control, LeftButtonUp, 0, Pack(point));
    }

    /// <summary>The message Windows posts when the pointer leaves a window that asked to be told about it.</summary>
    public static void Leave(Control control) => Send(control, MouseLeave, 0, 0);

    /// <summary>A double click: the first press and release, then the message Windows sends for the second press.</summary>
    public static void DoubleClick(Control control, Point point)
    {
        Click(control, point);
        Send(control, LeftButtonDoubleClick, LeftButton, Pack(point));
        Send(control, LeftButtonUp, 0, Pack(point));
    }

    /// <summary>A press and a move far enough to start a drag, in one go.</summary>
    public static void Drag(Control control, Point from, Point to)
    {
        Send(control, LeftButtonDown, LeftButton, Pack(from));
        Send(control, MouseMove, LeftButton, Pack(to));
    }

    public static void Drop(Control control, Point point)
    {
        Send(control, MouseMove, LeftButton, Pack(point));
        Send(control, LeftButtonUp, 0, Pack(point));
    }

    /// <summary>Turns the wheel; a negative <paramref name="notches"/> scrolls down.</summary>
    public static void Wheel(Control control, int notches) => Send(control, MouseWheel, notches * WheelDelta << 16, 0);

    public static void Key(Control control, Keys key)
    {
        Send(control, KeyDown, (int)key, 0);
        Send(control, KeyUp, (int)key, 0);
    }

    /// <summary>
    /// The press of a key on its own, for a handler that closes its window: there is no window left to release the key
    /// to afterwards.
    /// </summary>
    public static void KeyDownOnly(Control control, Keys key) => Send(control, KeyDown, (int)key, 0);

    /// <summary>A key press as a message, for the preprocessing the message loop does before dispatching one.</summary>
    public static Message KeyDownMessage(Control control, Keys key) =>
        Message.Create(control.Handle, KeyDown, (nint)key, 0);

    // The low word is the x coordinate and the high word the y coordinate, both relative to the client area.
    private static int Pack(Point point) => (point.Y << 16) | (point.X & 0xFFFF);

    private static void Send(Control control, int message, int wParam, int lParam) =>
        SendMessage(control.Handle, message, wParam, lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
