using League.Models;

namespace League.ChampionSkin
{
    /// <summary>
    /// The champion list as one painted grid: icon, name, the number of owned skins and a star for a chosen automatic
    /// skin. Only visible rows are drawn, so filtering and scrolling stay quick with every champion listed.
    /// </summary>
    public sealed class ChampionGridView : Control
    {
        private const int CellWidth = 80;
        private const int CellHeight = 98;
        private const int IconSize = 60;

        private readonly ThinScrollBar _scroll = new(vertical: true);
        private readonly Font _nameFont = new(SkinBrowserTheme.FontName, 9F);
        private readonly Font _badgeFont = new(SkinBrowserTheme.FontName, 7.5F, FontStyle.Bold);
        private IReadOnlyList<ChampionSkinCardModel> _items = [];
        private int _selected = -1;
        private int _hover = -1;

        public ChampionGridView()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.Selectable,
                true
            );
            BackColor = SkinBrowserTheme.Panel;
        }

        /// <summary>Returns the champion's icon once loaded, or null to draw a placeholder.</summary>
        public Func<int, Image?> Icon { get; set; } = _ => null;

        public event EventHandler? SelectionChanged;

        public ChampionSkinCardModel? Selected => _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

        public IReadOnlyList<ChampionSkinCardModel> Items => _items;

        /// <summary>Shows a new list and keeps the selected champion when it is still listed, else the first.</summary>
        public void SetItems(IReadOnlyList<ChampionSkinCardModel> items)
        {
            int keep = Selected?.ChampionId ?? 0;
            _items = items;
            _hover = -1;
            int index = items.ToList().FindIndex(m => m.ChampionId == keep);
            UpdateScroll();
            SelectIndex(
                index >= 0 ? index
                    : items.Count > 0 ? 0
                    : -1,
                force: true
            );
        }

        public void SelectChampion(int championId) =>
            SelectIndex(_items.ToList().FindIndex(m => m.ChampionId == championId));

        private void SelectIndex(int index, bool force = false)
        {
            bool changed = force || index != _selected;
            _selected = index;
            EnsureVisible(index);
            Invalidate();
            if (changed)
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private int S(int value) => LogicalToDeviceUnits(value);

        // A fixed gutter for the slim scroll bar keeps the columns from shifting when it appears.
        private int ScrollWidth => S(12);

        private Rectangle Track => _scroll.Track(ClientRectangle, S(6), S(3));

        private int Columns => Math.Max(1, (ClientSize.Width - ScrollWidth - S(8)) / S(CellWidth));

        private int Rows => (_items.Count + Columns - 1) / Columns;

        // Spare width is split evenly so the grid stays centered.
        private int LeftMargin => Math.Max(S(4), (ClientSize.Width - ScrollWidth - Columns * S(CellWidth)) / 2);

        private Rectangle CellBounds(int index) =>
            new(
                LeftMargin + index % Columns * S(CellWidth),
                S(6) + index / Columns * S(CellHeight) - _scroll.Value,
                S(CellWidth),
                S(CellHeight)
            );

        private void UpdateScroll() => _scroll.Update(Rows * S(CellHeight) + S(12), ClientSize.Height);

        private void EnsureVisible(int index)
        {
            if (index < 0)
            {
                return;
            }

            var cell = CellBounds(index);
            if (cell.Top < 0)
            {
                _scroll.ScrollTo(_scroll.Value + cell.Top - S(6));
            }
            else if (cell.Bottom > ClientSize.Height)
            {
                _scroll.ScrollTo(_scroll.Value + cell.Bottom - ClientSize.Height + S(6));
            }
        }

        private int HitTest(Point point)
        {
            if (point.X >= ClientSize.Width - ScrollWidth)
            {
                return -1;
            }

            int column = (point.X - LeftMargin) / S(CellWidth);
            int row = (point.Y + _scroll.Value - S(6)) / S(CellHeight);
            if (point.X < LeftMargin || column >= Columns || point.Y + _scroll.Value < S(6))
            {
                return -1;
            }

            int index = row * Columns + column;
            return index < _items.Count ? index : -1;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScroll();
            EnsureVisible(_selected);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_scroll.Dragging)
            {
                if (_scroll.MouseMove(e.Location, Track))
                {
                    Invalidate();
                }

                return;
            }

            int hover = HitTest(e.Location);
            if (hover != _hover)
            {
                _hover = hover;
                Cursor = hover >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (_scroll.MouseDown(e.Location, Track))
            {
                Invalidate();
                return;
            }

            int index = HitTest(e.Location);
            if (index >= 0)
            {
                SelectIndex(index);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _scroll.MouseUp();
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_scroll.ScrollTo(_scroll.Value - Math.Sign(e.Delta) * S(CellHeight)))
            {
                Invalidate();
            }
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = e.KeyCode switch
            {
                Keys.Left => -1,
                Keys.Right => 1,
                Keys.Up => -Columns,
                Keys.Down => Columns,
                _ => 0,
            };
            if (step != 0 && _items.Count > 0)
            {
                SelectIndex(Math.Clamp(Math.Max(0, _selected) + step, 0, _items.Count - 1));
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            if (_items.Count == 0)
            {
                TextRenderer.DrawText(
                    g,
                    "没有符合条件的英雄",
                    _nameFont,
                    ClientRectangle,
                    SkinBrowserTheme.MutedText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                );
                return;
            }

            int first = Math.Max(0, (_scroll.Value - S(6)) / S(CellHeight) * Columns);
            for (int i = first; i < _items.Count; i++)
            {
                var cell = CellBounds(i);
                if (cell.Top > ClientSize.Height)
                {
                    break;
                }

                PaintCell(g, cell, _items[i], i == _selected, i == _hover);
            }

            _scroll.Paint(g, Track, Track.Contains(PointToClient(MousePosition)));
        }

        private void PaintCell(Graphics g, Rectangle cell, ChampionSkinCardModel model, bool selected, bool hover)
        {
            var inner = Rectangle.Inflate(cell, -S(3), -S(3));
            if (selected || hover)
            {
                using var fill = new SolidBrush(SkinBrowserTheme.PanelHover);
                g.FillRectangle(fill, inner);
            }

            var icon = new Rectangle(
                cell.Left + (cell.Width - S(IconSize)) / 2,
                cell.Top + S(8),
                S(IconSize),
                S(IconSize)
            );
            if (Icon(model.ChampionId) is { } image)
            {
                g.DrawImage(image, icon);
            }
            else
            {
                using var placeholder = new SolidBrush(SkinBrowserTheme.Border);
                g.FillRectangle(placeholder, icon);
            }

            using (
                var frame = new Pen(
                    selected ? SkinBrowserTheme.GoldBright : SkinBrowserTheme.Border,
                    selected ? S(2) : 1
                )
            )
            {
                g.DrawRectangle(frame, icon);
            }

            PaintBadges(g, icon, model);
            var name = new Rectangle(cell.Left + S(2), icon.Bottom + S(4), cell.Width - S(4), S(20));
            TextRenderer.DrawText(
                g,
                model.Name,
                _nameFont,
                name,
                selected ? SkinBrowserTheme.GoldBright : SkinBrowserTheme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
            );
        }

        // Top right: owned skins besides the default. Bottom left: a star when an automatic skin is chosen.
        private void PaintBadges(Graphics g, Rectangle icon, ChampionSkinCardModel model)
        {
            int count = model.NonDefaultSkinCount;
            if (count > 0)
            {
                string text = count.ToString();
                var size = TextRenderer.MeasureText(g, text, _badgeFont, Size.Empty, TextFormatFlags.NoPadding);
                var badge = new Rectangle(icon.Right - size.Width - S(6), icon.Top - S(4), size.Width + S(8), S(16));
                using var fill = new SolidBrush(SkinBrowserTheme.Badge);
                g.FillRectangle(fill, badge);
                TextRenderer.DrawText(
                    g,
                    text,
                    _badgeFont,
                    badge,
                    Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                );
            }

            if (model.PreferredSkinId > 0)
            {
                var star = new Rectangle(icon.Left - S(4), icon.Bottom - S(14), S(18), S(18));
                using var fill = new SolidBrush(Color.FromArgb(220, 17, 21, 26));
                g.FillEllipse(fill, star);
                TextRenderer.DrawText(
                    g,
                    "★",
                    _badgeFont,
                    star,
                    SkinBrowserTheme.GoldBright,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                );
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _nameFont.Dispose();
                _badgeFont.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
