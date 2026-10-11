using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

public sealed class MatchStatisticsView : UserControl
{
    private sealed class StatisticsGrid : DataGridView
    {
        public StatisticsGrid()
        {
            DoubleBuffered = true;
        }
    }

    private readonly StatisticsGrid _grid = new();
    private readonly List<Image> _portraits = new();
    private readonly Dictionary<int, Image> _headers = new();
    private bool _loaded;
    private readonly JObject[] _players;
    private readonly Font _bold = new("Microsoft YaHei UI", 9f, FontStyle.Bold);

    public MatchStatisticsView(MatchInfo match)
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        _players = (match.AllParticipants ?? new()).OrderBy(p => (int?)p["teamId"] ?? 0).ToArray();
        ConfigureGrid();
        CreatePlayerColumns(match);
        foreach (var category in MatchMetricCatalog.Categories)
        {
            AddMetricGroup(category.Title, category.Metrics.Select(m => (m.Title, m.Key)).ToArray());
        }
        _grid.CellClick += (_, e) => ToggleMetricGroup(e.RowIndex);
        _grid.CellPainting += (_, e) => PaintPlayerHeader(e);
        Controls.Add(_grid);
        _grid.SizeChanged += (_, _) => ResizePlayerColumns();
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoGenerateColumns = false;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersHeight = 64;
        _grid.RowTemplate.Height = 28;
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            Font = Font,
            ForeColor = Color.FromArgb(36, 63, 98),
            BackColor = Color.White,
            SelectionBackColor = Color.FromArgb(232, 241, 253),
            SelectionForeColor = Color.FromArgb(36, 63, 98),
            Alignment = DataGridViewContentAlignment.MiddleCenter,
        };
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(244, 247, 251);
    }

    private void CreatePlayerColumns(MatchInfo match)
    {
        _grid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "Metric",
                HeaderText = "统计指标",
                Width = 190,
                Frozen = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleLeft,
                    Padding = new Padding(12, 0, 0, 0),
                },
            }
        );
        for (int i = 0; i < _players.Length; i++)
        {
            var player = _players[i];
            bool self =
                (!string.IsNullOrEmpty(match.CurrPuuid) && (string?)player["puuid"] == match.CurrPuuid)
                || (
                    !string.IsNullOrEmpty(match.CurrSummonerId)
                    && player["summonerId"]?.ToString() == match.CurrSummonerId
                );
            var column = new DataGridViewTextBoxColumn
            {
                Name = $"Player{i}",
                HeaderText = "",
                Width = 84,
                MinimumWidth = 76,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ToolTipText =
                    player["riotIdGameName"]?.ToString() ?? player["summonerName"]?.ToString() ?? $"玩家 {i + 1}",
            };
            if (self)
            {
                column.DefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(232, 241, 253),
                    ForeColor = Color.FromArgb(46, 103, 190),
                    Font = _bold,
                };
            }

            if (i > 0 && !JToken.DeepEquals(player["teamId"], _players[i - 1]["teamId"]))
            {
                _grid.Columns[i].DividerWidth = 6;
            }

            _grid.Columns.Add(column);
        }
    }

    private void AddMetricGroup(string name, (string Title, string Key)[] metrics)
    {
        var available = metrics.Where(m => _players.Any(p => Value(p, m.Key) != "—")).ToArray();
        if (available.Length == 0)
        {
            return;
        }

        int start = _grid.Rows.Add("▾  " + name);
        var group = _grid.Rows[start];
        group.Tag = name;
        group.DefaultCellStyle.BackColor = Color.FromArgb(232, 239, 248);
        group.DefaultCellStyle.Font = _bold;
        foreach (var metric in available)
        {
            _grid.Rows.Add(
                new object[] { metric.Title }
                    .Concat(_players.Select(p => (object)Value(p, metric.Key)))
                    .ToArray()
            );
        }
    }

    private void ToggleMetricGroup(int rowIndex)
    {
        if (rowIndex < 0 || _grid.Rows[rowIndex].Tag is not string name)
        {
            return;
        }

        int next = rowIndex + 1;
        if (next >= _grid.Rows.Count)
        {
            return;
        }

        bool show = !_grid.Rows[next].Visible;
        _grid.CurrentCell = _grid.Rows[rowIndex].Cells[0];
        _grid.SuspendLayout();
        for (int r = next; r < _grid.Rows.Count && _grid.Rows[r].Tag == null; r++)
        {
            _grid.Rows[r].Visible = show;
        }

        _grid.Rows[rowIndex].Cells[0].Value = (show ? "▾  " : "▸  ") + name;
        _grid.ResumeLayout();
    }

    private void PaintPlayerHeader(DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex != -1 || e.ColumnIndex <= 0)
        {
            return;
        }

        e.PaintBackground(e.ClipBounds, false);
        var box = new Rectangle(e.CellBounds.Left + (e.CellBounds.Width - 34) / 2, e.CellBounds.Top + 7, 34, 34);
        if (_headers.TryGetValue(e.ColumnIndex, out var image))
        {
            var state = e.Graphics.Save();
            using var clip = new System.Drawing.Drawing2D.GraphicsPath();
            clip.AddEllipse(box);
            e.Graphics.SetClip(clip);
            e.Graphics.DrawImage(image, box);
            e.Graphics.Restore(state);
        }
        using var pen = new Pen(
            (int?)_players[e.ColumnIndex - 1]["teamId"] == 100
                ? Color.FromArgb(46, 153, 190)
                : Color.FromArgb(191, 81, 91),
            2
        );
        e.Graphics.DrawEllipse(pen, box);
        TextRenderer.DrawText(
            e.Graphics,
            $"{e.ColumnIndex}",
            Font,
            new Rectangle(e.CellBounds.Left, e.CellBounds.Top + 43, e.CellBounds.Width, 18),
            ForeColor,
            TextFormatFlags.HorizontalCenter
        );
        e.Handled = true;
    }

    private void ResizePlayerColumns()
    {
        int width = Math.Max(76, (_grid.ClientSize.Width - 210) / Math.Max(1, _players.Length));
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            if (column.Index > 0)
            {
                column.Width = width;
            }
        }
    }

    public static string Value(JObject player, string key)
    {
        var data = player["stats"] as JObject ?? player;
        if (key == "kda")
        {
            return new[] { "kills", "deaths", "assists" }.All(k => data[k]?.Type == JTokenType.Integer)
                ? $"{data["kills"]}/{data["deaths"]}/{data["assists"]}"
                : "—";
        }

        return data[key]?.Type switch
        {
            JTokenType.Boolean => (bool)data[key]! ? "●" : "○",
            JTokenType.Integer => ((long)data[key]!).ToString("N0"),
            _ => "—",
        };
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        for (int i = 0; i < _players.Length; i++)
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                var image = await FormMain.Globals.resLoading.GetChampionIconAsync(
                    (int?)_players[i]["championId"] ?? 0
                );
                if (IsDisposed)
                {
                    image?.Dispose();
                    return;
                }
                if (image != null)
                {
                    _portraits.Add(image);
                    _headers[i + 1] = image;
                    _grid.InvalidateColumn(i + 1);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Stats portrait: {ex.GetType().Name}");
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var image in _portraits)
            {
                image.Dispose();
            }

            _portraits.Clear();
            _bold.Dispose();
        }
        base.Dispose(disposing);
    }
}
