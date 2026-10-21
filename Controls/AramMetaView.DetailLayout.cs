using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

internal sealed partial class AramMetaView
{
    private const int OverviewTab = 0;
    private const int AugmentTab = 1;
    private const int BuildTab = 2;
    private Font? _heroHeadingFont;
    private Font? _sectionHeadingFont;
    private AramMetaPage? _detailData;
    private FlowLayoutPanel? _detailBody;
    private int _detailHero,
        _detailTab,
        _rarity,
        _augmentSort;
    private bool _detailFailed;
    private CancellationToken _detailToken;

    private void MarkDetailUnavailable()
    {
        _detailFailed = true;
        if (_detailTab != AugmentTab)
        {
            RenderDetailBody();
        }
    }

    private void RenderDetail(AramMetaPage page, int id, CancellationToken token, bool equipmentOnly = false)
    {
        _detailData = page;
        _detailToken = token;
        _detailFailed = false;
        if (equipmentOnly && TryRefreshEquipment(id))
        {
            return;
        }
        if (_detailHero != id)
        {
            _detailTab = OverviewTab;
            _rarity = 0;
            _augmentSort = 0;
            _augmentSearch = "";
        }
        _detailHero = id;
        _detail.SuspendLayout();
        try
        {
            foreach (Control control in _detail.Controls.Cast<Control>().ToArray())
            {
                control.Dispose();
            }
            var champion = page.Index.Champions.FirstOrDefault(c => c.Id == id);
            if (champion == null)
            {
                _detailBody = null;
                Section("暂无英雄数据");
                return;
            }
            RenderChampionSummary(page, champion, token);
            RenderDetailNavigation();
            _detailBody = Section("");
            _detailBody.Controls[0].Dispose();
            RenderDetailBody();
            ResizeSections();
        }
        finally
        {
            _detail.ResumeLayout(true);
        }
    }

    private bool TryRefreshEquipment(int id)
    {
        if (_detailHero != id || _detailBody is not { IsDisposed: false })
        {
            return false;
        }
        if (_detailTab == OverviewTab && _overviewEquipment is { IsDisposed: false })
        {
            RenderOverviewEquipment();
        }
        else if (_detailTab == BuildTab)
        {
            RenderDetailBody();
        }
        // Augment comparison already uses the complete index; retain its filters and selected explanation.
        return true;
    }

    private void RenderChampionSummary(AramMetaPage page, AramMetaChampion champion, CancellationToken token)
    {
        var summary = Section(Name(champion));
        summary.BackColor = Color.FromArgb(226, 242, 243);
        summary.Controls[0].Font = _heroHeadingFont ??= new Font(Font.FontFamily, 14, FontStyle.Bold);
        var header = Row(summary);
        Icon(header, (string?)champion.Data["image"], Name(champion), token, 48, "champion", champion.Id);
        var tierInk = TierColor(champion.Tier);
        header.Controls.Add(
            new Label
            {
                Text = champion.Tier,
                Size = new Size(44, 30),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = tierInk,
                BackColor = Color.FromArgb(24, tierInk),
                Margin = new Padding(4, 17, 8, 0),
            }
        );
        AddChampionMetrics(header, page, champion);
        _tips.SetToolTip(
            summary,
            "调整后胜率来自 arammeta 的统计模型；样本选取率按完整 5v5 样本计算，仅代表来源样本。"
        );
    }

    private void AddChampionMetrics(Control header, AramMetaPage page, AramMetaChampion champion)
    {
        foreach (
            var (label, value) in new[]
            {
                ("调整后胜率", Rate(champion.WinRate)),
                ("原始胜率", Rate(champion.RawWinRate)),
                ("样本选取率", Rate(page.Index.PickRate(champion))),
                ("样本量", champion.Games?.ToString("N0") ?? "—"),
            }
        )
        {
            header.Controls.Add(
                new Label
                {
                    Text = value + "\n" + label,
                    AutoSize = false,
                    Size = new Size(116, 46),
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = label == "调整后胜率" ? Theme.Win : Theme.Muted,
                    Margin = new Padding(4),
                }
            );
        }
    }

    private void RenderDetailNavigation()
    {
        var navigation = Section("");
        navigation.Controls[0].Dispose();
        var tabs = Row(navigation);
        var names = new[] { "概览", "强化比较", "出装与技能" };
        for (int i = 0; i < names.Length; i++)
        {
            int selected = i;
            var button = new Button
            {
                Text = names[i],
                Width = 112,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 0, 8, 0),
            };
            tabs.Controls.Add(button);
            button.Click += (_, _) =>
            {
                _detailTab = selected;
                UpdateDetailTabs(tabs);
                RenderDetailBody();
            };
        }
        UpdateDetailTabs(tabs);
    }

    private void UpdateDetailTabs(Control tabs)
    {
        for (int i = 0; i < tabs.Controls.Count; i++)
        {
            tabs.Controls[i].BackColor = i == _detailTab ? Theme.Accent : Color.White;
            tabs.Controls[i].ForeColor = i == _detailTab ? Color.White : Color.FromArgb(60, 82, 99);
        }
    }

    private void RenderDetailBody()
    {
        if (_detailBody == null || _detailBody.IsDisposed || _detailData == null)
        {
            return;
        }

        _detailBody.SuspendLayout();
        try
        {
            foreach (Control control in _detailBody.Controls.Cast<Control>().ToArray())
            {
                control.Dispose();
            }

            if (_detailTab == OverviewTab)
            {
                RenderOverview();
            }
            else if (_detailTab == AugmentTab)
            {
                RenderAugmentComparison();
            }
            else
            {
                RenderBuildComparison();
            }

            ResizeSections();
        }
        finally
        {
            _detailBody.ResumeLayout(true);
        }
    }

    private TableLayoutPanel AddStatsRow(
        Control host,
        Control? icon,
        string name,
        string win,
        string pick,
        string games,
        bool heading = false
    )
    {
        var row = new TableLayoutPanel
        {
            Height = heading ? 34 : 54,
            Width = Math.Max(510, host.Width - 24),
            MinimumSize = new Size(510, 0),
            ColumnCount = 5,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 3),
            BackColor = heading ? Color.FromArgb(235, 242, 247) : Theme.SurfaceSubtle,
        };
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, heading ? 34 : 54));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        }

        row.Controls.Add(icon ?? new Panel(), 0, 0);
        int column = 1;
        foreach (var text in new[] { name, win, pick, games })
        {
            row.Controls.Add(
                new Label
                {
                    Text = text,
                    Dock = DockStyle.Fill,
                    AutoEllipsis = true,
                    TextAlign = column == 1 ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleCenter,
                    ForeColor = column == 2 && !heading ? Theme.Win : Theme.Ink,
                },
                column++,
                0
            );
        }

        host.Controls.Add(row);
        return row;
    }
}
