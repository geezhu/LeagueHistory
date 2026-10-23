using System.Diagnostics;
using League.Clients;
using League.Controls;
using League.Models;
using League.UIState;
using Newtonsoft.Json.Linq;
using static League.FormMain;

namespace League.Managers
{
    /// <summary>
    /// 管理比赛详情显示和回放功能
    /// </summary>
    public class MatchDetailManager
    {
        private readonly FormMain _form;

        public MatchDetailManager(FormMain form)
        {
            _form = form;
        }

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
                    ShowReplayNotSupportedMessage();
                    return;
                }

                // The card no longer offers an expired replay; the detail view's button can still ask for it.
                if (matchInfo.IsReplayExpired)
                {
                    ShowReplayExpiredMessage();
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
                ShowReplayError(ex.Message);
            }
        }

        /// <summary>
        /// 下载并播放回放
        /// </summary>
        private async Task DownloadAndPlayReplay(MatchInfo matchInfo, MatchListPanel matchPanel)
        {
            var result = MessageBox.Show(
                "确定下载并观看这场回放吗？\n\n"
                    + $"比赛ID：{matchInfo.GameId}\n"
                    + $"模式：{matchInfo.Mode}\n\n"
                    + "注意：确定后会立即下载并播放，且只有当前版本的比赛可以下载，版本更新后旧回放会失效。",
                "查看回放",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question
            );

            if (result != DialogResult.OK)
            {
                return;
            }

            switch (await Globals.lcuClient.DownloadReplayAsync(matchInfo.GameId))
            {
                case ReplayDownloadResult.Success:
                case ReplayDownloadResult.AlreadyExists:
                    matchInfo.IsReplayDownloaded = true;
                    matchPanel.Invalidate();
                    if (!await Globals.lcuClient.PlayReplayAsync(matchInfo.GameId))
                    {
                        ShowReplayPlayError();
                    }

                    break;
                case ReplayDownloadResult.Incompatible:
                    matchInfo.IsReplayExpired = true;
                    matchPanel.Invalidate();
                    ShowReplayExpiredMessage();
                    break;
                case ReplayDownloadResult.Timeout:
                    ShowDownloadReplayError("等待下载超时，客户端可能仍在下载，请稍后再试。");
                    break;
                default:
                    ShowDownloadReplayError("请检查是否为最近比赛或支持回放的模式。");
                    break;
            }
        }

        /// <summary>
        /// 播放已存在的回放
        /// </summary>
        private async Task PlayExistingReplay(MatchInfo matchInfo)
        {
            if (await Globals.lcuClient.PlayReplayAsync(matchInfo.GameId))
            {
                MessageBox.Show("回放启动成功！", "播放成功", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
            }
            else
            {
                MessageBox.Show("回放启动失败", "播放失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
        }

        /// <summary>
        /// 检查是否支持回放（默认支持所有模式）
        /// </summary>
        private bool IsReplaySupported(string queueTag)
        {
            // 回放默认全部尝试，让 LCU 自己判断
            return !string.IsNullOrEmpty(queueTag);
        }

        /// <summary>
        /// 显示回放不支持消息
        /// </summary>
        private void ShowReplayNotSupportedMessage()
        {
            MessageBox.Show("该模式不支持查看回放！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Question);
        }

        /// <summary>
        /// 显示回放错误
        /// </summary>
        private void ShowReplayError(string message)
        {
            MessageBox.Show("处理回放失败: " + message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Hand);
        }

        /// <summary>
        /// 显示回放播放错误
        /// </summary>
        private void ShowReplayPlayError()
        {
            MessageBox.Show("回放下载完成，但启动播放失败", "提示", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
        }

        /// <summary>
        /// 显示回放下载错误
        /// </summary>
        private void ShowDownloadReplayError(string reason)
        {
            MessageBox.Show("下载失败，" + reason, "下载失败", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
        }

        /// <summary>
        /// 显示回放已过期
        /// </summary>
        private void ShowReplayExpiredMessage()
        {
            MessageBox.Show(
                "该场回放已因版本更新失效，无法下载观看。",
                "回放已过期",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }
        #endregion

        #region 工具方法
        /// <summary>
        /// 释放控件中的图片资源
        /// </summary>
        ///// <summary>
        ///// 检查是否是召唤师峡谷或大乱斗模式
        ///// </summary>
        //public bool IsSummonersRiftOrAram(string queueTag)
        //{
        //    if (string.IsNullOrEmpty(queueTag)) return false;

        //    string[] allowed = new string[18]
        //    {
        //        "q_400", "q_420", "q_430", "q_440", "q_450", "q_480", "q_830", "q_840", "q_850", "q_900", "q_1010",
        //        "q_1020", "q_1900", "q_2400","q_3270","q_4310","q_2450","q_4320"
        //    };
        //    return allowed.Contains(queueTag);
        //}

        /// <summary>
        /// 检查是否支持显示详细对局信息
        /// 只允许标准 5v5（固定10人）的峡谷/大乱斗系模式
        /// </summary>
        public bool IsSummonersRiftOrAram(string queueTag)
        {
            if (string.IsNullOrEmpty(queueTag))
            {
                return false;
            }

            string[] allowed = new[]
            {
                // 排位
                "q_420",
                "q_440",
                // 匹配
                "q_400",
                "q_430",
                // 大乱斗 & 无限
                "q_450",
                "q_900",
                "q_1900",
                // 快速模式
                "q_480",
                // 海克斯乱斗
                "q_2400",
                "q_2450",
                "q_3270",
                // 人机（完整）
                "q_830",
                "q_840",
                "q_850",
                "q_870",
                "q_880",
                "q_890",
                // 末日人机（已确认是5v5峡谷）
                "q_950",
                "q_960",
                // 其他标准5v5
                "q_1020", // 克隆大作战
                "q_1300", // 极限闪击
                "q_1400", // 终极魔典
                // 经典 / 训练
                "q_4310",
                "q_4320",
                "q_3140",
                "q_3100",
            };

            return allowed.Contains(queueTag);
        }
        #endregion
    }
}
