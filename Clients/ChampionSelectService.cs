using System.Diagnostics;
using System.Text;
using League.PrimaryElection;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Clients
{
    /// <summary>
    /// 英雄选择服务
    /// </summary>
    public class ChampionSelectService
    {
        private readonly LcuClient _client;
        private readonly GameflowService _gameflowService;

        public ChampionSelectService(LcuClient client, GameflowService gameflowService)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _gameflowService = gameflowService ?? throw new ArgumentNullException(nameof(gameflowService));
        }

        /// <summary>
        /// 经典模式 queue（峡谷 / 人机）：选人会话里的英雄 ID = 现代英雄 ID + 60000
        /// </summary>
        private static bool IsClassicQueue(int queueId) => queueId is 4310 or 4320;

        /// <summary>
        /// 现代英雄 ID → 当前选人会话使用的 ID
        /// </summary>
        private static int ToSessionChampionId(int modernChampionId, int queueId)
        {
            if (modernChampionId <= 0) return modernChampionId;
            return IsClassicQueue(queueId) ? modernChampionId + 60000 : modernChampionId;
        }

        /// <summary>
        /// 会话里的英雄 ID → 现代英雄 ID（回读校验用）
        /// </summary>
        private static int FromSessionChampionId(int sessionChampionId, int queueId)
        {
            if (IsClassicQueue(queueId) && sessionChampionId >= 60000)
                return sessionChampionId - 60000;
            return sessionChampionId;
        }

        /// <summary>
        /// 自动预选英雄（兼容现代模式 + 经典峡谷/人机 ID 偏移）
        /// </summary>
        public async Task<bool> AutoDeclareIntentAsync(List<PreliminaryHero> preSelectedHeroes)
        {
            // 先判空再写日志：列表为空时原来会在日志这一行抛出空引用。
            if (preSelectedHeroes == null || !preSelectedHeroes.Any())
            {
                return false;
            }

            Debug.WriteLine($"[LCU 自动预选] 开始执行，列表 {preSelectedHeroes.Count} 个英雄");

            try
            {
                var (session, apiPrefix) = await ResolveChampSelectSessionAsync();
                if (session == null || session["actions"] == null)
                {
                    return false;
                }

                var myPickAction = FindMyPendingPick(session);
                if (myPickAction == null)
                {
                    return false;
                }

                var actionId = myPickAction["id"]?.Value<int>() ?? 0;
                if (actionId == 0)
                {
                    return false;
                }

                // session 里已禁/已选是「会话 ID」（经典为 600xx）
                var bannedSessionIds = GetBannedAndSelectedChampions(session);

                // 找第一个可用英雄
                var availableHero = preSelectedHeroes.FirstOrDefault(h => !bannedChampIds.Contains(h.ChampionId));

                var availableHero = preSelectedHeroes.FirstOrDefault(h => !IsUnavailable(h.ChampionId));
                if (availableHero == null)
                {
                    LogPickState("自动预选：预选英雄都已被禁用或被选走");
                    return false;
                }

                // 检查是否已经 hover 了这个英雄
                var currentHoverId = myPickAction["championId"]?.Value<int>() ?? 0;
                if (currentHoverId == availableHero.ChampionId)
                {
                    return false; // 已经预选，无需重复
                }

                // 执行预选
                var patchBody = new JObject { ["championId"] = availableHero.ChampionId };
                var jsonContent = new StringContent(patchBody.ToString(), Encoding.UTF8, "application/json");

                var response = await _client.PatchAsync(
                    $"/lol-champ-select/v1/session/actions/{actionId}",
                    jsonContent
                );

                if (response.IsSuccessStatusCode)
                {
                    LogPickState($"自动预选：已预选 {availableHero.ChampionName}（{availableHero.ChampionId}）");
                    return true;
                }

                LogPickState($"自动预选：客户端返回 {(int)response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                AppLog.Shared.Warn(LogCategory.Automation, "[自动预选] 执行异常", ex);
                return false;
            }
        }

        // The pick loop polls every second; only a changed outcome is logged.
        private static void LogPickState(string message) =>
            Services.AppLog.Shared.State(Services.LogCategory.Automation, "auto-pick", message);

        // 自己还没完成的 pick 操作。
        private static JToken? FindMyPendingPick(JObject session)
        {
            var actions = session["actions"] as JArray;
            return actions
                .SelectMany(a => a as JArray)
                .FirstOrDefault(a =>
                    a["actorCellId"]?.Value<long>() == session["localPlayerCellId"]?.Value<long>()
                    && a["type"]?.ToString() == "pick"
                    && a["completed"]?.Value<bool>() != true
                );
        }

        // 自己当前持有的英雄；不在房间或还没分到英雄时为 0。
        private static int GetMyChampionId(JObject session)
        {
            var myCellId = session["localPlayerCellId"]?.Value<int>() ?? -1;
            if (myCellId == -1)
            {
                return 0;
            }

            var myTeam = session["myTeam"] as JArray ?? new JArray();
            var myPlayer = myTeam.FirstOrDefault(p => p["cellId"]?.Value<int>() == myCellId);
            return myPlayer?["championId"]?.Value<int>() ?? 0;
        }

        // 板凳席上可以交换的英雄。
        private static HashSet<int> GetBenchChampionIds(JObject session)
        {
            var benchChampions = session["benchChampions"] as JArray ?? new JArray();
            var availableChampIds = new HashSet<int>();
            foreach (var item in benchChampions)
            {
                var id = item["championId"]?.Value<int>() ?? 0;
                if (id > 0)
                {
                    availableChampIds.Add(id);
                }
            }
            return availableChampIds;
        }

        /// <summary>
        /// 解析选人 session：经典模式优先 legacy
        /// </summary>
        private async Task<(JObject? session, string apiPrefix)> ResolveChampSelectSessionAsync()
        {
            // 1) 经典模式：implementation-active == true 时优先 legacy
            try
            {
                var activeResp = await _client.GetAsync("/lol-champ-select-legacy/v1/implementation-active");
                if (activeResp.IsSuccessStatusCode)
                {
                    var text = (await activeResp.Content.ReadAsStringAsync()).Trim().Trim('"').ToLowerInvariant();
                    if (text == "true")
                    {
                        var legacySession = await GetSessionByPrefixAsync("/lol-champ-select-legacy/v1");
                        if (legacySession != null)
                            return (legacySession, "/lol-champ-select-legacy/v1");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[自动预选] 探测 legacy active 失败: {ex.Message}");
            }

            // 2) 现代 session
            var modern = await _gameflowService.GetChampSelectSessionAsync();
            if (modern != null && modern.HasValues)
            {
                // session 自带 isLegacyChampSelect 时，仍优先用 legacy 写
                if (modern["isLegacyChampSelect"]?.Value<bool>() == true)
                {
                    var legacySession = await GetSessionByPrefixAsync("/lol-champ-select-legacy/v1");
                    if (legacySession != null)
                        return (legacySession, "/lol-champ-select-legacy/v1");
                }

                // queueId 是经典峡谷/人机时，也优先 legacy
                int q = modern["queueId"]?.Value<int>() ?? 0;
                if (q is 4310 or 4320)
                {
                    var legacySession = await GetSessionByPrefixAsync("/lol-champ-select-legacy/v1");
                    if (legacySession != null)
                        return (legacySession, "/lol-champ-select-legacy/v1");
                }

                return (modern, "/lol-champ-select/v1");
            }

            // 3) 最后再试 legacy session
            var fallback = await GetSessionByPrefixAsync("/lol-champ-select-legacy/v1");
            if (fallback != null)
                return (fallback, "/lol-champ-select-legacy/v1");

            return (null, "/lol-champ-select/v1");
        }

        private async Task<JObject?> GetSessionByPrefixAsync(string apiPrefix)
        {
            try
            {
                var resp = await _client.GetAsync($"{apiPrefix}/session");
                if (!resp.IsSuccessStatusCode)
                    return null;

                var json = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(json) || json == "null")
                    return null;

                return JObject.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// ARAM 模式自动抢英雄
        /// 支持模式：450=大乱斗、2400=海克斯乱斗、900=无限乱斗
        /// </summary>
        public async Task AutoSwapToHighestPriorityAsync(List<PreliminaryHero> preSelectedHeroes)
        {
            if (preSelectedHeroes == null || !preSelectedHeroes.Any())
            {
                return;
            }

            try
            {
                var session = await _gameflowService.GetChampSelectSessionAsync();
                if (session == null)
                {
                    return;
                }

                var queueId = session["queueId"]?.Value<int>() ?? 0;

                // 只有非ARAM模式才返回，450=大乱斗、2400=海克斯乱斗、900=无限乱斗
                if (queueId != 450 && queueId != 2400 && queueId != 900)
                {
                    return;
                }

                var currentChampId = GetMyChampionId(session);
                if (currentChampId == 0)
                {
                    return;
                }

                var currentPriority =
                    preSelectedHeroes.FirstOrDefault(h => h.ChampionId == currentChampId)?.Priority ?? int.MaxValue;
                var availableChampIds = GetBenchChampionIds(session);

                // 找出 bench 中优先级最高的预选英雄
                var bestAvailable = preSelectedHeroes
                    .Where(h => availableChampIds.Contains(h.ChampionId))
                    .OrderBy(h => h.Priority)
                    .FirstOrDefault();

                if (bestAvailable == null)
                {
                    return;
                }

                // 只有当 bench 中的最高优先级严格高于当前持有，才抢
                if (bestAvailable.Priority < currentPriority)
                {
                    var response = await _client.PostAsync(
                        $"/lol-champ-select/v1/session/bench/swap/{bestAvailable.ChampionId}"
                    );

                    Services.AppLog.Shared.Info(
                        Services.LogCategory.Automation,
                        $"板凳席换英雄：{bestAvailable.ChampionName}（{bestAvailable.ChampionId}）→ {(int)response.StatusCode}"
                    );
                    if (response.IsSuccessStatusCode)
                    {
                        await Task.Delay(100); // 稍微延时，避免API过于频繁
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Shared.Warn(LogCategory.Automation, "[ARAM 贪婪抢] 异常", ex);
            }
        }

        /// <summary>
        /// 获取已禁和已选英雄列表
        /// </summary>
        private HashSet<int> GetBannedAndSelectedChampions(JObject session)
        {
            var bannedChampIds = new HashSet<int>();

            // 获取禁用英雄
            var myTeamBans = session["bans"]?["myTeamBans"] as JArray ?? new JArray();
            var theirTeamBans = session["bans"]?["theirTeamBans"] as JArray ?? new JArray();

            foreach (var ban in myTeamBans.Concat(theirTeamBans))
            {
                var champId = ban.Value<int>();
                if (champId > 0)
                {
                    bannedChampIds.Add(champId);
                }
            }

            // 获取我方已选英雄
            var myTeam = session["myTeam"] as JArray ?? new JArray();
            foreach (var player in myTeam)
            {
                var champId = player["championId"]?.Value<int>() ?? 0;
                if (champId > 0)
                {
                    bannedChampIds.Add(champId);
                }
            }

            return bannedChampIds;
        }

        /// <summary>
        /// 选人阶段切换皮肤：PATCH /lol-champ-select/v1/session/my-selection
        /// </summary>
        public async Task<bool> TryApplyPreferredSkinAsync(int championId, int skinId, int? lastAppliedSkinId = null)
        {
            if (championId <= 0 || skinId <= 0 || skinId % 1000 == 0)
            {
                return false;
            }

            if (lastAppliedSkinId == skinId)
            {
                return false;
            }

            try
            {
                var body = new JObject { ["selectedSkinId"] = skinId };
                var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                var resp = await _client.PatchAsync("/lol-champ-select/v1/session/my-selection", content);

                Services.AppLog.Shared.Info(
                    Services.LogCategory.Automation,
                    $"自动换皮肤：英雄 {championId} 皮肤 {skinId} → {(int)resp.StatusCode}"
                );
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                AppLog.Shared.Warn(LogCategory.Automation, "[自动皮肤]", ex);
                return false;
            }
        }
    }
}
