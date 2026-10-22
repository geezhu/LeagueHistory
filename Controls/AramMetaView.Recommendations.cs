using League.Models;

namespace League.Controls;

internal sealed partial class AramMetaView
{
    private FlowLayoutPanel? _overviewEquipment;
    private string _augmentSearch = "";
    private readonly Dictionary<string, int> _recommendationChoices = new();
    private static readonly string[] RarityKeys = { "kPrismatic", "kGold", "kSilver" };
    private static readonly string[] RarityNames = { "棱彩强化", "黄金强化", "白银强化" };

    private AramMetaRecommendation[] Recommendations(string category) =>
        AramMetaRecommendation.Read(_detailData!, _detailHero, category);

    private string RecommendationName(AramMetaRecommendation r, bool augment) =>
        augment ? FormMain.Globals.resLoading.GetAugmentById(r.Ids[0])?.Name ?? r.Name : r.Name;

    // A single item as a small tile, several to a row, like the OP.GG guide's later items: twenty full-width rows
    // used to fill the left of the page and leave the rest empty.
    private void ItemTile(Control parent, AramMetaRecommendation entry)
    {
        int id = entry.Ids[0];
        var tile = new Panel
        {
            Size = new Size(140, 46),
            Margin = new Padding(0, 0, 8, 8),
            BackColor = Theme.SurfaceSubtle,
        };
        parent.Controls.Add(tile);
        var icon = new FlowLayoutPanel
        {
            Bounds = new Rectangle(4, 4, 40, 40),
            Margin = Padding.Empty,
            WrapContents = false,
        };
        tile.Controls.Add(icon);
        string name = FormMain.Globals.resLoading.GetItemById(id)?.Name ?? entry.Name;
        Icon(icon, null, TooltipText.Format(name, ""), _detailToken, 36, "item", id);
        tile.Paint += (_, e) =>
        {
            var flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter;
            int half = tile.Height / 2;
            var winColor =
                entry.WinRate is not double rate ? Theme.Muted
                : rate >= 0.5 ? Theme.Win
                : Theme.Loss;
            using var strong = new Font(tile.Font, FontStyle.Bold);
            var text = new Rectangle(48, 2, tile.Width - 50, half - 2);
            TextRenderer.DrawText(e.Graphics, $"胜率 {entry.WinRate:P1}", strong, text, winColor, flags);
            text.Y = half;
            TextRenderer.DrawText(e.Graphics, $"选用 {entry.PickRate:P1}", tile.Font, text, Theme.Muted, flags);
        };
        _tips.SetToolTip(tile, $"{name} · 胜率 {entry.WinRate:P1} · 选用率 {entry.PickRate:P1} · {entry.Games:N0} 场");
    }

    private void RenderOverview()
    {
        var host = _detailBody!;
        host.Controls.Add(Text("强化精选 · 按来源推荐顺序"));
        var board = Row(host);
        int available = Math.Max(510, host.Width - 56);
        int columnWidth = available >= 850 ? (available - 24) / 3 : available;
        for (int i = 0; i < 3; i++)
        {
            var column = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Width = columnWidth,
                MinimumSize = new Size(columnWidth, 0),
                MaximumSize = new Size(columnWidth, 0),
                BackColor = Color.FromArgb(239, 245, 248),
                Margin = new Padding(0, 0, 8, 8),
                Padding = new Padding(8),
            };
            board.Controls.Add(column);
            column.Controls.Add(Text(RarityNames[i]));
            foreach (var entry in Recommendations(RarityKeys[i]).Take(2))
            {
                var tile = new Panel { Size = new Size(columnWidth - 16, 48), Margin = new Padding(0, 0, 0, 2) };
                column.Controls.Add(tile);
                var icons = new FlowLayoutPanel
                {
                    Location = new Point(0, 3),
                    Size = new Size(42, 42),
                    Margin = Padding.Empty,
                };
                tile.Controls.Add(icons);
                Icon(icons, null, RecommendationName(entry, true), _detailToken, 36, "augment", entry.Ids[0]);
                var name = new Label
                {
                    Text = RecommendationName(entry, true),
                    Location = new Point(44, 3),
                    Size = new Size(columnWidth - 64, 20),
                    AutoEllipsis = true,
                };
                var metric = new Label
                {
                    Text = $"调整后胜率 {entry.WinRate:P1}",
                    Location = new Point(44, 24),
                    AutoSize = true,
                    ForeColor = Theme.Win,
                };
                tile.Controls.AddRange(new Control[] { name, metric });
                _tips.SetToolTip(
                    name,
                    TooltipText.Format(
                        name.Text,
                        FormMain.Globals.resLoading.GetAugmentById(entry.Ids[0])?.Description ?? entry.Description
                    ) + $"\n选用率 {entry.PickRate:P1} · {entry.Games:N0} 场"
                );
            }
        }
        _overviewEquipment = Row(host);
        _overviewEquipment.FlowDirection = FlowDirection.TopDown;
        _overviewEquipment.WrapContents = false;
        RenderOverviewEquipment();
    }

    private void RenderOverviewEquipment()
    {
        var host = _overviewEquipment!;
        foreach (Control child in host.Controls.Cast<Control>().ToArray())
        {
            child.Dispose();
        }

        if (_detailData!.Detail == null)
        {
            host.Controls.Add(Text(_detailFailed ? "出装推荐暂不可用，可刷新重试。" : "正在加载出装与召唤师技能…"));
            return;
        }
        var pair = Row(host);
        // This host is a narrow top-down column when the row is made, so the row takes the page's width instead.
        pair.MaximumSize = new Size(Math.Max(330, _detailBody!.Width - 24), 0);
        foreach (var (key, title) in new[] { ("items", "核心装备"), ("boots", "鞋子") })
        {
            LabeledChooser(pair, title, key, Recommendations(key));
        }
        var more = new Button { Text = "查看后续装备 ›", AutoSize = true };
        ButtonStyles.Ghost(more);
        host.Controls.Add(more);
        more.Click += (_, _) =>
            ((Button)_detail.Controls[1].Controls.OfType<FlowLayoutPanel>().Single().Controls[2]).PerformClick();
        RenderSpellHighlights(host);
    }

    private void RenderSpellHighlights(Control host)
    {
        host.Controls.Add(Text("召唤师技能"));
        var columns = Row(host);
        // On the overview the host is a narrow column when this row is made; the cards share the page's width.
        columns.MaximumSize = new Size(Math.Max(330, _detailBody!.Width - 24), 0);
        foreach (var (title, entry) in AramMetaRecommendation.SpellHighlights(Recommendations("spells")))
        {
            var card = new FlowLayoutPanel
            {
                Width = 230,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(230, 0),
                MaximumSize = new Size(230, 0),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(8),
                Margin = new Padding(0, 0, 8, 4),
                BackColor = Color.FromArgb(239, 245, 248),
            };
            columns.Controls.Add(card);
            card.Controls.Add(Text(title));
            if (entry == null)
            {
                card.Controls.Add(Text("暂无数据"));
                continue;
            }
            var icons = Row(card);
            foreach (int id in entry.Ids)
            {
                Icon(
                    icons,
                    null,
                    TooltipText.Format(
                        FormMain.Globals.resLoading.GetSpellById(id)?.Name ?? id.ToString(),
                        FormMain.Globals.resLoading.GetSpellById(id)?.Description ?? ""
                    ),
                    _detailToken,
                    32,
                    "spell",
                    id
                );
            }

            card.Controls.Add(Text($"胜率 {entry.WinRate:P1} · 选用率 {entry.PickRate:P1}", Theme.Win));
            _tips.SetToolTip(card, $"样本 {entry.Games:N0} 场");
        }
    }

    // Labels sit above their selectors, the boots selector no longer repeats its own numbers underneath, and the
    // single items follow as tiles.
    private void RenderBuildComparison()
    {
        var host = _detailBody!;
        if (_detailData!.Detail == null)
        {
            host.Controls.Add(Text(_detailFailed ? "详情加载失败，请刷新重试。" : "正在加载出装详情…"));
            return;
        }
        var cores = Recommendations("items");
        var pair = Row(host);
        var core = LabeledChooser(pair, "装备组合", "items", cores);
        LabeledChooser(pair, "鞋子", "boots", Recommendations("boots"));
        host.Controls.Add(Text("单件备选 · 已排除所选组合中的装备（不是固定购买顺序）"));
        var later = Row(host);
        core.SelectedIndexChanged += (_, _) => RenderLaterEquipment(later, cores, core.SelectedIndex);
        RenderLaterEquipment(later, cores, core.SelectedIndex);
        RenderSpellHighlights(host);
    }

    // A selector under its own label. Two of them share a wrapping row, so they sit side by side on a wide page
    // instead of stacking with half the page empty beside them.
    private EquipmentComboBox LabeledChooser(
        Control row,
        string title,
        string category,
        AramMetaRecommendation[] options
    )
    {
        var block = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 0, 16, 4),
        };
        row.Controls.Add(block);
        block.Controls.Add(Text(title));
        return CreateEquipmentChooser(block, category, options);
    }

    private EquipmentComboBox CreateEquipmentChooser(Control host, string category, AramMetaRecommendation[] options)
    {
        var chooser = new EquipmentComboBox(options.Select(r => r.ToOption()).ToArray(), _detailToken)
        {
            Width = 430,
            Light = true,
        };
        host.Controls.Add(chooser);
        chooser.SelectedIndex = Math.Clamp(
            _recommendationChoices.GetValueOrDefault($"{_detailHero}:{category}"),
            0,
            chooser.Items.Count - 1
        );
        chooser.SelectedIndexChanged += (_, _) =>
            _recommendationChoices[$"{_detailHero}:{category}"] = chooser.SelectedIndex;
        return chooser;
    }

    private void RenderLaterEquipment(Control host, AramMetaRecommendation[] cores, int selectedIndex)
    {
        host.SuspendLayout();
        try
        {
            foreach (Control child in host.Controls.Cast<Control>().ToArray())
            {
                child.Dispose();
            }
            if (cores.Length == 0)
            {
                host.Controls.Add(Text("暂无装备组合"));
                return;
            }
            _recommendationChoices[$"{_detailHero}:items"] = selectedIndex;
            var excluded = cores[selectedIndex].Ids.ToHashSet();
            var seen = new HashSet<int>();
            foreach (
                var item in Recommendations("singleItems")
                    .Where(r => r.Ids.All(id => !excluded.Contains(id)) && seen.Add(r.Ids[0]))
            )
            {
                ItemTile(host, item);
            }
        }
        finally
        {
            host.ResumeLayout(true);
        }
    }
}
