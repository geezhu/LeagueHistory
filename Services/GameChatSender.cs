using League.Extensions;
using League.Models;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using static League.FormMain;

namespace League.Services
{
    public class GameChatSender
    {
        private readonly FormMain _mainForm;
        private ChatMessageBuilder? _chatMessageBuilder;
        private bool _isSendingMessage = false;

        private const int MaxWaitMs = 1000;
        private const int RetryIntervalMs = 350;

        public GameChatSender(FormMain mainForm, ChatMessageBuilder? chatMessageBuilder)
        {
            _mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
            _chatMessageBuilder = chatMessageBuilder;
        }

        public void SetChatMessageBuilder(ChatMessageBuilder chatMessageBuilder)
        {
            _chatMessageBuilder = chatMessageBuilder ?? throw new ArgumentNullException(nameof(chatMessageBuilder));
            Debug.WriteLine("[GameChatSender] ChatMessageBuilder 已成功设置");
        }

        public async Task HandleTierAsync(Func<CancellationToken, Task<IReadOnlyList<string>>> build,
            Action<string, string>? progress = null, Func<bool>? stillCurrent = null, CancellationToken cancellationToken = default)
        {
            var diagnostics = ChatSendDiagnostics.Shared;
            void Report(string stage, string message)
            {
                diagnostics.Report(stage, message);
                progress?.Invoke(stage, message);
            }

            if (_mainForm.IsDisposed) return;
            if (cancellationToken.IsCancellationRequested || stillCurrent?.Invoke() == false)
            {
                Report("cancelled", "未发送：对局或发送请求已变化。");
                return;
            }
            if (!GameInputService.HasInputPermission())
            {
                Report("permission", "未发送：请以管理员身份重新启动 League，游戏内快捷键和输入需要相应权限。");
                return;
            }
            if (_isSendingMessage)
            {
                Report("busy", "未发送：上一条消息仍在处理，请稍后重试。");
                return;
            }
            var foreground = GameInputService.GetGameForegroundState();
            if (!foreground.IsGame)
            {
                Report("foreground", "未发送：" + foreground.Reason + "。");
                return;
            }
            Report("foreground-ok", foreground.Reason + "。");
            _isSendingMessage = true;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            try
            {
                Report("loading", "正在读取对局和统计数据，最长等待 25 秒。");
                var messages = await build(timeout.Token);
                if (messages.Count == 0 || _mainForm.IsDisposed) return;
                string phase = await GetGameflowPhaseSafeAsync();
                if (phase != "InProgress")
                {
                    Report("phase", $"未发送：数据读取后游戏阶段变为 {phase}。");
                    return;
                }
                if (stillCurrent?.Invoke() == false)
                {
                    Report("cancelled", "未发送：对局或发送请求已变化。");
                    return;
                }
                // Release Ctrl before Enter so the chord cannot change the chat channel.
                var until = DateTime.UtcNow.AddSeconds(2);
                if (!GameInputService.ModifiersReleased())
                    Report("modifiers", "请松开 Ctrl、Shift、Alt、Win，等待发送（最多 2 秒）。");
                while (!GameInputService.ModifiersReleased() && DateTime.UtcNow < until)
                    await Task.Delay(30, timeout.Token);
                if (!GameInputService.ModifiersReleased())
                {
                    Report("modifiers", "未发送：Ctrl、Shift、Alt 或 Win 在等待后仍未松开。");
                    return;
                }
                string? stopped = null;
                var input = new GameInputService();
                Report("input", $"正在输入 {messages.Count} 条消息，请保持游戏窗口在前台。");
                bool completed = await input.SendInGameMessageAsync(string.Join("\n", messages), () =>
                {
                    var current = GameInputService.GetGameForegroundState();
                    stopped = _mainForm.IsDisposed ? "程序正在关闭"
                        : timeout.IsCancellationRequested ? "发送超时"
                        : stillCurrent?.Invoke() == false ? "对局或发送请求已变化"
                        : !current.IsGame ? current.Reason
                        : !GameInputService.ModifiersReleased() ? "发送时按下了 Ctrl、Shift、Alt 或 Win"
                        : null;
                    return stopped == null;
                });
                if (completed)
                    Report("input-complete", "键盘输入已完成，请查看游戏聊天（无法确认服务器是否接收）。");
                else
                    Report("input-stopped", "发送未完成，可能已输入部分内容：" + (stopped ?? input.LastFailureReason ?? "输入未完成"));
            }
            catch (OperationCanceledException)
            {
                Report("timeout", "未完成：请求已取消或数据读取超时，请稍后重试。");
            }
            catch (InvalidOperationException ex)
            {
                Report("unavailable", ex.Message);
            }
            catch (Exception ex)
            {
                Report("error", "快捷发送失败：" + ex.GetType().Name + $"（HRESULT {ex.HResult:X8}）。请查看失败前的日志阶段。");
            }
            finally
            {
                _isSendingMessage = false;
            }
        }

        // Ctrl + F7 - 选人阶段
        public async Task HandleChampSelectF7Async()
        {
            string phase = await GetGameflowPhaseSafeAsync();
            if (phase != "ChampSelect") return;

            var (isCustomMode, msg) = await GetUiSendStateAsync();

            if (!isCustomMode)
            {
                var myTeam = _mainForm._cachedMyTeam;
                if (myTeam == null || myTeam.Count == 0)
                {
                    var session = await Globals.lcuClient.GetChampSelectSession();
                    if (session != null) _mainForm._cachedMyTeam = session["myTeam"] as JArray;
                    myTeam = _mainForm._cachedMyTeam;
                }

                if (myTeam == null || myTeam.Count == 0) return;

                // 选人阶段强制使用玩家名称（英雄未锁定）
                msg = _chatMessageBuilder?.BuildMyTeamSummary(myTeam, useChampionName: false) ?? "";
            }

            await SendMessageAsync(msg, isChampSelect: true);
        }

        public async Task HandleMyTeamAsync()
        {
            if (_isSendingMessage) return;
            await SendTeamDataAsync(isFullTeam: false);
        }

        public async Task HandleFullTeamAsync()
        {
            await SendTeamDataAsync(isFullTeam: true);
        }

        private async Task SendTeamDataAsync(bool isFullTeam)
        {
            if (_isSendingMessage) return;

            string phase = await GetGameflowPhaseSafeAsync();
            if (phase != "InProgress") return;

            var (isCustomMode, msg) = await GetUiSendStateAsync();
            if (isCustomMode)
            {
                await SendMessageAsync(msg, false);
                return;
            }

            var config = _mainForm.GetAppConfig();
            bool useChampionName = config?.UseChampionNameWhenSending ?? true;

            JArray? myTeam = await EnsureTeamDataReadyAsync(_mainForm._cachedMyTeam, true);
            JArray? enemyTeam = isFullTeam ? await EnsureTeamDataReadyAsync(_mainForm._cachedEnemyTeam, false) : null;

            if (myTeam?.Count == 0 || (isFullTeam && enemyTeam?.Count == 0))
                return;

            msg = isFullTeam
                ? _chatMessageBuilder?.BuildFullTeamSummary(myTeam, enemyTeam, useChampionName) ?? ""
                : _chatMessageBuilder?.BuildMyTeamSummary(myTeam, useChampionName) ?? "";

            if (string.IsNullOrWhiteSpace(msg)) return;

            try
            {
                _isSendingMessage = true;
                await SendMessageAsync(msg, false);
            }
            finally
            {
                _isSendingMessage = false;
            }
        }

        private async Task<JArray?> EnsureTeamDataReadyAsync(JArray? team, bool isMyTeam)
        {
            if (team == null || team.Count == 0)
            {
                var session = await Globals.lcuClient.GetGameSession();
                if (session != null)
                {
                    team = isMyTeam ? session["gameData"]?["teamOne"] as JArray : session["gameData"]?["teamTwo"] as JArray;
                }
            }

            if (team == null || team.Count == 0) return null;

            var start = DateTime.Now;
            int attempt = 0;
            int readyCount = 0;

            while ((DateTime.Now - start).TotalMilliseconds < MaxWaitMs)
            {
                attempt++;
                var infos = _mainForm._playerCardManager?.GetAllCachedPlayerInfos() ?? new Dictionary<long, PlayerMatchInfo>();

                readyCount = 0;
                foreach (var p in team)
                {
                    long sid = p["summonerId"]?.Value<long>() ?? 0;
                    if (sid != 0 && infos.ContainsKey(sid))
                        readyCount++;
                }

                if (readyCount == team.Count)
                {
                    Debug.WriteLine($"[Ensure] {(isMyTeam ? "我方" : "敌方")} 数据完全就绪！");
                    return team;
                }

                if (attempt % 6 == 0)
                    Debug.WriteLine($"[Ensure] {(isMyTeam ? "我方" : "敌方")} 等待中... {readyCount}/{team.Count}");

                await Task.Delay(RetryIntervalMs);
            }

            Debug.WriteLine($"[Ensure] {(isMyTeam ? "我方" : "敌方")} 等待超时，最终就绪 {readyCount}/{team.Count}");
            return team;
        }

        private async Task<(bool isCustomMode, string msg)> GetUiSendStateAsync()
        {
            bool isCustomMode = false;
            string msg = "";

            await _mainForm.InvokeIfRequiredAsync(async () =>
            {
                isCustomMode = _mainForm.GetSendMode();
                msg = _mainForm.GetCustomSendContent();
            });

            return (isCustomMode, msg);
        }

        private async Task SendMessageAsync(string msg, bool isChampSelect)
        {
            if (string.IsNullOrWhiteSpace(msg)) return;

            bool success = isChampSelect
                ? await Globals.lcuClient.SendChampSelectMessageAsync(msg)
                : await Globals.lcuClient.SendInGameMessageAsync(msg);

            Debug.WriteLine($"[消息发送] {(isChampSelect ? "选人阶段" : "游戏内")} 发送 {(success ? "成功" : "失败")}");
        }

        private async Task<string> GetGameflowPhaseSafeAsync()
        {
            try
            {
                return await Task.Run(async () => await Globals.lcuClient.GetGameflowPhase())
                                 .WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch
            {
                return "Unknown";
            }
        }
    }
}
