using System.Diagnostics;
using League.PrimaryElection;
using Newtonsoft.Json.Linq;
using static League.FormMain;

namespace League.Services
{
    /// <summary>
    /// 自动预选 / 抢英雄服务
    /// </summary>
    public class AutoPickService
    {
        private readonly FormMain _form;
        private CancellationTokenSource? _champSelectCts;

        private bool _hasAutoPreliminated = false;
        private bool _hasSwappedInAram = false;
        private TeamCardDisplayService? _cardDisplayService;
        private int _lastSkinChampionId = 0; // 上次处理的英雄
        private int _lastAppliedSkinId = 0; // 上次成功/尝试的皮肤

        public AutoPickService(FormMain form)
        {
            _form = form;
        }

        /// <summary>
        /// 启动选人阶段监控（同时负责我方卡片更新）
        /// </summary>
        public async Task StartChampSelectMonitoring(TeamCardDisplayService cardDisplayService)
        {
            _cardDisplayService = cardDisplayService;
            if (_champSelectCts != null)
            {
                await _champSelectCts.CancelAsync();
            }

            _champSelectCts = new CancellationTokenSource();
            var token = _champSelectCts.Token;

            _hasAutoPreliminated = false;
            _hasSwappedInAram = false;
            // A new champion select: log its outcomes even if they match the previous game's.
            AppLog.Shared.ResetStates("auto-pick", "game-queue");

            await Task.Run(
                async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var currentPhase = await Globals.lcuClient.GetGameflowPhase();
                            if (currentPhase != "ChampSelect")
                            {
                                break;
                            }

                            // 我方队伍卡片显示
                            await _cardDisplayService.ShowMyTeamCards(token);

                            // 自动预选抢英雄
                            await TryAutoPreliminaryAsync(token);

                            // 自动配置皮肤
                            await TryAutoSkinAsync(token);

                            var session = await Globals.lcuClient.GetChampSelectSession(token);
                            int queueId = session?["queueId"]?.Value<int>() ?? 0;
                            AppLog.Shared.State(LogCategory.Game, "game-queue", $"选人队列 {queueId}");

                            // 乱斗类轮询更快，其它模式 1 秒
                            int delay = (queueId == 450 || queueId == 2400 || queueId == 900) ? 500 : 1000;
                            await Task.Delay(delay, token);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (OperationCanceledException)
                        {
                            RoomLoadDiagnostics.Shared.Report(0, "selection-timeout");
                        }
                        catch (Exception ex)
                        {
                            AppLog.Shared.Warn(LogCategory.Automation, "[AutoPickService] 异常", ex);
                        }
                    }
                },
                token
            );
        }

        public void Stop()
        {
            _champSelectCts?.Cancel();
            _champSelectCts?.Dispose();
            _champSelectCts = null;
        }

        public void Reset()
        {
            _hasAutoPreliminated = false;
            _hasSwappedInAram = false;

            _lastSkinChampionId = 0;
            _lastAppliedSkinId = 0;
        }

        /// <summary>
        /// 自动预选功能核心方法，新增根据勾选的模式进行过滤
        /// </summary>
        private async Task TryAutoPreliminaryAsync(CancellationToken cancellationToken)
        {
            var preConfig = _form.GetAppConfig()?.Preliminary;
            if (preConfig == null || !preConfig.EnableAutoPreliminary)
            {
                LogPickState("自动预选：未开启");
                return;
            }

            var lists = await _form.GetPreliminaryConfigAsync();
            if (!PreliminaryLists.AnyHeroes(lists))
            {
                LogPickState("自动预选：预选列表为空");
                return;
            }

            var session = await Globals.lcuClient.GetChampSelectSession(cancellationToken);
            if (session == null)
            {
                return;
            }

            int queueId = session["queueId"]?.Value<int>() ?? 0;

            // 根据 queueId 和用户配置决定是否执行
            bool shouldExecute = queueId switch
            {
                400 or 430 => _form.GetAppConfig().EnablePreliminaryInNormal, // 匹配
                420 or 440 => _form.GetAppConfig().EnablePreliminaryInRanked, // 排位
                450 => _form.GetAppConfig().EnablePreliminaryInAram, // 大乱斗
                2400 => _form.GetAppConfig().EnablePreliminaryInNexusBlitz, // 海克斯
                900 => _form.GetAppConfig().EnablePreliminaryInUrf, // 无限乱斗（随机）
                1900 => _form.GetAppConfig().EnablePreliminaryInPickUrf, // 无限火力（自选）
                _ => false,
            };

            if (!shouldExecute)
            {
                LogPickState($"自动预选：队列 {queueId} 未启用或不支持");
                return;
            }

            var (listKey, preList) = PreliminaryLists.Resolve(lists, queueId, AssignedPosition(session));
            if (preList.Count == 0)
            {
                LogPickState($"自动预选：「{PreliminaryLists.NameOf(listKey)}」列表为空");
                return;
            }

            // 有板凳席的乱斗类模式：大乱斗、海克斯、无限乱斗 → 抢英雄
            if (PreliminaryLists.IsBenchQueue(queueId))
            {
                LogPickState(
                    $"自动预选：队列 {queueId} 按「{PreliminaryLists.NameOf(listKey)}」列表从板凳席换优先英雄"
                );
                await Globals.lcuClient.AutoSwapToHighestPriorityAsync(preList);
                return;
            }

            // 普通自选模式：匹配、排位、无限火力 → 只预选一次
            if (_hasAutoPreliminated)
            {
                return;
            }

            LogPickState($"自动预选：队列 {queueId} 使用「{PreliminaryLists.NameOf(listKey)}」列表");
            bool success = await Globals.lcuClient.AutoDeclareIntentAsync(preList);
            if (success)
            {
                _hasAutoPreliminated = true;
            }
        }

        // The local player's assigned lane (top, jungle, middle, bottom, utility); empty in blind pick.
        private static string? AssignedPosition(JObject session)
        {
            var cell = session["localPlayerCellId"]?.Value<int>();
            var self = (session["myTeam"] as JArray)?.FirstOrDefault(p => p["cellId"]?.Value<int>() == cell);
            return self?["assignedPosition"]?.ToString().ToLowerInvariant();
        }

        // Same key as ChampionSelectService, so the log reads as one sequence of auto-pick outcomes.
        private static void LogPickState(string message) =>
            AppLog.Shared.State(LogCategory.Automation, "auto-pick", message);

        /// <summary>
        /// 选人阶段：若开启自动皮肤，且当前英雄在 PreferredChampionSkins 中有非默认皮，则 PATCH。
        /// </summary>
        private async Task TryAutoSkinAsync(CancellationToken cancellationToken)
        {
            var cfg = _form.GetAppConfig();
            if (cfg == null || !cfg.EnableAutoSkinSelect)
            {
                return;
            }

            if (cfg.PreferredChampionSkins == null || cfg.PreferredChampionSkins.Count == 0)
            {
                return;
            }

            try
            {
                var session = await Globals.lcuClient.GetChampSelectSession(cancellationToken);
                if (session == null)
                {
                    return;
                }

                var me = FindMySelection(session);
                int championId = me == null ? 0 : GetSelectedChampionId(me);
                if (championId <= 0)
                {
                    return;
                }

                // 换英雄了：允许再切一次皮
                if (championId != _lastSkinChampionId)
                {
                    _lastSkinChampionId = championId;
                    _lastAppliedSkinId = 0;
                }

                if (
                    !cfg.PreferredChampionSkins.TryGetValue(championId, out int wantSkin)
                    || wantSkin <= 0
                    || wantSkin % 1000 == 0
                )
                {
                    return;
                }

                int currentSkin = me["selectedSkinId"]?.Value<int>() ?? 0;
                if (currentSkin == wantSkin)
                {
                    _lastAppliedSkinId = wantSkin;
                    return;
                }

                if (_lastAppliedSkinId == wantSkin)
                {
                    return;
                }

                bool ok = await Globals.lcuClient.TryApplyPreferredSkinAsync(championId, wantSkin, _lastAppliedSkinId);

                // 无论成功与否先记下，避免失败时每秒刷屏；若要失败重试可只在 ok 时赋值
                _lastAppliedSkinId = wantSkin;

                if (ok)
                {
                    Debug.WriteLine($"[自动皮肤] 已请求切换 champ={championId} skin={wantSkin}");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Shared.Warn(LogCategory.Automation, "[自动皮肤] TryAutoSkinAsync", ex);
            }
        }

        // 选人会话里自己的那一格。
        private static JToken? FindMySelection(JObject session)
        {
            int cellId = session["localPlayerCellId"]?.Value<int>() ?? -1;
            if (cellId < 0)
            {
                return null;
            }

            var myTeam = session["myTeam"] as JArray;
            return myTeam?.FirstOrDefault(p => p["cellId"]?.Value<int>() == cellId);
        }

        // 已锁定/已选 > 悬停意图
        private static int GetSelectedChampionId(JToken me)
        {
            int championId = me["championId"]?.Value<int>() ?? 0;
            return championId > 0 ? championId : me["championPickIntent"]?.Value<int>() ?? 0;
        }
    }
}
