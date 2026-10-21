using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

public sealed class MatchChartsView : UserControl
{
    private readonly MatchMetricSelection _selection = new();
    private readonly List<(CheckBox Box, MatchMetricCatalog.Metric Metric)> _items = new();
    private readonly List<(CheckBox Box, string[] Keys)> _groups = new();
    private readonly ToolTip _selectionTip = new();
    private readonly MatchComparisonChart _chart;
    private bool _updating;

    public MatchChartsView(MatchInfo match)
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        ForeColor = Theme.Heading;
        var players = (match.AllParticipants ?? new()).OrderBy(p => (int?)p["teamId"] ?? 0).ToArray();
        _chart = new MatchComparisonChart(players, match.CurrPuuid, match.CurrSummonerId) { Dock = DockStyle.Fill };
        var sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 215,
            BackColor = Theme.Canvas,
        };
        sidebar.Controls.Add(CreateMetricChoices(players));
        Controls.Add(_chart);
        Controls.Add(sidebar);
        SelectInitialMetric();
    }

    private FlowLayoutPanel CreateMetricChoices(JObject[] players)
    {
        var choices = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };
        foreach (var category in MatchChartMetricCatalog.Categories)
        {
            var metrics = category
                .Metrics.Where(m => players.Any(p => MatchMetricCatalog.Number(p, m.Key).HasValue))
                .ToArray();
            if (metrics.Length > 0)
            {
                AddMetricChoices(choices, category.Title, metrics);
            }
        }
        return choices;
    }

    private void AddMetricChoices(Control choices, string title, MatchMetricCatalog.Metric[] metrics)
    {
        var group = new CheckBox
        {
            Text = title,
            Width = 190,
            Height = 28,
            AutoCheck = false,
            Margin = new Padding(8, 8, 0, 0),
        };
        var keys = metrics.Select(m => m.Key).ToArray();
        _groups.Add((group, keys));
        choices.Controls.Add(group);
        group.Click += (_, _) => UpdateSelection(keys, !keys.All(_selection.Contains), true);
        foreach (var metric in metrics)
        {
            var box = new CheckBox
            {
                Text = metric.Title,
                Width = 174,
                Height = 26,
                Margin = new Padding(20, 0, 0, 0),
                AutoCheck = false,
            };
            _items.Add((box, metric));
            choices.Controls.Add(box);
            box.Click += (_, _) => UpdateSelection(new[] { metric.Key }, !_selection.Contains(metric.Key));
        }
    }

    private void SelectInitialMetric()
    {
        var first = _items.FirstOrDefault(i => i.Metric.Key == "totalDamageDealtToChampions");
        if (first.Metric != null)
        {
            _selection.Set(new[] { first.Metric.Key }, true);
        }
        else if (_items.Count > 0)
        {
            _selection.Set(new[] { _items[0].Metric.Key }, true);
        }

        RefreshSelection();
    }

    private void UpdateSelection(string[] keys, bool selected, bool group = false)
    {
        if (_updating)
        {
            return;
        }

        if (!(group ? _selection.SetGroup(keys, selected) : _selection.Set(keys, selected)))
        {
            _selectionTip.Show("最多选择 6 个小项，请先取消部分小项", this, 8, 8, 2500);
            return;
        }
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        _updating = true;
        foreach (var (box, metric) in _items)
        {
            box.Checked = _selection.Contains(metric.Key);
            box.Enabled = box.Checked || _selection.Count < MatchMetricSelection.Limit;
        }
        foreach (var (box, keys) in _groups)
        {
            int count = keys.Count(_selection.Contains);
            box.CheckState =
                count == 0 ? CheckState.Unchecked
                : count == keys.Length ? CheckState.Checked
                : CheckState.Indeterminate;
        }
        _selectionTip.Hide(this);
        _chart.SetMetrics(_items.Where(i => _selection.Contains(i.Metric.Key)).Select(i => i.Metric).ToArray());
        _updating = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _selectionTip.Dispose();
        }

        base.Dispose(disposing);
    }
}
