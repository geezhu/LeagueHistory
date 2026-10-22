using League.Models;

namespace League.Controls;

/// <summary>
/// The augment tab as a grid of small cards: icon, name, and the win rate with the pick rate and games beside it. The
/// rarity shows in the icon's own frame and the description is a hover or a click away, so a card holds only what
/// the comparison needs and the grid can use the whole width.
/// </summary>
internal sealed partial class AramMetaView
{
    private const string AugmentGridTag = "augment-grid";
    private const int AllRarities = 3;
    private const int AugmentCardMinimumWidth = 200;
    private const int AugmentCardGap = 8;

    private void RenderAugmentComparison()
    {
        var host = _detailBody!;
        var filters = RenderRarityFilters(host);
        var grid = new FlowLayoutPanel
        {
            Tag = AugmentGridTag,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Margin = new Padding(0, 6, 0, 0),
        };
        host.Controls.Add(grid);
        FitAugmentGrid(grid, Math.Max(510, host.Width - 24));
        RenderAugmentTools(filters, grid);
        PopulateAugments(grid);
    }

    private FlowLayoutPanel RenderRarityFilters(Control host)
    {
        var filters = Row(host);
        var names = RarityNames.Append("全部").ToArray();
        for (int i = 0; i < names.Length; i++)
        {
            int rarity = i;
            var button = new Button
            {
                Text = names[i],
                Width = i == AllRarities ? 64 : 100,
                Height = 30,
            };
            StyleChoice(button, i == _rarity);
            filters.Controls.Add(button);
            button.Click += (_, _) =>
            {
                _rarity = rarity;
                RenderDetailBody();
            };
        }
        return filters;
    }

    // Search and sort join the rarity buttons on one line instead of taking a line of their own.
    private void RenderAugmentTools(Control tools, FlowLayoutPanel grid)
    {
        var search = new TextBox
        {
            Width = 180,
            PlaceholderText = "搜索强化",
            Text = _augmentSearch,
            Margin = new Padding(16, 4, 3, 3),
        };
        var sort = new ComboBox
        {
            Width = 150,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(3, 4, 3, 3),
        };
        sort.Items.AddRange(new object[] { "来源推荐顺序", "调整后胜率", "选用率" });
        sort.SelectedIndex = _augmentSort;
        tools.Controls.AddRange(new Control[] { search, sort });
        search.TextChanged += (_, _) =>
        {
            _augmentSearch = search.Text;
            PopulateAugments(grid);
        };
        sort.SelectedIndexChanged += (_, _) =>
        {
            _augmentSort = sort.SelectedIndex;
            PopulateAugments(grid);
        };
    }

    private void PopulateAugments(FlowLayoutPanel grid)
    {
        grid.SuspendLayout();
        try
        {
            foreach (Control child in grid.Controls.Cast<Control>().ToArray())
            {
                child.Dispose();
            }
            var rarities = _rarity == AllRarities ? Enumerable.Range(0, RarityKeys.Length) : new[] { _rarity };
            var shown = AramMetaRecommendation
                .Sort(rarities.SelectMany(rarity => Recommendations(RarityKeys[rarity])), _augmentSort)
                .Where(r => RecommendationName(r, true).Contains(_augmentSearch, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            int width = CardWidth(grid.MaximumSize.Width);
            foreach (var entry in shown)
            {
                AddAugmentCard(grid, entry, width);
            }
            if (shown.Length == 0)
            {
                grid.Controls.Add(Text("没有匹配的强化"));
            }
        }
        finally
        {
            grid.ResumeLayout(true);
        }
    }

    private void AddAugmentCard(FlowLayoutPanel grid, AramMetaRecommendation entry, int width)
    {
        string name = RecommendationName(entry, true);
        var card = new AugmentCard(name, entry)
        {
            Width = width,
            Margin = new Padding(0, 0, AugmentCardGap, AugmentCardGap),
        };
        grid.Controls.Add(card);
        var icon = Icon(card, null, name, _detailToken, 40, "augment", entry.Ids[0]);
        icon.Location = new Point(8, 8);
        string description = FormMain.Globals.resLoading.GetAugmentById(entry.Ids[0])?.Description ?? entry.Description;
        string details =
            TooltipText.Format(name, description)
            + $"\n\n调整后胜率 {entry.WinRate:P2} · 选用率 {entry.PickRate:P2} · 来源样本 {entry.Games:N0} 场"
            + "\n来源推荐顺序不等于胜率排名，统计也不证明与某套出装存在联动。";
        // A click keeps the description up on a timer; changing the rarity, search or sort rebuilds the grid within
        // that time, and the timer then hid the tooltip from a disposed card and crashed. Hide it while the card's
        // window still exists.
        card.HandleDestroyed += (_, _) => _tips.Hide(card);
        // Hover shows the description; a click keeps it up for reading.
        foreach (Control target in new Control[] { card, icon })
        {
            _tips.SetToolTip(target, details);
            target.Cursor = Cursors.Hand;
            target.Click += (_, _) => _tips.Show(details, card, 0, card.Height + 2, 15000);
        }
    }

    // Every card carries its gap as a right margin, the last one in a row included, so a row of n cards needs
    // n × (width + gap) and the cards are sized to fit exactly that.
    private static int AugmentColumns(int width) => Math.Max(1, width / (AugmentCardMinimumWidth + AugmentCardGap));

    private static int CardWidth(int gridWidth) =>
        Math.Max(AugmentCardMinimumWidth, gridWidth / AugmentColumns(gridWidth) - AugmentCardGap);

    // Called by ResizeSections: the grid takes the section's width and its cards share it evenly.
    private static void FitAugmentGrid(Control grid, int width)
    {
        grid.MinimumSize = new Size(width, 0);
        grid.MaximumSize = new Size(width, 0);
        int cardWidth = CardWidth(width);
        foreach (var card in grid.Controls.OfType<AugmentCard>())
        {
            card.Width = cardWidth;
        }
    }

    private static void ResizeComparison(Control root, int width)
    {
        foreach (Control child in root.Controls)
        {
            if (child.Tag is string tag && tag == AugmentGridTag)
            {
                FitAugmentGrid(child, width);
            }
            ResizeComparison(child, width);
        }
    }

    /// <summary>One augment: the name over its win rate, split green and red at 50%, then the pick rate and games.</summary>
    private sealed class AugmentCard : Panel
    {
        private const int TextLeft = 56;
        private readonly string _name;
        private readonly AramMetaRecommendation _entry;
        private bool _hover;

        public AugmentCard(string name, AramMetaRecommendation entry)
        {
            _name = name;
            _entry = entry;
            DoubleBuffered = true;
            Height = 56;
            BackColor = Theme.SurfaceSubtle;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            // Moving onto the icon also leaves the card; only a real exit clears the outline.
            _hover = ClientRectangle.Contains(PointToClient(Cursor.Position));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter;
            int width = Math.Max(1, Width - TextLeft - 8);
            using var strong = new Font(Font, FontStyle.Bold);
            TextRenderer.DrawText(
                g,
                _name,
                strong,
                new Rectangle(TextLeft, 8, width, 20),
                Theme.Ink,
                flags | TextFormatFlags.EndEllipsis
            );
            var winColor =
                _entry.WinRate is not double rate ? Theme.Muted
                : rate >= 0.5 ? Theme.Win
                : Theme.Loss;
            string win = _entry.WinRate is double value ? $"{value:P1}" : "—";
            int winWidth = TextRenderer.MeasureText(g, win, strong, Size.Empty, flags).Width;
            TextRenderer.DrawText(g, win, strong, new Rectangle(TextLeft, 30, winWidth, 18), winColor, flags);
            TextRenderer.DrawText(
                g,
                $"选用 {_entry.PickRate:P1} · {_entry.Games:N0} 场",
                Font,
                new Rectangle(TextLeft + winWidth + 8, 30, Math.Max(1, width - winWidth - 8), 18),
                Theme.Muted,
                flags | TextFormatFlags.EndEllipsis
            );
            if (_hover)
            {
                using var outline = new Pen(Theme.Accent);
                g.DrawRectangle(outline, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
