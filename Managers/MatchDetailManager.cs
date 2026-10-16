using System.Diagnostics;
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
                $"点击确定将自动下载回放文件并开始播放比赛！\n比赛ID: {matchInfo.GameId}\n模式: {matchInfo.Mode}",
                "查看回放",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question
            );

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
        private void ShowDownloadReplayError()
        {
            MessageBox.Show(
                "下载失败，请检查是否为最近比赛或支持回放的模式。",
                "下载失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Exclamation
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
