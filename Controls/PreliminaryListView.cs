using League.ChampionSkin;
using League.PrimaryElection;

namespace League.Controls
{
    /// <summary>
    /// One preselection list painted as rows: priority, icon, name, lanes and up / down / remove buttons. Rows can be
    /// dragged to a new place; Delete removes the selected row and Ctrl+Up / Ctrl+Down move it. Every change renumbers
    /// the priorities and raises <see cref="Changed"/>.
    /// </summary>
    public sealed class PreliminaryListView : Control
    {
        private const int RowHeight = 58;
        private const int IconSize = 44;
        private const int ButtonSize = 28;
        private const int DragThreshold = 5;

        private enum RowButton
        {
            None,
            Up,
            Down,
            Remove,
        }

        private readonly ThinScrollBar _scroll = new(vertical: true);
        private readonly Font _nameFont = new(SkinBrowserTheme.FontName, 10.5F, FontStyle.Bold);
        private readonly Font _detailFont = new(SkinBrowserTheme.FontName, 8.5F);
        private readonly Font _numberFont = new(SkinBrowserTheme.FontName, 13F, FontStyle.Bold);
        private readonly Font _buttonFont = new(SkinBrowserTheme.FontName, 10F, FontStyle.Bold);
        private List<PreliminaryHero> _heroes = new();
        private int _selected = -1;
        private int _hover = -1;
        private RowButton _hoverButton;
        private int _pressed = -1;
        private Point _pressPoint;
        private bool _dragging;
        private int _dropIndex = -1;

        public PreliminaryListView()
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

        public Func<int, Image?> Icon { get; set; } = _ => null;

        public Func<int, PrimaryChampion?> Champion { get; set; } = _ => null;

        /// <summary>Shown when the list is empty.</summary>
        public string EmptyText { get; set; } = "";

        public event EventHandler? Changed;

        public IReadOnlyList<PreliminaryHero> Heroes => _heroes;

        public int SelectedIndex => _selected;

        /// <summary>Shows a stored list; the view edits it in place.</summary>
        public void SetList(List<PreliminaryHero> heroes)
        {
            _heroes = heroes;
            _selected = -1;
            _hover = -1;
            UpdateScroll();
            _scroll.ScrollTo(0);
            Invalidate();
        }

        /// <summary>Selects the champion's row and scrolls it into view.</summary>
        public void Highlight(int championId)
        {
            _selected = _heroes.FindIndex(h => h.ChampionId == championId);
            EnsureVisible(_selected);
            Invalidate();
        }

        /// <summary>Re-reads the list after an outside change, such as an added champion.</summary>
        public void Refresh(int highlightChampionId)
        {
            UpdateScroll();
            Highlight(highlightChampionId);
        }

        public void Move(int from, int to)
        {
            if (from < 0 || from >= _heroes.Count)
            {
                return;
            }

            to = Math.Clamp(to, 0, _heroes.Count - 1);
            if (from == to)
            {
                return;
            }

            var hero = _heroes[from];
            _heroes.RemoveAt(from);
            _heroes.Insert(to, hero);
            _selected = to;
            Commit();
            EnsureVisible(to);
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _heroes.Count)
            {
                return;
            }

            _heroes.RemoveAt(index);
            _selected = Math.Min(index, _heroes.Count - 1);
            _hover = -1;
            UpdateScroll();
            Commit();
        }

        private void Commit()
        {
            for (int i = 0; i < _heroes.Count; i++)
            {
                _heroes[i].Priority = i + 1;
            }

            Invalidate();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private int S(int value) => LogicalToDeviceUnits(value);

        private Rectangle Track => _scroll.Track(ClientRectangle, S(6), S(3));

        private Rectangle RowBounds(int index) =>
            new(S(8), S(8) + index * S(RowHeight) - _scroll.Value, ClientSize.Width - S(24), S(RowHeight) - S(6));

        private Rectangle ButtonBounds(Rectangle row, RowButton button)
        {
            int slot = button switch
            {
                RowButton.Up => 2,
                RowButton.Down => 1,
                _ => 0,
            };
            int size = S(ButtonSize);
            return new Rectangle(
                row.Right - S(10) - size - slot * (size + S(4)),
                row.Top + (row.Height - size) / 2,
                size,
                size
            );
        }

        private void UpdateScroll() => _scroll.Update(_heroes.Count * S(RowHeight) + S(16), ClientSize.Height);

        private void EnsureVisible(int index)
        {
            if (index < 0)
            {
                return;
            }

            var row = RowBounds(index);
            if (row.Top < 0)
            {
                _scroll.ScrollTo(_scroll.Value + row.Top - S(8));
            }
            else if (row.Bottom > ClientSize.Height)
            {
                _scroll.ScrollTo(_scroll.Value + row.Bottom - ClientSize.Height + S(8));
            }
        }

        private (int Index, RowButton Button) HitTest(Point point)
        {
            int index = (point.Y + _scroll.Value - S(8)) / S(RowHeight);
            if (point.Y + _scroll.Value < S(8) || index >= _heroes.Count)
            {
                return (-1, RowButton.None);
            }

            var row = RowBounds(index);
            if (!row.Contains(point))
            {
                return (-1, RowButton.None);
            }

            foreach (var button in new[] { RowButton.Up, RowButton.Down, RowButton.Remove })
            {
                if (ButtonBounds(row, button).Contains(point))
                {
                    return (index, button);
                }
            }

            return (index, RowButton.None);
        }

        // Where a dragged row lands: before the row under the pointer's upper half, after it otherwise.
        private int DropIndex(Point point)
        {
            int position = point.Y + _scroll.Value - S(8) + S(RowHeight) / 2;
            return Math.Clamp(position / S(RowHeight), 0, _heroes.Count);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScroll();
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

            var (index, button) = HitTest(e.Location);
            if (index < 0)
            {
                return;
            }

            _selected = index;
            switch (button)
            {
                case RowButton.Up:
                    Move(index, index - 1);
                    break;
                case RowButton.Down:
                    Move(index, index + 1);
                    break;
                case RowButton.Remove:
                    RemoveAt(index);
                    break;
                default:
                    _pressed = index;
                    _pressPoint = e.Location;
                    break;
            }

            Invalidate();
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

            if (_pressed >= 0 && !_dragging && Distance(e.Location, _pressPoint) > S(DragThreshold))
            {
                _dragging = true;
                Cursor = Cursors.SizeNS;
            }

            if (_dragging)
            {
                _dropIndex = DropIndex(e.Location);
                AutoScrollWhileDragging(e.Location);
                Invalidate();
                return;
            }

            var (hover, button) = HitTest(e.Location);
            if (hover != _hover || button != _hoverButton)
            {
                _hover = hover;
                _hoverButton = button;
                Cursor = button != RowButton.None ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        private void AutoScrollWhileDragging(Point point)
        {
            if (point.Y < S(16))
            {
                _scroll.ScrollTo(_scroll.Value - S(12));
            }
            else if (point.Y > ClientSize.Height - S(16))
            {
                _scroll.ScrollTo(_scroll.Value + S(12));
            }
        }

        private static int Distance(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _scroll.MouseUp();
            if (_dragging && _pressed >= 0 && _dropIndex >= 0)
            {
                // Removing the row first shifts every later slot up by one.
                Move(_pressed, _dropIndex > _pressed ? _dropIndex - 1 : _dropIndex);
            }

            _pressed = -1;
            _dragging = false;
            _dropIndex = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            _hoverButton = RowButton.None;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_scroll.ScrollTo(_scroll.Value - Math.Sign(e.Delta) * S(RowHeight)))
            {
                Invalidate();
            }
        }

        protected override bool IsInputKey(Keys keyData) =>
            (keyData & Keys.KeyCode) is Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_heroes.Count == 0)
            {
                return;
            }

            int current = Math.Max(0, _selected);
            switch (e.KeyCode)
            {
                case Keys.Delete:
                    RemoveAt(_selected);
                    break;
                case Keys.Up when e.Control:
                    Move(current, current - 1);
                    break;
                case Keys.Down when e.Control:
                    Move(current, current + 1);
                    break;
                case Keys.Up:
                case Keys.Down:
                    _selected = Math.Clamp(current + (e.KeyCode == Keys.Up ? -1 : 1), 0, _heroes.Count - 1);
                    EnsureVisible(_selected);
                    Invalidate();
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            if (_heroes.Count == 0)
            {
                TextRenderer.DrawText(
                    g,
                    EmptyText,
                    _detailFont,
                    ClientRectangle,
                    SkinBrowserTheme.MutedText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak
                );
                return;
            }

            for (int i = 0; i < _heroes.Count; i++)
            {
                var row = RowBounds(i);
                if (row.Bottom < 0)
                {
                    continue;
                }

                if (row.Top > ClientSize.Height)
                {
                    break;
                }

                PaintRow(g, row, i);
            }

            PaintDropLine(g);
            _scroll.Paint(g, Track, Track.Contains(PointToClient(MousePosition)));
        }

        private void PaintRow(Graphics g, Rectangle row, int index)
        {
            var hero = _heroes[index];
            bool selected = index == _selected;
            using (
                var fill = new SolidBrush(
                    selected || index == _hover ? SkinBrowserTheme.PanelHover : SkinBrowserTheme.Background
                )
            )
            {
                g.FillRectangle(fill, row);
            }

            if (selected)
            {
                using var accent = new SolidBrush(SkinBrowserTheme.Gold);
                g.FillRectangle(accent, row.Left, row.Top, S(3), row.Height);
            }

            var number = new Rectangle(row.Left + S(6), row.Top, S(34), row.Height);
            TextRenderer.DrawText(
                g,
                (index + 1).ToString(),
                _numberFont,
                number,
                index == 0 ? SkinBrowserTheme.GoldBright : SkinBrowserTheme.MutedText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );

            var icon = new Rectangle(
                number.Right + S(4),
                row.Top + (row.Height - S(IconSize)) / 2,
                S(IconSize),
                S(IconSize)
            );
            if (Icon(hero.ChampionId) is { } image)
            {
                g.DrawImage(image, icon);
            }
            else
            {
                using var placeholder = new SolidBrush(SkinBrowserTheme.Border);
                g.FillRectangle(placeholder, icon);
            }

            PaintText(g, row, icon, hero);
            foreach (var button in new[] { RowButton.Up, RowButton.Down, RowButton.Remove })
            {
                PaintButton(g, ButtonBounds(row, button), button, index, index == _hover && button == _hoverButton);
            }
        }

        private void PaintText(Graphics g, Rectangle row, Rectangle icon, PreliminaryHero hero)
        {
            var champion = Champion(hero.ChampionId);
            int left = icon.Right + S(12);
            int right = ButtonBounds(row, RowButton.Up).Left - S(8);
            string name =
                champion?.Name ?? (hero.ChampionName.Length > 0 ? hero.ChampionName : "英雄 " + hero.ChampionId);
            string detail =
                champion == null
                    ? "英雄资料中找不到，选人时仍按 ID 预选"
                    : string.Join(" · ", new[] { champion.Title, Lanes(champion) }.Where(t => t.Length > 0));
            var nameBounds = new Rectangle(left, row.Top + S(8), right - left, S(22));
            var detailBounds = new Rectangle(left, nameBounds.Bottom + S(2), right - left, S(18));
            TextRenderer.DrawText(
                g,
                name,
                _nameFont,
                nameBounds,
                SkinBrowserTheme.Text,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
            );
            TextRenderer.DrawText(
                g,
                detail,
                _detailFont,
                detailBounds,
                SkinBrowserTheme.MutedText,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
            );
        }

        private void PaintButton(Graphics g, Rectangle bounds, RowButton button, int index, bool hover)
        {
            bool enabled = button switch
            {
                RowButton.Up => index > 0,
                RowButton.Down => index < _heroes.Count - 1,
                _ => true,
            };
            if (hover && enabled)
            {
                using var fill = new SolidBrush(
                    button == RowButton.Remove ? Color.FromArgb(120, 60, 50) : SkinBrowserTheme.Border
                );
                g.FillRectangle(fill, bounds);
            }

            string glyph = button switch
            {
                RowButton.Up => "↑",
                RowButton.Down => "↓",
                _ => "×",
            };
            var color =
                !enabled ? Color.FromArgb(70, 80, 90)
                : hover ? Color.White
                : SkinBrowserTheme.MutedText;
            TextRenderer.DrawText(
                g,
                glyph,
                _buttonFont,
                bounds,
                color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
            );
        }

        private void PaintDropLine(Graphics g)
        {
            if (!_dragging || _dropIndex < 0)
            {
                return;
            }

            int y = S(8) + _dropIndex * S(RowHeight) - _scroll.Value - S(3);
            using var pen = new Pen(SkinBrowserTheme.GoldBright, S(2));
            g.DrawLine(pen, S(8), y, ClientSize.Width - S(16), y);
        }

        public static string Lanes(PrimaryChampion champion) =>
            string.Join(
                "/",
                champion
                    .GetPositions()
                    .Select(p =>
                        p switch
                        {
                            ChampionPosition.Top => "上单",
                            ChampionPosition.Jungle => "打野",
                            ChampionPosition.Mid => "中单",
                            ChampionPosition.ADC => "下路",
                            ChampionPosition.Support => "辅助",
                            _ => "",
                        }
                    )
                    .Where(p => p.Length > 0)
            );

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _nameFont.Dispose();
                _detailFont.Dispose();
                _numberFont.Dispose();
                _buttonFont.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
