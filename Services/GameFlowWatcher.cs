using League.Clients;
using League.Managers;
using League.Parsers;
using League.UIState;
using System.Diagnostics;
using static League.FormMain;

namespace League.Services
{
    /// <summary>
    /// 游戏流程协调者（精简版）
    /// </summary>
    public class GameFlowWatcher
    {
        private readonly FormMain _form;
        private readonly FormUiStateManager _uiManager;
        private readonly PlayerCardManager _cardManager;
        private readonly MatchQueryProcessor _matchQueryProcessor;

        private readonly AutoQueueAccepter _autoAccepter;
        private readonly AutoPickService _autoPickService;
        private readonly TeamCardDisplayService _cardDisplayService;
        private readonly GameEndCleanupService _cleanupService;

        private string? _lastHandledPhase;

        // 断线前是否在对局中（用于重连后补卡）
        private bool _wasInGameBeforeDisconnect;

        public GameFlowWatcher(FormMain form, FormUiStateManager uiManager,
            PlayerCardManager cardManager, MatchQueryProcessor matchQueryProcessor)
        {
            _form = form;
            _uiManager = uiManager;
            _cardManager = cardManager;
            _matchQueryProcessor = matchQueryProcessor;

            _autoAccepter = new AutoQueueAccepter(form);
            _autoPickService = new AutoPickService(form);
            _cardDisplayService = new TeamCardDisplayService(form, cardManager);
            _cleanupService = new GameEndCleanupService(form, cardManager, uiManager);

            LcuManager.Instance.PhaseChanged += OnGameflowPhaseChanged;
            LcuManager.Instance.Disconnected += HandleLcuDisconnected;
        }

        #region 启动与停止
        public async void StartGameflowWatcher()
        {
            if (!_uiManager.LcuReady) return;
            //Debug.WriteLine("[GameFlowWatcher] StartGameflowWatcher 就绪（事件已由 LcuManager 驱动）");
        }

        public void StopGameflowWatcher()
        {
            _autoPickService.Stop();
            _cleanupService.Cleanup();
            _cardDisplayService.ClearSnapshots();
        }

        private void HandleLcuDisconnected()
        {
            _autoAccepter.Reset();
            Debug.WriteLine("[GameFlowWatcher] 检测到 LCU 断开，准备重启连接轮询");

            Debug.WriteLine("[GameFlowWatcher] 检测到 LCU 断开");

            _wasInGameBeforeDisconnect = _uiManager.IsGame
                || (_form._cachedMyTeam != null && _form._cachedMyTeam.Count > 0)
                || (_form._cachedEnemyTeam != null && _form._cachedEnemyTeam.Count > 0);

            _lastHandledPhase = null;
            _uiManager.LcuReady = false;
            _uiManager.IsGame = false;
            _uiManager.SetLcuUiState(false, false);

            _autoPickService.Stop();

            _cardDisplayService.ClearSnapshots();

            // 通知主窗体重新启动等待连接的轮询
            _form.StartLcuConnectPolling();
        }
        #endregion

        public async Task OnGameflowPhaseChanged(string phase, string? previousPhase)
        {
            if ((phase is "EndOfGame" or "PreEndOfGame" or "WaitingForStats") && (previousPhase is "GameStart" or "InProgress" or "Reconnect"))
                Clients.ChampionMasteryService.Shared.Invalidate(_form.GetCurrentPuuid() ?? "");
            if (phase != "ReadyCheck") _autoAccepter.Reset();
            try
            {
                //Debug.WriteLine($"[Phase Changed] {previousPhase ?? "null"} → {phase} | 时间: {DateTime.Now:HH:mm:ss.fff}");

                switch (phase)
                {
                    case "Matchmaking":
                    case "ReadyCheck":
                        _wasInGameBeforeDisconnect = false;
                        await HandleQueuePhase(phase);
                        break;

                    case "ChampSelect":
                        await HandleChampSelectStart();
                        break;

                    case "GameStart":
                        _autoPickService.Stop();
                        Debug.WriteLine("[GameFlowWatcher] GameStart：停止自动选人");
                        break;

                    case "InProgress":
                        await HandleGameInProgress(previousPhase);
                        _wasInGameBeforeDisconnect = false;
                        break;

                    case "EndOfGame":
                    case "PreEndOfGame":
                    case "WaitingForStats":
                        _autoPickService.Stop();
                        _cardDisplayService.ClearSnapshots();
                        Debug.WriteLine($"[GameEnd Trigger] 检测到结束阶段: {phase}, previous={previousPhase}");
                        await HandleGameEnd(previousPhase);
                        break;

                    case "Lobby":
                    case "None":
                        _autoPickService.Stop();
                        _cardDisplayService.ClearSnapshots();
                        if (_cleanupService.ShouldReturnToLobby())
                        {
                            Debug.WriteLine($"[Lobby] 检测到刚结束游戏，开始返回模式 {Globals.CurrGameMod}");
                            await _cleanupService.ReturnToSpecificLobbyAsync();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameFlowWatcher] 阶段处理异常: {ex}");
            }
        }

        private async Task HandleQueuePhase(string phase)
        {
            _autoPickService.Stop();
            _cardDisplayService.ClearSnapshots();
            _uiManager.IsGame = false;
            _cardManager.ClearAllCaches();
            _cardManager.ClearGameState();
            _matchQueryProcessor.ClearPlayerMatchCache();
            _cleanupService.Reset();
            _autoAccepter.Reset();

            if (phase == "ReadyCheck" && _form.GetAppConfig()?.EnableAutoAcceptQueue == true)
            {
                int delay = _form.GetAppConfig()?.AutoAcceptDelaySeconds ?? 0;
                if (delay != 5 && delay != 10) delay = 0;

                _ = _autoAccepter.TryAcceptAsync(delay);
            }

            FormUiStateManager.SafeInvoke(_form.imageTabControl1, () =>
            {
                _uiManager.SetLcuUiState(_uiManager.LcuReady, false);
                _form.imageTabControl1.SelectedIndex = 1;
            });
        }

        private async Task HandleChampSelectStart()
        {
            _autoPickService.Stop();
            _uiManager.IsGame = true;
            await _cardDisplayService.EnterChampSelectAsync();
            _autoPickService.StartChampSelectMonitoring(_cardDisplayService);
        }

        private Task HandleGameInProgress()
        {
            _uiManager.IsGame = true;
            _autoPickService.Stop();
            _cardDisplayService.StartGameSessionLoad();
            return Task.CompletedTask;
        }

        private async Task HandleGameEnd(string? previousPhase)
        {
            Debug.WriteLine($"[HandleGameEnd] 被调用 | previousPhase={previousPhase}");

            bool shouldHandle = previousPhase == "InProgress"
                             || previousPhase == "WaitingForStats"
                             || previousPhase == "ChampSelect"
                             || previousPhase == "EndOfGame"
                             || previousPhase == "PreEndOfGame";

            if (shouldHandle)
            {
                if (!string.IsNullOrEmpty(Globals.CurrGameMod))
                {
                    _cleanupService.MarkGameJustEnded();
                    Debug.WriteLine(
                        $"[HandleGameEnd] 已标记游戏结束，等待Lobby后返回模式 {Globals.CurrGameMod}");
                }

                await _cleanupService.HandleGameEndAsync(previousPhase);
                _autoAccepter.Reset();
                _autoPickService.Reset();
            }
            else
            {
                Debug.WriteLine($"[HandleGameEnd] 条件不满足，跳过处理");
            }
        }

        public void ClearGameState()
        {
            _cardDisplayService.ClearSnapshots();
            _cardManager.ClearGameState();
        }
    }
}
