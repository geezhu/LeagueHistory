using League.Clients;
using League.Controls;
using League.Models;
using League.UIState;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using static League.FormMain;

namespace League.Managers
{
    /// <summary>
    /// 管理比赛详情显示和回放功能
    /// </summary>
    public class MatchDetailManager
    {
        private readonly FormMain _form;
        private ToolTip? _toolTip;

        public MatchDetailManager(FormMain form)
        {
            _form = form;
        }

        #region 比赛详情显示
        /// <summary>
        /// 显示比赛详情
        /// </summary>
        public void ShowMatchDetails(MatchInfo matchInfo)
        {
            try
            {
                Debug.WriteLine("显示比赛详情: " + matchInfo.SelfPlayer?.GameName + " - " + matchInfo.GameTime);

                if (!ValidateMatchData(matchInfo))
                    return;

                var detailForm = CreateDetailForm(matchInfo);
                InitializeDetailForm(detailForm, matchInfo);
                detailForm.ShowDialog();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("显示详情失败: " + ex.Message + "\n" + ex.StackTrace);
                MessageBox.Show("显示详情失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 验证比赛数据
        /// </summary>
        private bool ValidateMatchData(MatchInfo matchInfo)
        {
            if (matchInfo?.AllParticipants == null || matchInfo.AllParticipants.Count == 0)
            {
                MessageBox.Show("比赛数据不完整，无法显示详情");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 创建详情窗体
        /// </summary>
        private Form CreateDetailForm(MatchInfo matchInfo)
        {
            string championName = matchInfo.ChampionName ?? "未知英雄";
            string gameTime = matchInfo.GameTime ?? "未知时间";

            return new Form
            {
                Text = $"比赛详情 - {championName} - {gameTime}",
                Size = new Size(1180, 760),
                MinimumSize = new Size(1000, 650),
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = Color.White,
                MinimizeBox = false,
                MaximizeBox = true,
                ShowIcon = true,
                ShowInTaskbar = true
            };
        }

        /// <summary>
        /// 初始化详情窗体
        /// </summary>
        private void InitializeDetailForm(Form detailForm, MatchInfo matchInfo)
        {
            _toolTip = new ToolTip
            {
                AutoPopDelay = 30000,
                InitialDelay = 100,
                ReshowDelay = 50,
                ShowAlways = true,
                UseAnimation = true,
                UseFading = true
            };

            var layoutManager = new DetailedViewLayoutManager(_toolTip);
            detailForm.BackColor = Color.FromArgb(244, 247, 251);
            var summary = new Label
            {
                Dock = DockStyle.Top, Height = 72, Padding = new Padding(22, 12, 0, 0),
                Font = new Font("Microsoft YaHei UI", 11f), ForeColor = Color.FromArgb(36, 48, 65),
                Text = $"{matchInfo.Mode}     ·     {matchInfo.DurationText}     ·     {matchInfo.GameTime}\n比赛 {matchInfo.GameId}    |    基础数据 · 点击列标题排序，悬停查看完整数据"
            };
            Panel panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = detailForm.BackColor };
            detailForm.Controls.Add(panel);
            detailForm.Controls.Add(summary);
            CreateDetailedPlayerViews(panel, matchInfo, layoutManager);
            detailForm.FormClosed += (s, e) => CleanupDetailForm(detailForm);
        }

        /// <summary>
        /// 创建Tab控件
        /// </summary>
        private TabControl CreateTabControl()
        {
            return new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("微软雅黑", 9f),
                ItemSize = new Size(80, 25),
                Padding = new Point(10, 5)
            };
        }

        /// <summary>
        /// 清理详情窗体资源
        /// </summary>
        private void CleanupDetailForm(Form detailForm)
        {
            try
            {
                _toolTip?.RemoveAll();
                _toolTip?.Dispose();
                DisposeImagesInControls(detailForm.Controls);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("释放资源时出错: " + ex.Message);
            }
        }
        #endregion

        #region 玩家视图创建
        /// <summary>
        /// 创建详细玩家视图
        /// </summary>
        private async void CreateDetailedPlayerViews(Panel panel, MatchInfo matchInfo, DetailedViewLayoutManager layoutManager)
        {
            if (matchInfo.AllParticipants == null) return;
            var loading = new Label { Text = "正在加载比赛数据…", ForeColor = Color.FromArgb(111, 123, 139), AutoSize = true, Location = new Point(20, 10) };
            panel.Controls.Add(loading);
            try
            {
                int y = 8;
                foreach (int teamId in new[] { 100, 200 })
                {
                    var team = matchInfo.AllParticipants.Where(p => p["teamId"]?.Value<int>() == teamId).ToList();
                    if (team.Count == 0) continue;
                    var rows = new List<(JObject Player, Panel Row)>();
                    // Build the whole team before displaying it so headers never detach from rows.
                    var section = new Panel { Location = new Point(12, y), Size = new Size(1120, 64 + team.Count * 44),
                        BackColor = Color.White };
                    panel.Controls.Add(section);
                    var win = team.Select(p => p["win"]?.Value<bool>()).FirstOrDefault(v => v.HasValue);
                    Color teamColor = win == true ? Color.FromArgb(38, 139, 113) : win == false ? Color.FromArgb(191, 81, 91) : Color.FromArgb(111, 123, 139);
                    int Sum(string key) => team.Sum(p => p[key]?.Value<int>() ?? 0);
                    section.Controls.Add(new Label { Text = $"{(win == true ? "胜方" : win == false ? "败方" : "队伍")}  ·  {(teamId == 100 ? "蓝队" : "红队")}     {Sum("kills")} / {Sum("deaths")} / {Sum("assists")}     总经济 {Sum("goldEarned") / 1000d:0.0}k",
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
                            foreach(var item in ordered) { item.Row.Top=rowY; rowY+=44; }
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
                    }
                    y+=section.Height+10;
                }
                loading.Dispose();
            }
            catch(Exception ex)
            {
                Debug.WriteLine($"详情加载失败: {ex}");
                if(!panel.IsDisposed) { loading.Text="部分数据加载失败，请关闭详情后重试。"; loading.BringToFront(); }
            }
        }
        /// <summary>
        /// 添加队伍标签
        /// </summary>
        private void AddTeamLabel(Panel panel, string text, Color color, ref int y)
        {
            var label = new Label
            {
                Text = text,
                Location = new Point(10, y),
                Size = new Size(200, 25),
                Font = new Font("微软雅黑", 11f, FontStyle.Bold),
                ForeColor = color
            };
            panel.Controls.Add(label);
            y += 30;
        }
        #endregion

        #region 回放功能
        /// <summary>
        /// 处理回放点击事件
        /// </summary>
        public async Task HandleReplayClick(MatchInfo matchInfo, MatchListPanel matchPanel)
        {
            try
            {
                if (!IsReplaySupported(matchInfo.QueueId))
                {
                    // 仅此一种必要提示可保留，也可改成静默 return
                    MessageBox.Show("该模式不支持查看回放！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Question);
                    return;
                }

                if (!matchInfo.IsReplayDownloaded)
                {
                    await DownloadAndPlayReplay(matchInfo, matchPanel);
                }
                else
                {
                    await PlayExistingReplay(matchInfo);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[回放] 处理异常: {ex}");
                // 不再弹错误框，避免多余提示
            }
        }

        /// <summary>
        /// 下载并播放回放（只弹一次确认框）
        /// </summary>
        private async Task DownloadAndPlayReplay(MatchInfo matchInfo, MatchListPanel matchPanel)
        {
            // ========== 唯一弹窗 ==========
            var result = MessageBox.Show(
                "确定下载并观看这场回放吗？\n\n" +
                $"比赛ID：{matchInfo.GameId}\n" +
                $"模式：{matchInfo.Mode}\n\n" +
                "注意：确定后会立即下载并播放，且只有当前版本的比赛可以下载，版本更新后旧回放会失效。",
                "查看回放",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);

            if (result != DialogResult.OK)
                return;

            Cursor previousCursor = Cursor.Current;
            try
            {
                Cursor.Current = Cursors.WaitCursor;

                var downloadResult = await Globals.lcuClient.DownloadReplayAsync(matchInfo.GameId);

                switch (downloadResult)
                {
                    case ReplayDownloadResult.Success:
                    case ReplayDownloadResult.AlreadyExists:
                        matchInfo.IsReplayDownloaded = true;
                        matchPanel.Invalidate();

                        // 下载成功直接进回放，不再弹成功提示
                        await Globals.lcuClient.PlayReplayAsync(matchInfo.GameId);
                        break;

                    case ReplayDownloadResult.Incompatible:
                        matchInfo.IsReplayExpired = true;
                        matchPanel.SetReplayStatus(matchInfo, "回放已过期");
                        Debug.WriteLine($"[回放] GameId {matchInfo.GameId} 版本不兼容，回放已过期");
                        break;

                    case ReplayDownloadResult.Timeout:
                    case ReplayDownloadResult.Failed:
                    case ReplayDownloadResult.RequestError:
                    default:
                        Debug.WriteLine($"[回放] GameId {matchInfo.GameId} 下载失败: {downloadResult}");
                        // 可选：UpdateReplayButtonStatus(matchPanel, matchInfo, "下载失败");
                        break;
                }
            }
            finally
            {
                Cursor.Current = previousCursor;
            }
        }

        /// <summary>
        /// 播放已存在的回放（无弹窗）
        /// </summary>
        private async Task PlayExistingReplay(MatchInfo matchInfo)
        {
            Cursor previousCursor = Cursor.Current;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                await Globals.lcuClient.PlayReplayAsync(matchInfo.GameId);
            }
            finally
            {
                Cursor.Current = previousCursor;
            }
        }

        private bool IsReplaySupported(string queueTag)
        {
            return !string.IsNullOrEmpty(queueTag);
        }
        #endregion

        #region 工具方法
        /// <summary>
        /// 释放控件中的图片资源
        /// </summary>
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

        /// <summary>
        /// 检查是否支持显示详细对局信息
        /// 只允许标准 5v5（固定10人）的峡谷/大乱斗系模式
        /// </summary>
        public bool IsSummonersRiftOrAram(string queueTag)
        {
            if (string.IsNullOrEmpty(queueTag)) return false;

            string[] allowed = new[]
            {
                // 排位
                "q_420", "q_440",

                // 匹配
                "q_400", "q_430",

                // 大乱斗 & 无限
                "q_450", "q_900", "q_1900",

                // 快速模式
                "q_480",

                // 海克斯乱斗
                "q_2400", "q_2450", "q_3270",

                // 人机（完整）
                "q_830", "q_840", "q_850",
                "q_870", "q_880", "q_890",

                // 末日人机（已确认是5v5峡谷）
                "q_950", "q_960",

                // 其他标准5v5
                "q_1020",  // 克隆大作战
                "q_1300",  // 极限闪击
                "q_1400",  // 终极魔典

                // 经典 / 训练
                "q_4310", "q_4320",
                "q_3140", "q_3100"
            };

            return allowed.Contains(queueTag);
        }
        #endregion
    }
}