using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

public partial class MatchDetailView
{
    private Control CreateMatchHeader(MatchInfo match, params Button[] actions)
    {
        var header = new BufferedPanel
        {
            Name = "MatchHeader",
            Dock = DockStyle.Top,
            Height = 76,
            BackColor = BackColor,
            Font = new Font("Microsoft YaHei UI", 9f),
        };
        var info = new TableLayoutPanel
        {
            ColumnCount = 5,
            RowCount = 1,
            Margin = Padding.Empty,
        };
        foreach (float width in new[] { 23f, 24f, 21f, 19f, 13f })
        {
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        }

        info.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        string Kills(int team)
        {
            var players = match.AllParticipants?.Where(p => (int?)p["teamId"] == team).ToArray();
            return players?.Length > 0 && players.All(p => p["kills"]?.Type == JTokenType.Integer)
                ? players.Sum(p => (int)p["kills"]!).ToString()
                : "—";
        }
        string time = DateTime.TryParse(match.GameTime, out var date) ? date.ToString("MM-dd HH:mm") : match.GameTime;
        string mapName = MatchMap.GetName(match.RawGameData);
        var values = new[]
        {
            ("时间", time),
            ("类型", match.Mode),
            ("地图", mapName),
            ("比赛时长", match.DurationText),
            ("击杀", $"{Kills(100)} / {Kills(200)}"),
        };
        for (int i = 0; i < values.Length; i++)
        {
            var cell = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
            var value = new Label
            {
                Text = values[i].Item2,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(36, 63, 98),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            cell.Controls.Add(value);
            cell.Controls.Add(
                new Label
                {
                    Text = values[i].Item1,
                    Dock = DockStyle.Top,
                    Height = 22,
                    ForeColor = Color.FromArgb(111, 123, 139),
                }
            );
            _toolTip.SetToolTip(
                value,
                i == 0 ? $"{match.GameTime}\n比赛 ID：{match.GameId}"
                    : i == 4 ? "蓝队击杀 / 红队击杀"
                    : i == 2 && mapName == "随机地图" ? "比赛未提供地图变体字段，无法确认本场具体桥梁。"
                    : values[i].Item2
            );
            info.Controls.Add(cell, i, 0);
        }
        var buttons = new FlowLayoutPanel
        {
            Width = actions.Sum(action => action.Width + 6),
            Height = 38,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        foreach (var action in actions)
        {
            action.Margin = new Padding(0, 0, 6, 0);
            action.Font = header.Font;
            buttons.Controls.Add(action);
        }
        actions[0].FlatAppearance.BorderSize = 0;
        actions[0].ForeColor = Color.FromArgb(46, 103, 190);
        _toolTip.SetToolTip(actions[0], $"查看比赛 {match.GameId} 的回放");
        header.Controls.Add(info);
        header.Controls.Add(buttons);
        void Arrange()
        {
            bool narrow = header.Width < buttons.Width + 620;
            header.Height = narrow ? 112 : 76;
            info.SetBounds(12, 10, Math.Max(1, header.Width - (narrow ? 24 : buttons.Width + 30)), 52);
            buttons.Location = new Point(Math.Max(12, header.Width - buttons.Width - 12), narrow ? 70 : 24);
        }
        header.SizeChanged += (_, _) => Arrange();
        Arrange();
        return header;
    }
}
