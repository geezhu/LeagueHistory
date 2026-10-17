using League.PrimaryElection;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Text;

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
            Debug.WriteLine($"[LCU 自动预选] 开始执行，列表 {preSelectedHeroes?.Count ?? 0} 个英雄");

            if (preSelectedHeroes == null || !preSelectedHeroes.Any())
                return false;

            try
            {
                var (session, apiPrefix) = await ResolveChampSelectSessionAsync();
                if (session == null || session["actions"] == null)
                {
                    Debug.WriteLine($"[自动预选] session/actions 为空, prefix={apiPrefix}");
                    return false;
                }

                int queueId = session["queueId"]?.Value<int>() ?? 0;
                bool isLegacyFlag = session["isLegacyChampSelect"]?.Value<bool>() ?? false;
                Debug.WriteLine($"[自动预选] queueId={queueId}, isClassic={IsClassicQueue(queueId)}, isLegacyFlag={isLegacyFlag}, prefix={apiPrefix}");

                var actions = session["actions"] as JArray;
                long localCellId = session["localPlayerCellId"]?.Value<long>()
                                ?? session["localPlayerCellId"]?.Value<int>()
                                ?? -1;

                // 诊断（确认后可删或降为 Verbose）
                Debug.WriteLine($"[自动预选诊断] localPlayerCellId={localCellId}, hasSimultaneousPicks={session["hasSimultaneousPicks"]}, timer.phase={session["timer"]?["phase"]}");
                if (actions != null)
                {
                    foreach (var group in actions)
                    {
                        var arr = group as JArray;
                        if (arr == null) continue;
                        foreach (var a in arr)
                        {
                            Debug.WriteLine(
                                $"[自动预选诊断] act id={a["id"]} type={a["type"]} actor={a["actorCellId"]} " +
                                $"completed={a["completed"]} inProgress={a["isInProgress"]} champ={a["championId"]}");
                        }
                    }
                }

                // 找自己未完成的 pick（兼容 actorCellId 为 int/long）
                var myPickAction = actions
                    .SelectMany(g => g as JArray ?? new JArray())
                    .FirstOrDefault(a =>
                    {
                        long actor = a["actorCellId"]?.Value<long>()
                                  ?? a["actorCellId"]?.Value<int>()
                                  ?? -999;
                        return actor == localCellId
                            && a["type"]?.ToString() == "pick"
                            && a["completed"]?.Value<bool>() != true;
                    });

                if (myPickAction == null)
                {
                    Debug.WriteLine("[自动预选] 当前没有自己的 pick action（可能未轮到）");
                    return false;
                }

                var actionId = myPickAction["id"]?.Value<int>() ?? 0;
                if (actionId == 0)
                    return false;

                // session 里已禁/已选是「会话 ID」（经典为 600xx）
                var bannedSessionIds = GetBannedAndSelectedChampions(session);

                // 预选列表是现代 ID → 转成会话 ID 再判断是否被占
                bool IsUnavailable(int modernId)
                {
                    int sid = ToSessionChampionId(modernId, queueId);
                    return bannedSessionIds.Contains(sid) || bannedSessionIds.Contains(modernId);
                }

                var availableHero = preSelectedHeroes.FirstOrDefault(h => !IsUnavailable(h.ChampionId));
                if (availableHero == null)
                {
                    Debug.WriteLine("[自动预选] 预选列表中没有当前可用英雄");
                    return false;
                }

                int sessionChampId = ToSessionChampionId(availableHero.ChampionId, queueId);
                int currentHoverId = myPickAction["championId"]?.Value<int>() ?? 0;

                if (currentHoverId == sessionChampId)
                {
                    Debug.WriteLine($"[自动预选] 已 hover: {availableHero.ChampionName} (sessionId={sessionChampId})");
                    return false;
                }

                // 关键：经典模式发 60000+现代ID，其它模式发现代 ID
                var patchBody = new JObject { ["championId"] = sessionChampId };
                var jsonContent = new StringContent(patchBody.ToString(), Encoding.UTF8, "application/json");
                var url = $"{apiPrefix}/session/actions/{actionId}";

                var response = await _client.PatchAsync(url, jsonContent);
                var body = await response.Content.ReadAsStringAsync();
                Debug.WriteLine(
                    $"[自动预选] PATCH {url} modernId={availableHero.ChampionId} sessionId={sessionChampId} " +
                    $"({availableHero.ChampionName}) => {(int)response.StatusCode} {response.StatusCode}, body={body}");

                // 回读会话确认是否真的换上（不单信 HTTP 状态码）
                await Task.Delay(150);
                var (session2, _) = await ResolveChampSelectSessionAsync();
                var me2 = (session2?["myTeam"] as JArray)?.FirstOrDefault(p =>
                {
                    long cell = p["cellId"]?.Value<long>() ?? p["cellId"]?.Value<int>() ?? -999;
                    return cell == localCellId;
                });

                int intent = me2?["championPickIntent"]?.Value<int>() ?? 0;
                int locked = me2?["championId"]?.Value<int>() ?? 0;
                // 也看 action 上的 championId
                int actionChamp = 0;
                try
                {
                    var actions2 = session2?["actions"] as JArray;
                    var act2 = actions2?
                        .SelectMany(g => g as JArray ?? new JArray())
                        .FirstOrDefault(a => (a["id"]?.Value<int>() ?? -1) == actionId);
                    actionChamp = act2?["championId"]?.Value<int>() ?? 0;
                }
                catch { /* ignore */ }

                bool reallyOk =
                    intent == sessionChampId || locked == sessionChampId || actionChamp == sessionChampId ||
                    intent == availableHero.ChampionId || locked == availableHero.ChampionId || actionChamp == availableHero.ChampionId;

                Debug.WriteLine(
                    $"[自动预选] 回读 intent={intent}, locked={locked}, actionChamp={actionChamp}, 目标sessionId={sessionChampId}, reallyOk={reallyOk}");

                if (reallyOk)
                {
                    Debug.WriteLine($"[自动预选] 会话已确认换上: {availableHero.ChampionName}");
                    return true;
                }

                // 现代接口 2xx 但未换上：经典模式再强制试 legacy 一次（同一 sessionChampId）
                if (IsClassicQueue(queueId) && apiPrefix.Contains("lol-champ-select/v1") && !apiPrefix.Contains("legacy"))
                {
                    var legacyUrl = $"/lol-champ-select-legacy/v1/session/actions/{actionId}";
                    var legacyResp = await _client.PatchAsync(legacyUrl, jsonContent);
                    var legacyBody = await legacyResp.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[自动预选] 强制 legacy PATCH {legacyUrl} => {(int)legacyResp.StatusCode}, body={legacyBody}");

                    await Task.Delay(150);
                    var (session3, _) = await ResolveChampSelectSessionAsync();
                    var me3 = (session3?["myTeam"] as JArray)?.FirstOrDefault(p =>
                    {
                        long cell = p["cellId"]?.Value<long>() ?? p["cellId"]?.Value<int>() ?? -999;
                        return cell == localCellId;
                    });
                    int intent3 = me3?["championPickIntent"]?.Value<int>() ?? 0;
                    int locked3 = me3?["championId"]?.Value<int>() ?? 0;
                    if (intent3 == sessionChampId || locked3 == sessionChampId)
                    {
                        Debug.WriteLine($"[自动预选] legacy 会话已确认换上: {availableHero.ChampionName}");
                        return true;
                    }
                }

                // HTTP 成功但会话未变：返回 false，让 AutoPickService 继续轮询，不要标记 _hasAutoPreliminated
                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine("[自动预选] HTTP 成功但会话未换上，视为失败（可能 ID/通道仍不对）");
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[自动预选] 执行异常: {ex.Message}");
                return false;
            }
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
                return;

            try
            {
                var session = await _gameflowService.GetChampSelectSessionAsync();
                if (session == null) return;

                var queueId = session["queueId"]?.Value<int>() ?? 0;

                // 只有非ARAM模式才返回，450=大乱斗、2400=海克斯乱斗、900=无限乱斗
                if (queueId != 450 && queueId != 2400 && queueId != 900) return; 

                var myCellId = session["localPlayerCellId"]?.Value<int>() ?? -1;
                if (myCellId == -1) return;

                var myTeam = session["myTeam"] as JArray ?? new JArray();
                var myPlayer = myTeam.FirstOrDefault(p => p["cellId"]?.Value<int>() == myCellId);
                var currentChampId = myPlayer?["championId"]?.Value<int>() ?? 0;

                if (currentChampId == 0) return;

                var currentPriority = preSelectedHeroes
                    .FirstOrDefault(h => h.ChampionId == currentChampId)
                    ?.Priority ?? int.MaxValue;

                // bench 中所有可用英雄
                var benchChampions = session["benchChampions"] as JArray ?? new JArray();
                var availableChampIds = new HashSet<int>();

                foreach (var item in benchChampions)
                {
                    var id = item["championId"]?.Value<int>() ?? 0;
                    if (id > 0) availableChampIds.Add(id);
                }

                // 找出 bench 中优先级最高的预选英雄
                var bestAvailable = preSelectedHeroes
                    .Where(h => availableChampIds.Contains(h.ChampionId))
                    .OrderBy(h => h.Priority)
                    .FirstOrDefault();

                if (bestAvailable == null) return;

                // 只有当 bench 中的最高优先级严格高于当前持有，才抢
                if (bestAvailable.Priority < currentPriority)
                {
                    var response = await _client.PostAsync($"/lol-champ-select/v1/session/bench/swap/{bestAvailable.ChampionId}");

                    if (response.IsSuccessStatusCode)
                    {
                        await Task.Delay(100); // 稍微延时，避免API过于频繁
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ARAM 贪婪抢] 异常: {ex.Message}");
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
                if (champId > 0) bannedChampIds.Add(champId);
            }

            // 获取我方已选英雄
            var myTeam = session["myTeam"] as JArray ?? new JArray();
            foreach (var player in myTeam)
            {
                var champId = player["championId"]?.Value<int>() ?? 0;
                if (champId > 0) bannedChampIds.Add(champId);
            }

            return bannedChampIds;
        }

        /// <summary>
        /// 选人阶段切换皮肤：PATCH /lol-champ-select/v1/session/my-selection
        /// </summary>
        public async Task<bool> TryApplyPreferredSkinAsync(
            int championId,
            int skinId,
            int? lastAppliedSkinId = null)
        {
            if (championId <= 0 || skinId <= 0 || skinId % 1000 == 0)
                return false;
            if (lastAppliedSkinId == skinId)
                return false;

            try
            {
                var body = new JObject { ["selectedSkinId"] = skinId };
                var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                var resp = await _client.PatchAsync(
                    "/lol-champ-select/v1/session/my-selection", content);

                Debug.WriteLine($"[自动皮肤] champ={championId} skin={skinId} status={resp.StatusCode}");
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[自动皮肤] {ex.Message}");
                return false;
            }
        }
    }
}