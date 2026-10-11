using System.Runtime.InteropServices;
using League.Controls;
using League.Models;

namespace League;

internal sealed class LoadingOverlayForm : Form
{
    private const int OverlayWidth = 1020;
    private const int OverlayHeight = 670;
    private const int NoActivateWindowStyle = 0x08000000;
    private const int ToolWindowStyle = 0x00000080;
    private const int MouseActivateMessage = 0x0021;
    private const int MouseNoActivate = 3;
    public LoadingOverlayView View { get; }
    public event Action? Dismissed;
    private readonly Label _title = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(14, 0, 0, 0),
    };

    public LoadingOverlayForm(Func<int, string> champion, Func<LoadingPlayer, string> details)
    {
        Text = "对局速览";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(8, 29, 40);
        ForeColor = Color.WhiteSmoke;
        ClientSize = new Size(OverlayWidth, OverlayHeight);
        View = new LoadingOverlayView(champion, details);
        Controls.Add(View);
        Controls.Add(CreateHeader());
        SetRosterTitle();
    }

    private Panel CreateHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 42,
            BackColor = Color.FromArgb(19, 59, 71),
        };
        var close = new Button
        {
            Dock = DockStyle.Right,
            Width = 72,
            Text = "收起",
            FlatStyle = FlatStyle.Flat,
            ForeColor = ForeColor,
        };
        close.FlatAppearance.BorderSize = 0;
        close.Click += (_, _) => Dismissed?.Invoke();
        header.Controls.Add(_title);
        header.Controls.Add(close);
        return header;
    }

    public void SetRosterTitle() => _title.Text = "对局速览 · 双方英雄";

    public void SetPreviewTitle() => _title.Text = "对局速览 · 显示测试       点击右侧收起";

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= NoActivateWindowStyle | ToolWindowStyle;
            return parameters;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == MouseActivateMessage)
        {
            // Mouse clicks work without taking game keyboard focus.
            m.Result = new IntPtr(MouseNoActivate);
            return;
        }
        base.WndProc(ref m);
    }

    public static Rectangle Placement(Rectangle game, Rectangle work)
    {
        int width = Math.Min(OverlayWidth, work.Width);
        int height = Math.Min(OverlayHeight, work.Height);
        int left = Math.Clamp(game.Left + (game.Width - width) / 2, work.Left, Math.Max(work.Left, work.Right - width));
        int top = Math.Clamp(game.Top + (game.Height - height) / 2, work.Top, Math.Max(work.Top, work.Bottom - height));
        return new(left, top, width, height);
    }
}
