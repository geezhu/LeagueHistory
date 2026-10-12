namespace League.Controls;

/// <summary>
/// Vertical edge tab beside the League client that opens or closes the champion select assistant.
/// It never takes focus, so clicking it leaves keyboard input with the client.
/// </summary>
internal sealed class AssistantToggleTab : Form
{
    private const int NoActivate = 0x08000000;
    private const int ToolWindow = 0x00000080;
    private static readonly Color Gold = Color.FromArgb(200, 170, 110);
    private static readonly Color DimGold = Color.FromArgb(120, 104, 72);
    private bool _open = true;
    private bool _hover;

    public AssistantToggleTab(Font font)
    {
        Text = "选人助手";
        AccessibleName = "打开或关闭选人助手";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(10, 32, 43);
        Cursor = Cursors.Hand;
        Font = font;
    }

    public event EventHandler? Toggled;

    public bool Open
    {
        get => _open;
        set
        {
            if (_open != value)
            {
                _open = value;
                Invalidate();
            }
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= NoActivate | ToolWindow;
            return parameters;
        }
    }

    /// <summary>Height that fits the vertical caption, one character per line.</summary>
    public Size PreferredTabSize(int width, int padding) => new(width, Text.Length * (Font.Height + 1) + padding * 2);

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left)
        {
            Toggled?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var accent = _open || _hover ? Gold : DimGold;
        using (var border = new Pen(accent))
        {
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        int lineHeight = Font.Height + 1;
        int top = (Height - Text.Length * lineHeight) / 2;
        for (int i = 0; i < Text.Length; i++)
        {
            var line = new Rectangle(0, top + i * lineHeight, Width, lineHeight);
            TextRenderer.DrawText(
                e.Graphics,
                Text[i].ToString(),
                Font,
                line,
                accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
            );
        }
    }
}
