namespace League.Controls;

/// <summary>Dark drop-down menus that match the assistant panels instead of the light system menu.</summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color Surface = Color.FromArgb(12, 36, 47);
    private static readonly Color Hover = Color.FromArgb(22, 62, 75);
    private static readonly Color Border = Color.FromArgb(49, 83, 95);
    private static readonly Color Text = Color.FromArgb(215, 225, 226);

    public DarkMenuRenderer()
        : base(new Colors()) => RoundedEdges = false;

    public static void Apply(ContextMenuStrip menu)
    {
        menu.Renderer = new DarkMenuRenderer();
        menu.BackColor = Surface;
        menu.ForeColor = Text;
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = true;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Text : Color.FromArgb(105, 128, 138);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Text;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // The default check box draws a light square; a plain accent tick reads better on the dark surface.
        var box = e.ImageRectangle;
        using var pen = new Pen(AccentColors.Accent, 2);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(
            pen,
            new[]
            {
                new Point(box.Left + box.Width / 5, box.Top + box.Height / 2),
                new Point(box.Left + box.Width * 2 / 5, box.Bottom - box.Height / 4),
                new Point(box.Right - box.Width / 6, box.Top + box.Height / 4),
            }
        );
    }

    private sealed class Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Surface;
        public override Color CheckBackground => Surface;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
    }
}
