using System.Runtime.InteropServices;
using League.Controls;

namespace League;

public partial class FormMain
{
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    private void ConfigureWindowChrome()
    {
        // The rail already separates the navigation from the pages; the outer border only doubled the window frame.
        panel1.BorderStyle = BorderStyle.None;
        panel1.BackColor = Theme.Canvas;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // The title bar continues the navigation rail. Windows 10 rejects these attributes and keeps the
        // system title bar, which still works, so the result is not checked.
        SetWindowColor(DwmCaptionColor, Theme.Rail);
        SetWindowColor(DwmBorderColor, Theme.Rail);
        SetWindowColor(DwmTextColor, Theme.RailText);
    }

    private void SetWindowColor(int attribute, Color color)
    {
        int colorRef = color.R | (color.G << 8) | (color.B << 16);
        _ = DwmSetWindowAttribute(Handle, attribute, ref colorRef, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
