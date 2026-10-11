using System.Diagnostics;
using League.Models;
using League.UIState;
using Newtonsoft.Json.Linq;

namespace League.Controls;

public partial class MatchDetailView
{
    private sealed class ScoreboardTeam
    {
        public required Panel Section { get; init; }
        public required List<JObject> Players { get; init; }
        public List<(JObject Player, Panel Row)> Rows { get; } = new();
        public string? SortKey { get; set; }
        public bool Descending { get; set; } = true;
    }

    private async Task CreateDetailedPlayerViews(
        Panel panel,
        MatchInfo matchInfo,
        DetailedViewLayoutManager layoutManager
    )
    {
        if (matchInfo.AllParticipants == null)
        {
            return;
        }
        var loading = new Label
        {
            Text = "正在加载比赛数据…",
            ForeColor = Color.FromArgb(111, 123, 139),
            AutoSize = true,
            Location = new Point(20, 10),
        };
        panel.Controls.Add(loading);
        _buildingScoreboard = true;
        var sections = new List<Panel>();
        try
        {
            int y = 8;
            foreach (int teamId in new[] { 100, 200 })
            {
                var team = CreateScoreboardTeam(panel, matchInfo, teamId, y);
                if (team == null)
                {
                    continue;
                }
                sections.Add(team.Section);
                if (!await BuildTeamRowsAsync(panel, team, teamId, matchInfo, layoutManager))
                {
                    return;
                }
                AddTeamBans(team.Section, teamId);
                y += team.Section.Height + 10;
            }
            if (!panel.IsDisposed)
            {
                ShowScoreboardTeams(panel, matchInfo, sections, loading);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"详情加载失败: {ex}");
            ShowScoreboardFailure(panel, sections, loading);
        }
        finally
        {
            _buildingScoreboard = false;
        }
    }

    private ScoreboardTeam? CreateScoreboardTeam(Panel panel, MatchInfo match, int teamId, int y)
    {
        var players = match.AllParticipants.Where(p => p["teamId"]?.Value<int>() == teamId).ToList();
        if (players.Count == 0)
        {
            return null;
        }
        // Build the whole team before displaying it so headers never detach from rows.
        var team = new ScoreboardTeam
        {
            Players = players,
            Section = new BufferedPanel
            {
                Location = new Point(12, y),
                Size = new Size(1120, 64 + players.Count * 44),
                BackColor = Color.White,
                Visible = false,
            },
        };
        panel.Controls.Add(team.Section);
        AddTeamSummary(team, match, teamId);
        AddScoreboardHeaders(team);
        return team;
    }

    private void AddTeamSummary(ScoreboardTeam team, MatchInfo match, int teamId)
    {
        var win = team.Players.Select(p => p["win"]?.Value<bool>()).FirstOrDefault(v => v.HasValue);
        Color teamColor =
            match.IsRemake ? MatchResult.RemakeColor
            : win == true ? Color.FromArgb(38, 139, 113)
            : win == false ? Color.FromArgb(191, 81, 91)
            : Color.FromArgb(111, 123, 139);
        var title =
            $"{(match.IsRemake ? "重开" : win == true ? "胜方" : win == false ? "败方" : "队伍")} · {(teamId == 100 ? "蓝队" : "红队")}";
        team.Section.Controls.Add(
            new TeamSummaryHeader(title, teamColor, TeamSummary.From(match, teamId), _toolTip)
            {
                Location = new Point(12, 0),
                Width = 1096,
            }
        );
    }

    private void AddScoreboardHeaders(ScoreboardTeam team)
    {
        foreach (
            var (title, x, width, key) in new (string, int, int, string?)[]
            {
                ("玩家", 58, 180, null),
                ("技能 / 装备", 242, 182, null),
                ("符文 / 海克斯", 436, 148, null),
                ("KDA", 592, 90, "kda"),
                ("经济", 686, 90, "goldEarned"),
                ("英雄伤害", 780, 96, "totalDamageDealtToChampions"),
                ("参团率", 880, 76, "kp"),
                ("补刀", 958, 64, "cs"),
                ("承受伤害", 1026, 94, "totalDamageTaken"),
            }
        )
        {
            var button = new Button
            {
                Text = title,
                Location = new Point(x, 32),
                Size = new Size(width, 30),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(111, 123, 139),
                BackColor = team.Section.BackColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9f),
                TabStop = key != null,
            };
            button.FlatAppearance.BorderSize = 0;
            team.Section.Controls.Add(button);
            if (key != null)
            {
                button.Cursor = Cursors.Hand;
                button.Click += (_, _) => SortTeamRows(team, button, title, key);
            }
        }
    }

    private static double ScoreboardSortValue(JObject player, string key) =>
        key switch
        {
            "cs" => (player["totalMinionsKilled"]?.Value<int>() ?? 0)
                + (player["neutralMinionsKilled"]?.Value<int>() ?? 0),
            "kda" => ((player["kills"]?.Value<double>() ?? 0) + (player["assists"]?.Value<double>() ?? 0))
                / Math.Max(1, player["deaths"]?.Value<double>() ?? 0),
            "kp" => player["challenges"]?["killParticipation"]?.Value<double>() ?? 0,
            _ => player[key]?.Value<double>() ?? 0,
        };

    private static void SortTeamRows(ScoreboardTeam team, Button button, string title, string key)
    {
        if (team.Rows.Count != team.Players.Count)
        {
            return;
        }
        team.Descending = team.SortKey == key ? !team.Descending : true;
        team.SortKey = key;
        var ordered = team.Descending
            ? team.Rows.OrderByDescending(r => ScoreboardSortValue(r.Player, key))
            : team.Rows.OrderBy(r => ScoreboardSortValue(r.Player, key));
        int rowY = 64;
        team.Section.SuspendLayout();
        try
        {
            foreach (var item in ordered)
            {
                item.Row.Top = rowY;
                rowY += item.Row.Height;
            }
            foreach (var header in team.Section.Controls.OfType<Button>())
            {
                header.Text = header.Text.TrimEnd(' ', '↑', '↓');
            }
            button.Text = title + (team.Descending ? " ↓" : " ↑");
        }
        finally
        {
            team.Section.ResumeLayout();
        }
    }

    private async Task<bool> BuildTeamRowsAsync(
        Panel panel,
        ScoreboardTeam team,
        int teamId,
        MatchInfo match,
        DetailedViewLayoutManager layout
    )
    {
        foreach (var participant in team.Players)
        {
            if (panel.IsDisposed)
            {
                return false;
            }
            var row = await layout.CreateDetailedPlayerPanel(participant, match, 0, 64 + team.Rows.Count * 44, teamId);
            if (panel.IsDisposed)
            {
                DisposeImagesInControls(row.Controls);
                row.Dispose();
                return false;
            }
            team.Section.Controls.Add(row);
            team.Rows.Add((participant, row));
            AddPlayerRank(row, participant);
        }
        return true;
    }

    private void ShowScoreboardTeams(Panel panel, MatchInfo match, List<Panel> sections, Label loading)
    {
        panel.SuspendLayout();
        try
        {
            _buildingScoreboard = false;
            AddMatchHighlights(match);
            foreach (var section in sections)
            {
                section.Visible = true;
            }
            loading.Dispose();
        }
        finally
        {
            panel.ResumeLayout(true);
        }
        ReflowScoreboard();
    }

    private void ShowScoreboardFailure(Panel panel, List<Panel> sections, Label loading)
    {
        foreach (var section in sections)
        {
            if (section.IsDisposed)
            {
                continue;
            }
            DisposeImagesInControls(section.Controls);
            section.Dispose();
        }
        if (!panel.IsDisposed)
        {
            loading.Text = "部分数据加载失败，请关闭详情后重试。";
            loading.BringToFront();
        }
    }

    private void DisposeImagesInControls(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            if (control is PictureBox pictureBox && pictureBox.Image != null)
            {
                try
                {
                    pictureBox.Image.Dispose();
                    pictureBox.Image = null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("释放图片资源失败: " + ex.Message);
                }
            }
            if (control.HasChildren)
            {
                DisposeImagesInControls(control.Controls);
            }
        }
    }
}
