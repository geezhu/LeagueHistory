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
        private const string ModernApi = "/lol-champ-select/v1";
        private const string LegacyApi = "/lol-champ-select-legacy/v1";

        // Classic Rift lists every champion again at id + 60000 (Jade_Shaco is 60035), and its session uses those ids.
        private const int ClassicChampionOffset = 60000;

        private readonly LcuClient _client;
        private readonly GameflowService _gameflowService;

        public ChampionSelectService(LcuClient client, GameflowService gameflowService)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _gameflowService = gameflowService ?? throw new ArgumentNullException(nameof(gameflowService));
        }

        /// <summary>Classic Rift (4310) and its co-op queue (4320).</summary>
        public static bool IsClassicQueue(int queueId) => queueId is 4310 or 4320;

        /// <summary>
        /// The champion select the client is running and the API that writes to it. Classic Rift runs on the legacy
        /// champion select (upstream 21ed806): the modern session says so, carries a classic queue, or is missing.
        /// </summary>
        public async Task<(JObject? Session, string Api)> GetPickSessionAsync()
        {
            var modern = await _gameflowService.GetChampSelectSessionAsync();
            bool hasModern = modern is { HasValues: true };
            bool legacy =
                !hasModern
                || modern!["isLegacyChampSelect"]?.Value<bool>() == true
                || IsClassicQueue(modern["queueId"]?.Value<int>() ?? 0);
            if (legacy && await GetLegacySessionAsync() is { } legacySession)
            {
                return (legacySession, LegacyApi);
            }

            return (hasModern ? modern : null, ModernApi);
        }

        private async Task<JObject?> GetLegacySessionAsync()
        {
            try
            {
                using var response = await _client.GetAsync(LegacyApi + "/session");
                return response.IsSuccessStatusCode ? JObject.Parse(await response.Content.ReadAsStringAsync()) : null;
            }
            catch (Exception ex) when (ex is HttpRequestException or Newtonsoft.Json.JsonException)
            {
                // Without the legacy session the modern one is still tried.
                AppLog.Shared.Warn(LogCategory.Automation, "[自动预选] 读取经典选人会话失败", ex);
                return null;
            }
        }

        private static int ToSessionChampionId(int championId, int queueId) =>
            IsClassicQueue(queueId) && championId > 0 ? championId + ClassicChampionOffset : championId;

        /// <summary>
        /// 自动预选英雄（根据优先级列表）；客户端确实换上后才返回 true
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
                var (session, api) = await GetPickSessionAsync();
                if (session?["actions"] == null)
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

                // 已禁和已选是会话里的 ID，预选列表是现代 ID，比较前先换算
                int queueId = session["queueId"]?.Value<int>() ?? 0;
                var bannedChampIds = GetBannedAndSelectedChampions(session);
                var availableHero = preSelectedHeroes.FirstOrDefault(h =>
                    !bannedChampIds.Contains(ToSessionChampionId(h.ChampionId, queueId))
                );

                if (availableHero == null)
                {
                    LogPickState("自动预选：预选英雄都已被禁用或被选走");
                    return false;
                }

                int sessionChampionId = ToSessionChampionId(availableHero.ChampionId, queueId);
                if ((myPickAction["championId"]?.Value<int>() ?? 0) == sessionChampionId)
                {
                    return true; // 已经预选
                }

                var patchBody = new JObject { ["championId"] = sessionChampionId };
                var jsonContent = new StringContent(patchBody.ToString(), Encoding.UTF8, "application/json");
                var response = await _client.PatchAsync($"{api}/session/actions/{actionId}", jsonContent);
                if (!response.IsSuccessStatusCode)
                {
                    LogPickState($"自动预选：客户端返回 {(int)response.StatusCode}");
                    return false;
                }

                // 经典选人有时接受请求却没有换上；回读会话确认，未换上就让调用方继续轮询
                await Task.Delay(150);
                var (after, _) = await GetPickSessionAsync();
                if (!HasPicked(after, actionId, sessionChampionId))
                {
                    LogPickState($"自动预选：客户端接受了 {availableHero.ChampionName} 但会话未变化，稍后重试");
                    return false;
                }

                LogPickState($"自动预选：已预选 {availableHero.ChampionName}（{availableHero.ChampionId}）");
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Shared.Warn(LogCategory.Automation, "[自动预选] 执行异常", ex);
                return false;
            }
        }

        // The pick shows on the action, or on the local player as an intent or a locked champion.
        private static bool HasPicked(JObject? session, int actionId, int championId)
        {
            if (session == null)
            {
                return false;
            }

            var action = (session["actions"] as JArray)
                ?.SelectMany(group => group as JArray ?? new JArray())
                .FirstOrDefault(a => a["id"]?.Value<int>() == actionId);
            long localCell = session["localPlayerCellId"]?.Value<long>() ?? -1;
            var self = (session["myTeam"] as JArray)?.FirstOrDefault(p => p["cellId"]?.Value<long>() == localCell);
            return action?["championId"]?.Value<int>() == championId
                || self?["championPickIntent"]?.Value<int>() == championId
                || self?["championId"]?.Value<int>() == championId;
        }

        // The pick loop polls every second; only a changed outcome is logged.
        private static void LogPickState(string message) =>
            Services.AppLog.Shared.State(Services.LogCategory.Automation, "auto-pick", message);

        // 自己还没完成的 pick 操作。
        private static JToken? FindMyPendingPick(JObject session)
        {
            var actions = session["actions"] as JArray;
            return actions
                ?.SelectMany(a => a as JArray ?? new JArray())
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
