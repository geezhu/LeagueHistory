namespace League.ChampionSkin
{
    /// <summary>
    /// A flat button for the dark skin browser. The system draws disabled flat buttons with embossed gray text that
    /// is unreadable on a dark background, so this one paints its own text in a muted color instead.
    /// </summary>
    internal sealed class SkinBrowserButton : Button
    {
        private bool _hover;

        public SkinBrowserButton(bool primary = false)
        {
            Primary = primary;
            SetStyle(
                ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,
                true
            );
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
            Padding = new Padding(10, 0, 10, 0);
        }

        /// <summary>A gold outline and text for the main action.</summary>
        public bool Primary { get; set; }

        /// <summary>Draws the button filled with gold, as the active choice of a group.</summary>
        public bool Active
        {
            get => _active;
            set
            {
                _active = value;
                Invalidate();
            }
        }

        private bool _active;

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

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var fill =
                Active ? SkinBrowserTheme.Gold
                : _hover && Enabled ? SkinBrowserTheme.PanelHover
                : SkinBrowserTheme.Panel;
            var border =
                !Enabled ? SkinBrowserTheme.Border
                : Primary || Active ? SkinBrowserTheme.Gold
                : SkinBrowserTheme.Border;
            var text =
                Active ? SkinBrowserTheme.Background
                : !Enabled ? SkinBrowserTheme.MutedText
                : Primary ? SkinBrowserTheme.GoldBright
                : SkinBrowserTheme.Text;
            using (var brush = new SolidBrush(fill))
            {
                g.FillRectangle(brush, ClientRectangle);
            }

            using (var pen = new Pen(border))
            {
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }

            TextRenderer.DrawText(
                g,
                Text,
                Font,
                ClientRectangle,
                text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
            );
            if (Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -3, -3), text, fill);
            }
        }
    }
}
