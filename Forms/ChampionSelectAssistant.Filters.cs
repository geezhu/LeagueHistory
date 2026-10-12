using League.Models;

namespace League;

internal sealed partial class ChampionSelectAssistant
{
    private sealed record FilterChoice(string Text, string Value)
    {
        public override string ToString() => Text;
    }

    private static ComboBox Filter(int width, params string[] choices)
    {
        var box = new Controls.DarkFilterComboBox
        {
            Width = Dip(width),
            DropDownWidth = Dip(170),
            Margin = DipPadding(2),
        };
        box.Items.AddRange(
            choices
                .Select(c =>
                {
                    var parts = c.Split('|');
                    return new FilterChoice(parts[0], parts[1]);
                })
                .ToArray()
        );
        box.SelectedIndex = 0;
        return box;
    }

    private readonly ComboBox _mode = Filter(
        112,
        "跟随模式|auto",
        "召唤师峡谷|ranked",
        "极地大乱斗|aram",
        "海克斯大乱斗|mayhem",
        "斗魂竞技场|arena",
        "无限火力|urf",
        "极限闪击|nexus_blitz"
    );
    private readonly ComboBox _region = Filter(70, "全球|global", "韩服|kr", "北美|na", "西欧|euw");
    private readonly ComboBox _tier = Filter(
        104,
        "翡翠及以上|emerald_plus",
        "全部段位|all",
        "黄金及以下|ibsg",
        "黄金及以上|gold_plus",
        "铂金及以上|platinum_plus",
        "钻石及以上|diamond_plus",
        "大师|master",
        "大师及以上|master_plus",
        "宗师|grandmaster",
        "王者|challenger"
    );
    private string _filterMode = "ranked";
    private readonly Button _filterSummary = new() { Name = "assistantFilterSummary" };
    private readonly FlowLayoutPanel _filterPanel = new();
    private bool _filtersExpanded;
    private readonly Dictionary<string, string> _selectedTiers = new();

    private static string FilterValue(ComboBox box) => ((FilterChoice)box.SelectedItem!).Value;

    private string SelectedMode =>
        FilterValue(_mode) == "auto"
            ? (_queue == 2400 ? "mayhem" : OpggQuery.ModeForFollowing(_queue))
            : FilterValue(_mode);
    private OpggQuery SelectedQuery =>
        new OpggQuery(
            FilterValue(_region),
            SelectedMode,
            FilterValue(_tier),
            new string?[] { null, "top", "jungle", "mid", "adc", "support" }[_lane.SelectedIndex] ?? _position ?? "mid",
            _hero
        ).Normalize();

    private string FilterSummary(string mode)
    {
        var parts = new List<string>
        {
            mode switch
            {
                "ranked" => "峡谷",
                "aram" => "大乱斗",
                "mayhem" => "海克斯大乱斗",
                "arena" => "斗魂竞技场",
                "urf" => "无限火力",
                _ => "极限闪击",
            },
        };
        if (mode != "mayhem")
        {
            parts.Add(_region.Text);
        }

        if (mode is not ("arena" or "mayhem"))
        {
            parts.Add(_tier.Text.Replace("及以上", "+").Replace("及以下", "-"));
        }

        if (mode == "ranked")
        {
            parts.Add(_lane.SelectedIndex == 0 ? "自动分路" : _lane.Text);
        }

        return string.Join(" · ", parts);
    }

    private void SyncFilters()
    {
        string mode = SelectedMode;
        _syncing = true;
        try
        {
            if (mode != _filterMode)
            {
                _selectedTiers[_filterMode] = FilterValue(_tier);
                string tier = _selectedTiers.GetValueOrDefault(mode, mode == "ranked" ? "emerald_plus" : "all");
                _tier.SelectedItem = _tier.Items.Cast<FilterChoice>().First(c => c.Value == tier);
                _filterMode = mode;
            }
            _lane.Enabled = mode == "ranked";
            _tier.Enabled = mode is not ("arena" or "mayhem");
            _region.Enabled = mode != "mayhem";
            _region.Visible = mode != "mayhem";
            _tier.Visible = mode is not ("arena" or "mayhem");
            _lane.Visible = mode == "ranked";
            _apply.Visible = true;
            _filterSummary.Text = FilterSummary(mode) + (_filtersExpanded ? "  ▴" : "  ▾");
            _filterPanel.Visible = _filtersExpanded;
            if (_filterPanel.Parent is Panel toolbar)
            {
                // Preferred size follows each filter's own visibility, so it is valid while hidden.
                int width = Dip(LayoutWidth) - toolbar.Padding.Horizontal;
                int filters = _filtersExpanded ? _filterPanel.GetPreferredSize(new Size(width, 0)).Height : 0;
                toolbar.Height = Dip(ToolbarHeaderHeight) + filters + toolbar.Padding.Vertical;
            }
        }
        finally
        {
            _syncing = false;
        }
    }
}
