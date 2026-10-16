namespace League.Controls;

/// <summary>
/// Small edge tab beside the League client that reopens a closed champion select assistant.
/// It uses the panel's own colors, a chevron toward where the panel opens, and a two-character label.
/// It never takes focus, so clicking it leaves keyboard input with the client.
/// </summary>
internal sealed class AssistantToggleTab : Form
{
    private const int NoActivate = 0x08000000;
    private const int ToolWindow = 0x00000080;
    private const string Caption = "助手";
    private const string ChevronLeft = "";
    private const string ChevronRight = "";
    private static readonly Color Surface = Color.FromArgb(16, 45, 57);
    private static readonly Color HoverSurface = Color.FromArgb(22, 62, 75);
    private static readonly Color Border = Color.FromArgb(49, 83, 95);
    private static readonly Color Muted = Color.FromArgb(150, 172, 182);
    private readonly Font _glyph = new("Segoe MDL2 Assets", 8);
    private bool _panelOnLeft = true;
    private bool _hover;

    public AssistantToggleTab(Font font)
    {
        Text = "选人助手";
        AccessibleName = "展开选人助手";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        BackColor = Surface;
        Cursor = Cursors.Hand;
        Font = font;
    }

    public event EventHandler? Toggled;

    /// <summary>Which side of the client the panel uses, so the chevron points the way it moves.</summary>
    public bool PanelOnLeft
    {
        get => _panelOnLeft;
        set
        {
            if (_panelOnLeft != value)
            {
                _panelOnLeft = value;
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

    /// <summary>Height for the chevron above the vertical two-character label.</summary>
    public Size PreferredTabSize(int width, int padding) =>
        new(width, _glyph.Height + Caption.Length * Font.Height + padding * 3);

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
        e.Graphics.Clear(_hover ? HoverSurface : Surface);
        using (var border = new Pen(Border))
        {
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        int padding = (Height - _glyph.Height - Caption.Length * Font.Height) / 3;
        var color = _hover ? AccentColors.Accent : Muted;
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(
            e.Graphics,
            _panelOnLeft ? ChevronLeft : ChevronRight,
            _glyph,
            new Rectangle(0, padding, Width, _glyph.Height),
            color,
            flags
        );
        int top = padding * 2 + _glyph.Height;
        for (int i = 0; i < Caption.Length; i++)
        {
            TextRenderer.DrawText(
                e.Graphics,
                Caption[i].ToString(),
                Font,
                new Rectangle(0, top + i * Font.Height, Width, Font.Height),
                _hover ? AccentColors.Accent : Color.FromArgb(215, 225, 226),
                flags
            );
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _glyph.Dispose();
        }

        base.Dispose(disposing);
    }
}
