using League.Models;
using League.UIState;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
namespace League.Controls;
public partial class MatchDetailView
{
        private async Task CreateDetailedPlayerViews(Panel panel, MatchInfo matchInfo, DetailedViewLayoutManager layoutManager)
        {
            if (matchInfo.AllParticipants == null) return;
            var loading = new Label { Text = "正在加载比赛数据…", ForeColor = Color.FromArgb(111, 123, 139), AutoSize = true, Location = new Point(20, 10) };
            panel.Controls.Add(loading);
            _buildingScoreboard = true;
            var sections = new List<Panel>();
            try
            {
                int y = 8;
                foreach (int teamId in new[] { 100, 200 })
                {
                    var team = matchInfo.AllParticipants.Where(p => p["teamId"]?.Value<int>() == teamId).ToList();
                    if (team.Count == 0) continue;
                    var rows = new List<(JObject Player, Panel Row)>();
                    // Build the whole team before displaying it so headers never detach from rows.
                    var section = new BufferedPanel { Location = new Point(12, y), Size = new Size(1120, 64 + team.Count * 44),
                        BackColor = Color.White, Visible = false };
                    panel.Controls.Add(section);
                    sections.Add(section);
                    var win = team.Select(p => p["win"]?.Value<bool>()).FirstOrDefault(v => v.HasValue);
                    Color teamColor = matchInfo.IsRemake ? MatchResult.RemakeColor : win == true ? Color.FromArgb(38, 139, 113) : win == false ? Color.FromArgb(191, 81, 91) : Color.FromArgb(111, 123, 139);
                    int Sum(string key) => team.Sum(p => p[key]?.Value<int>() ?? 0);
                    section.Controls.Add(new Label { Text = $"{(matchInfo.IsRemake ? "重开" : win == true ? "胜方" : win == false ? "败方" : "队伍")}  ·  {(teamId == 100 ? "蓝队" : "红队")}     {Sum("kills")} / {Sum("deaths")} / {Sum("assists")}     总经济 {Sum("goldEarned") / 1000d:0.0}k",
                        Location = new Point(12, 0), Size = new Size(1096, 32), ForeColor = teamColor,
                        Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft } );
                    string? activeSort = null;
                    bool descending = true;
                    void Header(string title, int x, int width, string? key = null)
                    {
                        var button = new Button { Text = title, Location = new Point(x,32), Size = new Size(width,30),
                            FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(111,123,139), BackColor = section.BackColor,
                            TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 9f), TabStop = key != null };
                        button.FlatAppearance.BorderSize=0;
                        section.Controls.Add(button);
                        if (key == null) return;
                        button.Cursor=Cursors.Hand;
                        button.Click += (_,_) =>
                        {
                            if(rows.Count != team.Count) return;
                            descending = activeSort == key ? !descending : true;
                            activeSort=key;
                            double Value(JObject p) => key == "cs" ? (p["totalMinionsKilled"]?.Value<int>() ?? 0)+(p["neutralMinionsKilled"]?.Value<int>() ?? 0)
                                : key == "kda" ? ((p["kills"]?.Value<double>() ?? 0)+(p["assists"]?.Value<double>() ?? 0))/Math.Max(1,p["deaths"]?.Value<double>() ?? 0)
                                : key == "kp" ? p["challenges"]?["killParticipation"]?.Value<double>() ?? 0 : p[key]?.Value<double>() ?? 0;
                            var ordered=descending ? rows.OrderByDescending(r=>Value(r.Player)) : rows.OrderBy(r=>Value(r.Player));
                            int rowY=64;
                            section.SuspendLayout();
                            foreach(var item in ordered) { item.Row.Top=rowY; rowY+=item.Row.Height; }
                            foreach(var header in section.Controls.OfType<Button>())header.Text=header.Text.TrimEnd(' ','↑','↓');
                            button.Text=title+(descending ? " ↓" : " ↑");
                            section.ResumeLayout();
                        };
                    }
                    Header("玩家",58,180); Header("技能 / 装备",242,182); Header("符文 / 海克斯",436,148);
                    Header("KDA",592,90,"kda"); Header("经济",686,90,"goldEarned"); Header("英雄伤害",780,96,"totalDamageDealtToChampions");
                    Header("参团率",880,76,"kp"); Header("补刀",958,64,"cs"); Header("承受伤害",1026,94,"totalDamageTaken");
                    foreach (var participant in team)
                    {
                        if(panel.IsDisposed)return;
                        var row=await layoutManager.CreateDetailedPlayerPanel(participant,matchInfo,0,64+rows.Count*44,teamId);
                        if(panel.IsDisposed) { DisposeImagesInControls(row.Controls); row.Dispose(); return; }
                        section.Controls.Add(row); rows.Add((participant,row));
                        AddPlayerRank(row,participant);
                    }
                    AddTeamBans(section,teamId);
                    y+=section.Height+10;
                }
                if (panel.IsDisposed) return;
                panel.SuspendLayout();
                try
                {
                    _buildingScoreboard = false;
                    AddMatchHighlights(matchInfo);
                    foreach (var section in sections) section.Visible = true;
                    loading.Dispose();
                }
                finally { panel.ResumeLayout(true); }
                ReflowScoreboard();
            }
            catch(Exception ex)
            {
                Debug.WriteLine($"详情加载失败: {ex}");
                foreach (var section in sections)
                {
                    if (section.IsDisposed) continue;
                    DisposeImagesInControls(section.Controls);
                    section.Dispose();
                }
                if(!panel.IsDisposed) { loading.Text="部分数据加载失败，请关闭详情后重试。"; loading.BringToFront(); }
            }
            finally { _buildingScoreboard = false; }
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
