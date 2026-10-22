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
    private Font? _summaryFigureFont;
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
        // The name sits beside the portrait above the figures instead of on a line of its own.
        var title = summary.Controls[0];
        title.Font = _heroHeadingFont ??= new Font(Font.FontFamily, 14, FontStyle.Bold);
        title.Margin = Padding.Empty;
        var header = Row(summary);
        var portrait = Icon(
            header,
            (string?)champion.Data["image"],
            Name(champion),
            token,
            72,
            "champion",
            champion.Id
        );
        portrait.Margin = new Padding(0, 0, 16, 0);
        var identity = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        header.Controls.Add(identity);
        identity.Controls.Add(title);
        var metrics = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            Margin = Padding.Empty,
        };
        identity.Controls.Add(metrics);
        AddChampionMetrics(metrics, page, champion);
        _tips.SetToolTip(
            summary,
            "调整后胜率来自 arammeta 的统计模型；样本选取率按完整 5v5 样本计算，仅代表来源样本。"
        );
    }

    // The same headline figures as the OP.GG guide: the adjusted win rate split green and red at 50%, the tier in its
    // own colour, the rest in the heading ink.
    private void AddChampionMetrics(Control header, AramMetaPage page, AramMetaChampion champion)
    {
        var winColor =
            champion.WinRate is not double rate ? Theme.Heading
            : rate >= 0.5 ? Theme.Win
            : Theme.Loss;
        var figure = _summaryFigureFont ??= Theme.NumberFont(18, strong: true);
        foreach (
            var (caption, value, color) in new[]
            {
                ("调整后胜率", Rate(champion.WinRate), winColor),
                ("原始胜率", Rate(champion.RawWinRate), Theme.Heading),
                ("梯队", champion.Tier, TierColor(champion.Tier)),
                ("样本选取率", Rate(page.Index.PickRate(champion)), Theme.Heading),
                ("样本量", champion.Games?.ToString("N0") ?? "—", Theme.Heading),
            }
        )
        {
            header.Controls.Add(SummaryMetric.Create(caption, value, color, figure));
        }
    }

    private void RenderDetailNavigation()
    {
        var navigation = Section("");
        navigation.Controls[0].Dispose();
        // The tabs sit on the page itself rather than in a card of their own, which cost a card's padding and gap.
        navigation.BackColor = Theme.Canvas;
        navigation.Padding = Padding.Empty;
        navigation.Margin = new Padding(0, 0, 0, 8);
        var tabs = Row(navigation);
        tabs.Margin = Padding.Empty;
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
            StyleChoice((Button)tabs.Controls[i], i == _detailTab);
        }
    }

    // Tabs and filters read like the OP.GG lane buttons: the chosen one solid, the rest a quiet fill without a border.
    private static void StyleChoice(Button button, bool chosen)
    {
        if (chosen)
        {
            ButtonStyles.Primary(button);
        }
        else
        {
            ButtonStyles.Secondary(button);
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
}
