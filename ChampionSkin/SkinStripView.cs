using League.Models;

namespace League.ChampionSkin
{
    /// <summary>
    /// One champion's owned skins as a painted horizontal strip of tiles with their names. A click previews the skin,
    /// a double click makes it the automatic skin; the wheel scrolls sideways.
    /// </summary>
    public sealed class SkinStripView : Control
    {
        private const int CellWidth = 118;
        private const int TileSize = 96;

        private readonly ThinScrollBar _scroll = new(vertical: false);
        private readonly Font _nameFont = new(SkinBrowserTheme.FontName, 8.5F);
        private readonly Font _starFont = new(SkinBrowserTheme.FontName, 9F, FontStyle.Bold);
        private IReadOnlyList<SkinThumbItem> _items = [];
        private int _selected = -1;
        private int _hover = -1;
        private int _preferred;

        public SkinStripView()
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

        /// <summary>Returns the skin's tile once loaded, or null to draw a placeholder.</summary>
        public Func<int, Image?> Tile { get; set; } = _ => null;

        public event EventHandler? SelectionChanged;

        public event EventHandler? Activated;

        public SkinThumbItem? Selected => _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

        public IReadOnlyList<SkinThumbItem> Items => _items;

        public void SetItems(IReadOnlyList<SkinThumbItem> items, int selectedSkinId, int preferredSkinId)
        {
            _items = items;
            _preferred = preferredSkinId;
            _hover = -1;
            UpdateScroll();
            _scroll.ScrollTo(0);
            int index = items.ToList().FindIndex(s => s.SkinId == selectedSkinId);
            SelectIndex(
                index >= 0 ? index
                    : items.Count > 0 ? 0
                    : -1,
                force: true
            );
        }

        public void SetPreferred(int preferredSkinId)
        {
            _preferred = preferredSkinId;
            Invalidate();
        }

        private int S(int value) => LogicalToDeviceUnits(value);

        private int Content => _items.Count * S(CellWidth) + S(16);

        private Rectangle Track => _scroll.Track(ClientRectangle, S(5), S(4));

        // The bottom keeps room for the slim scroll bar under the names.
        private Rectangle CellBounds(int index) =>
            new(S(8) + index * S(CellWidth) - _scroll.Value, S(8), S(CellWidth), ClientSize.Height - S(22));

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

        private void UpdateScroll() => _scroll.Update(Content, ClientSize.Width);

        private void EnsureVisible(int index)
        {
            if (index < 0)
            {
                return;
            }

            var cell = CellBounds(index);
            if (cell.Left < 0)
            {
                _scroll.ScrollTo(_scroll.Value + cell.Left - S(8));
            }
            else if (cell.Right > ClientSize.Width)
            {
                _scroll.ScrollTo(_scroll.Value + cell.Right - ClientSize.Width + S(8));
            }
        }

        private int HitTest(Point point)
        {
            int x = point.X + _scroll.Value - S(8);
            if (x < 0 || point.Y > ClientSize.Height - S(14))
            {
                return -1;
            }

            int index = x / S(CellWidth);
            return index < _items.Count ? index : -1;
        }

        // The strip gets its final width after the window lays out; keep the selected tile in view.
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

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (HitTest(e.Location) is int index && index >= 0 && index == _selected)
            {
                Activated?.Invoke(this, EventArgs.Empty);
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
            if (_scroll.ScrollTo(_scroll.Value - Math.Sign(e.Delta) * S(CellWidth)))
            {
                Invalidate();
            }
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Left or Keys.Right && _items.Count > 0)
            {
                SelectIndex(Math.Clamp(_selected + (e.KeyCode == Keys.Left ? -1 : 1), 0, _items.Count - 1));
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter && Selected != null)
            {
                Activated?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            for (int i = 0; i < _items.Count; i++)
            {
                var cell = CellBounds(i);
                if (cell.Right < 0)
                {
                    continue;
                }

                if (cell.Left > ClientSize.Width)
                {
                    break;
                }

                PaintCell(g, cell, _items[i], i == _selected, i == _hover);
            }

            _scroll.Paint(g, Track, Track.Contains(PointToClient(MousePosition)));
        }

        private void PaintCell(Graphics g, Rectangle cell, SkinThumbItem skin, bool selected, bool hover)
        {
            var tile = new Rectangle(
                cell.Left + (cell.Width - S(TileSize)) / 2,
                cell.Top + S(4),
                S(TileSize),
                S(TileSize)
            );
            if (Tile(skin.SkinId) is { } image)
            {
                g.DrawImage(image, tile);
            }
            else
            {
                using var placeholder = new SolidBrush(SkinBrowserTheme.Border);
                g.FillRectangle(placeholder, tile);
            }

            if (!selected && !hover)
            {
                // Unselected tiles are dimmed slightly so the chosen one stands out.
                using var dim = new SolidBrush(Color.FromArgb(70, 0, 0, 0));
                g.FillRectangle(dim, tile);
            }

            var frameColor =
                selected ? SkinBrowserTheme.GoldBright
                : hover ? SkinBrowserTheme.Gold
                : SkinBrowserTheme.Border;
            using (var frame = new Pen(frameColor, selected ? S(3) : 1))
            {
                g.DrawRectangle(frame, tile);
            }

            if (!skin.IsDefault && skin.SkinId == _preferred)
            {
                var star = new Rectangle(tile.Right - S(24), tile.Top + S(4), S(20), S(20));
                using var fill = new SolidBrush(Color.FromArgb(220, 17, 21, 26));
                g.FillEllipse(fill, star);
                TextRenderer.DrawText(
                    g,
                    "★",
                    _starFont,
                    star,
                    SkinBrowserTheme.GoldBright,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                );
            }

            var name = new Rectangle(
                cell.Left + S(4),
                tile.Bottom + S(6),
                cell.Width - S(8),
                cell.Bottom - tile.Bottom - S(6)
            );
            TextRenderer.DrawText(
                g,
                skin.IsDefault ? "默认" : skin.Name,
                _nameFont,
                name,
                selected ? SkinBrowserTheme.GoldBright : SkinBrowserTheme.Text,
                TextFormatFlags.HorizontalCenter
                    | TextFormatFlags.WordBreak
                    | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPrefix
            );
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _nameFont.Dispose();
                _starFont.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
