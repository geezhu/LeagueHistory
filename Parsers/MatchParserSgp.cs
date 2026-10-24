using System.Diagnostics;
using League.Controls;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Parsers
{
    public class MatchParserSgp
    {
        private class ResourceInfo
        {
            public Image Image { get; set; }

            public string Name { get; set; }

            public string Description { get; set; }
        }

        private const int MAX_CONCURRENT_PARSING = 10; // 增加到10

        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(
            MAX_CONCURRENT_PARSING,
            MAX_CONCURRENT_PARSING
        );

        public event Action<string> PlayerIconClicked;

        // 修改解析方法，使用 SemaphoreSlim 替代 lock
        public async Task<Panel> ParseGameToPanelFromSgpAsync(
            JObject game,
            string summonerId,
            string gameName,
            string tagLine,
            int index
        )
        {
            // 使用 SemaphoreSlim 控制并发，而不是直接跳过
            await _semaphore.WaitAsync();
            try
            {
                if (game == null)
                {
                    return CreateEmptyPanel();
                }

                MatchInfo matchInfo = await ParseGameDataLightweight(game, summonerId, gameName, tagLine, index);
                if (matchInfo == null)
                {
                    return CreateEmptyPanel();
                }

                MatchListPanel panel = new MatchListPanel(matchInfo);
                panel.PlayerIconClicked += delegate(string summonerId)
                {
                    PlayerIconClicked?.Invoke(summonerId);
                };
                return panel;
            }
            catch (Exception ex)
            {
                AppLog.Shared.Warn(LogCategory.Data, "解析异常", ex);
                return CreateEmptyPanel();
            }
            finally
            {
                game?.RemoveAll();
                game = null;
                _semaphore.Release();
            }
        }

        private async Task<MatchInfo> ParseGameDataLightweight(
            JObject game,
            string summonerId,
            string gameName,
            string tagLine,
            int index
        )
        {
            var (gameJson, participants) = ParseJsonStructure(game);
            if (participants == null || participants.Count == 0)
            {
                return null;
            }
            List<JObject> allParticipants = participants.Cast<JObject>().ToList();
            JObject selfParticipant = FindSelfParticipant(allParticipants, summonerId, gameName, tagLine);
            if (selfParticipant == null)
            {
                return null;
            }
            long gameId = ((long?)gameJson["gameId"]).GetValueOrDefault();
            // Without the player's puuid the row cannot be told apart from the others' (the scores, the detail's own
            // row), so it is left out as a placeholder.
            if (
                ExtractBasicMatchData(gameJson, selfParticipant, participants) is not { } basic
                || string.IsNullOrEmpty(basic.currPuuid)
            )
            {
                return null;
            }

            var (damageRank, goldRank, kpRank) = CalculateRanks(
                participants,
                selfParticipant,
                basic.damageToChampions,
                basic.goldEarned,
                basic.killParticipation
            );
            var (_, damageTakenText) = ExtractDamageTakenData(selfParticipant, participants);
            var resources = await LoadEssentialResourcesSimple(selfParticipant);

            // The list row shows only the player's own augments. The other nine players' (up to 54 more icons per game,
            // each rendered with its rarity frame) are left to the detail view, which loads every row's missing
            // augments before drawing it (DetailedViewLayoutManager.RetryMissingAugmentsAsync).
            var selfAugments = await LoadAugmentsAsync(ExtractAugments(selfParticipant));
            var allPlayersAugments = new Dictionary<string, RuneInfo[]>();
            if (!string.IsNullOrEmpty(basic.currPuuid))
            {
                allPlayersAugments[basic.currPuuid] = selfAugments;
            }

            MatchInfo matchInfo = CreateMatchInfo(
                index,
                gameId,
                basic.currPuuid,
                basic.currSummonerId,
                basic.isWin,
                basic.teamId,
                basic.duration,
                basic.createTime,
                basic.kills,
                basic.deaths,
                basic.assists,
                $"{basic.damageToChampions / 1000f:0.#}K |{damageRank}",
                $"{basic.goldEarned / 1000f:0.#}K |{goldRank}",
                $"{basic.killParticipation:P0} |{kpRank}",
                damageTakenText,
                basic.mode,
                basic.queueId,
                resources.champIcon,
                resources.champName,
                resources.spellImg1,
                resources.spellImg2,
                resources.primaryRunes,
                resources.secondaryRunes,
                resources.statRunes,
                resources.items,
                gameName.Contains("#") ? gameName.Substring(0, gameName.IndexOf('#')) : gameName,
                game,
                allParticipants,
                selfAugments,
                allPlayersAugments
            );
            matchInfo.SpellNames[0] = resources.spellName1;
            matchInfo.RawGameData = (JObject)(gameJson?["info"] as JObject ?? gameJson ?? game).DeepClone();
            matchInfo.SpellDescriptions[0] = resources.spellDesc1;
            matchInfo.SpellNames[1] = resources.spellName2;
            matchInfo.SpellDescriptions[1] = resources.spellDesc2;
            await LoadTeamMembers(matchInfo, allParticipants, summonerId, gameName, tagLine);
            return matchInfo;
        }

        // 修改海克斯符文加载方法，确保正确处理空值
        private async Task<RuneInfo[]> LoadAugmentsAsync(int[] augmentIds)
        {
            if (augmentIds == null || augmentIds.Length == 0)
            {
                return Array.Empty<RuneInfo>();
            }

            using var semaphore = new SemaphoreSlim(3);
            var tasks = augmentIds
                .Where(id => id > 0)
                .Select(async id =>
                {
                    var rune = new RuneInfo
                    {
                        id = id,
                        name = $"海克斯 {id}",
                        longDesc = "图片加载失败，打开详情时将重试",
                    };
                    await semaphore.WaitAsync();
                    try
                    {
                        var (img, name, desc) = await FormMain.Globals.resLoading.GetAugmentInfoAsync(id);
                        rune.Icon = img;
                        rune.name = name;
                        rune.longDesc = img == null ? desc + "\n图片未加载，打开详情时将重试" : desc;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Shared.Warn(LogCategory.Data, $"加载海克斯符文 {id} 失败", ex);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                    return rune; // Keep its ID and position even when the image is missing.
                });
            return await Task.WhenAll(tasks);
        }

        // 修改 ExtractAugments 方法，确保正确处理空值
        private int[] ExtractAugments(JObject participant)
        {
            if (participant == null)
            {
                return Array.Empty<int>();
            }

            int[] augIds = new int[]
            {
                participant["playerAugment1"]?.Value<int>() ?? 0,
                participant["playerAugment2"]?.Value<int>() ?? 0,
                participant["playerAugment3"]?.Value<int>() ?? 0,
                participant["playerAugment4"]?.Value<int>() ?? 0,
                participant["playerAugment5"]?.Value<int>() ?? 0,
                participant["playerAugment6"]?.Value<int>() ?? 0,
            };

            return augIds.Where(x => x > 0).ToArray();
        }

        private (JObject gameJson, JArray participants) ParseJsonStructure(JObject game)
        {
            JObject gameJson = null;
            JArray participants = null;
            if (game["json"] != null)
            {
                gameJson = game["json"] as JObject;
                participants = gameJson?["participants"] as JArray ?? gameJson?["info"]?["participants"] as JArray;
            }
            else
            {
                gameJson = game;
                participants = game["participants"] as JArray ?? game["info"]?["participants"] as JArray;
            }
            return (gameJson, participants);
        }

        private JObject FindSelfParticipant(
            List<JObject> participants,
            string summonerId,
            string gameName,
            string tagLine
        )
        {
            if (participants == null || participants.Count == 0)
            {
                return null;
            }

            // 方案1：优先使用summonerId进行精确匹配（最可靠）
            if (!string.IsNullOrEmpty(summonerId))
            {
                var self = participants.FirstOrDefault(
                    (p) => string.Equals(p["summonerId"]?.ToString(), summonerId, StringComparison.OrdinalIgnoreCase)
                );

                if (self != null)
                {
                    return self;
                }

                Debug.WriteLine($"⚠ SummonerId匹配失败: {summonerId}");
            }

            // 方案2：传统名称匹配（作为fallback），只在summonerId匹配失败时使用
            var byName = FindParticipantByName(participants, gameName, tagLine);
            if (byName != null)
            {
                return byName;
            }

            LogUnmatchedParticipants(participants, summonerId, gameName, tagLine);

            // 作为最后的手段，返回第一个参与者
            Debug.WriteLine("⚠ 使用第一个玩家代替以确保解析不中断。");
            return participants.FirstOrDefault();
        }

        /// <summary>
        /// 依次按完整 Riot ID、Riot 游戏名、召唤师名精确匹配，最后按名称包含关系模糊匹配。
        /// </summary>
        private static JObject FindParticipantByName(List<JObject> participants, string gameName, string tagLine)
        {
            var self = participants.FirstOrDefault(
                (p) =>
                    string.Equals(p["riotIdGameName"]?.ToString(), gameName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p["riotIdTagline"]?.ToString(), tagLine, StringComparison.OrdinalIgnoreCase)
            );
            if (self != null)
            {
                Debug.WriteLine($"⚠ SummonerId匹配失败，通过Riot ID找到玩家: {gameName}#{tagLine}");
                return self;
            }

            self = participants.FirstOrDefault(
                (p) => string.Equals(p["riotIdGameName"]?.ToString(), gameName, StringComparison.OrdinalIgnoreCase)
            );
            if (self != null)
            {
                Debug.WriteLine($"⚠ SummonerId匹配失败，通过Riot GameName找到玩家: {gameName}");
                return self;
            }

            self = participants.FirstOrDefault(
                (p) => string.Equals(p["summonerName"]?.ToString(), gameName, StringComparison.OrdinalIgnoreCase)
            );
            if (self != null)
            {
                Debug.WriteLine($"⚠ SummonerId匹配失败，通过SummonerName找到玩家: {gameName}");
                return self;
            }

            self = participants.FirstOrDefault(
                (p) =>
                    (p["riotIdGameName"]?.ToString() ?? "").IndexOf(gameName, StringComparison.OrdinalIgnoreCase) >= 0
                    || (p["summonerName"]?.ToString() ?? "").IndexOf(gameName, StringComparison.OrdinalIgnoreCase) >= 0
            );
            if (self != null)
            {
                Debug.WriteLine($"⚠ SummonerId匹配失败，通过模糊匹配找到玩家: {gameName}");
            }
            return self;
        }

        // 如果所有匹配都失败，记录详细信息用于调试
        private static void LogUnmatchedParticipants(
            List<JObject> participants,
            string summonerId,
            string gameName,
            string tagLine
        )
        {
            Debug.WriteLine($"❌ 所有匹配方法都失败！");
            Debug.WriteLine($"查找条件: summonerId={summonerId}, gameName={gameName}, tagLine={tagLine}");
            Debug.WriteLine($"参与者数量: {participants.Count}");
            Debug.WriteLine($"参与者列表:");

            for (int i = 0; i < participants.Count; i++)
            {
                var p = participants[i];
                string riotName = p["riotIdGameName"]?.ToString();
                string riotTag = p["riotIdTagline"]?.ToString();
                string summonerName = p["summonerName"]?.ToString();
                string participantSummonerId = p["summonerId"]?.ToString();
                string puuid = p["puuid"]?.ToString();

                Debug.WriteLine(
                    $"  [{i}] RiotID: {riotName}#{riotTag} | "
                        + $"SummonerName: {summonerName} | "
                        + $"SummonerId: {participantSummonerId} | "
                        + $"PUUID: {puuid?.Substring(0, Math.Min(8, puuid?.Length ?? 0))}..."
                );
            }
        }

        private (
            string currPuuid,
            string currSummonerId,
            bool isWin,
            int teamId,
            int duration,
            DateTime createTime,
            int kills,
            int deaths,
            int assists,
            int goldEarned,
            int damageToChampions,
            double killParticipation,
            string mode,
            string queueId
        )? ExtractBasicMatchData(JObject gameJson, JObject selfParticipant, JArray participants)
        {
            try
            {
                JToken info = gameJson["info"] ?? gameJson;
                string currPuuid = (string?)selfParticipant["puuid"];
                string currSummonerId = (string?)selfParticipant["summonerId"];
                bool isWin = selfParticipant["win"]?.Value<bool>() ?? false;
                int teamId = selfParticipant["teamId"]?.Value<int>() ?? 100;
                int duration = info["gameDuration"]?.Value<int>() ?? 0;
                long gameStart = info["gameStartTimestamp"]?.Value<long>() ?? info["gameCreation"]?.Value<long>() ?? 0;
                DateTime createTime =
                    gameStart > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(gameStart).LocalDateTime
                        : DateTime.MinValue;
                int kills = selfParticipant["kills"]?.Value<int>() ?? 0;
                int deaths = selfParticipant["deaths"]?.Value<int>() ?? 0;
                int assists = selfParticipant["assists"]?.Value<int>() ?? 0;
                int goldEarned = selfParticipant["goldEarned"]?.Value<int>() ?? 0;
                int damageToChampions = selfParticipant["totalDamageDealtToChampions"]?.Value<int>() ?? 0;
                double killParticipation = (
                    selfParticipant["challenges"]?["killParticipation"]?.Value<double>()
                ).GetValueOrDefault();
                var (mode, queueId) = ExtractGameMode(info as JObject);
                return (
                    currPuuid,
                    currSummonerId,
                    isWin,
                    teamId,
                    duration,
                    createTime,
                    kills,
                    deaths,
                    assists,
                    goldEarned,
                    damageToChampions,
                    killParticipation,
                    mode,
                    queueId
                );
            }
            catch (Exception ex)
            {
                AppLog.Shared.Warn(LogCategory.Data, "提取基础数据异常", ex);
                return null;
            }
        }

        /// <summary>官方队列对应的模式名；未收录时为 null。</summary>
        public static string? QueueModeName(int queue) => QueueModes.GetValueOrDefault(queue);

        /// <summary>
        /// 官方队列对应的模式名。
        /// </summary>
        private static readonly Dictionary<int, string> QueueModes = new()
        {
            [420] = "单双排",
            [440] = "灵活排位",
            [400] = "匹配征召",
            [430] = "匹配盲选",
            [450] = "大乱斗",
            [900] = "无限乱斗",
            [1900] = "自选无限火力",
            [480] = "快速模式",
            [1700] = "斗魂竞技场",
            [2400] = "海克斯乱斗",
            [2450] = "经典海克斯乱斗",
            [1020] = "克隆大作战",
            [1300] = "极限闪击",
            [1400] = "终极魔典",
            [700] = "冠军杯赛",
            [830] = "人机入门",
            [840] = "人机新手",
            [850] = "人机中等",
            [870] = "人机入门",
            [880] = "人机新手",
            [890] = "人机中等",
            [2000] = "新手教程1",
            [2010] = "新手教程2",
            [2020] = "新手教程3",
            [1090] = "云顶匹配",
            [1100] = "云顶排位",
            [1130] = "云顶疾速",
            [1160] = "云顶双人",
            [0] = "自定义",
            [3100] = "自定义峡谷",
            [3140] = "训练模式",
            [3270] = "自定义海克斯",
            [4310] = "经典峡谷",
            [4320] = "经典人机",
            [950] = "末日人机",
            [960] = "末日人机",
        };

        /// <summary>
        /// 战绩列表中显示模式信息：自定义优先，其次官方队列，最后按 gameMode 兜底。
        /// </summary>
        private (string mode, string queueId) ExtractGameMode(JObject gameJson)
        {
            int queue = gameJson["queueId"]?.Value<int>() ?? gameJson["info"]?["queueId"]?.Value<int>() ?? 0;
            string queueId = queue > 0 ? $"q_{queue}" : "";
            string gameMode = gameJson["gameMode"]?.ToString() ?? gameJson["info"]?["gameMode"]?.ToString();
            string gameType = gameJson["gameType"]?.ToString() ?? gameJson["info"]?["gameType"]?.ToString();

            if (gameType == "CUSTOM_GAME")
            {
                return (GetCustomGameMode(queue, gameMode), queueId);
            }

            if (QueueModes.TryGetValue(queue, out string? mode))
            {
                return (mode, queueId);
            }

            return (GetModeFromGameMode(gameMode, queue), queueId);
        }

        private static string GetCustomGameMode(int queue, string? gameMode)
        {
            if (queue == 4310 || string.Equals(gameMode, "JADE", StringComparison.OrdinalIgnoreCase))
            {
                return "经典峡谷";
            }

            if (queue == 3270 || string.Equals(gameMode, "KIWI", StringComparison.OrdinalIgnoreCase))
            {
                return "自定义海克斯";
            }

            if (string.Equals(gameMode, "PRACTICETOOL", StringComparison.OrdinalIgnoreCase))
            {
                return "训练模式";
            }

            return gameMode?.ToLower() switch
            {
                "classic" => "自定义峡谷",
                "aram" => "自定义大乱斗",
                "cherry" => "自定义海克斯",
                _ => "自定义",
            };
        }

        // queue 0 已在 QueueModes 中映射为「自定义」，这里只处理未收录的队列。
        private static string GetModeFromGameMode(string? gameMode, int queue)
        {
            if (string.IsNullOrEmpty(gameMode))
            {
                return $"未知模式({queue})";
            }

            return gameMode.ToLower() switch
            {
                "classic" => "召唤师峡谷",
                "aram" => "大乱斗",
                "cherry" => "斗魂竞技场",
                "jade" => "经典峡谷",
                "urf" => "无限乱斗",
                "tutorial" => "新手教程",
                _ => $"未知模式({queue})",
            };
        }

        private (int damageRank, int goldRank, int kpRank) CalculateRanks(
            JArray participants,
            JObject selfParticipant,
            int selfDamage,
            int selfGold,
            double selfKP
        )
        {
            if (participants == null)
            {
                return (damageRank: 1, goldRank: 1, kpRank: 1);
            }
            var allStats = participants
                .Cast<JObject>()
                .Select(
                    delegate(JObject p)
                    {
                        int damage = p["totalDamageDealtToChampions"]?.Value<int>() ?? 0;
                        int gold = p["goldEarned"]?.Value<int>() ?? 0;
                        double num = 0.0;
                        JToken jToken = p["challenges"];
                        if (jToken != null)
                        {
                            num = jToken["killParticipation"]?.Value<double>() ?? 0.0;
                        }
                        if (num == 0.0)
                        {
                            num = p["killParticipation"]?.Value<double>() ?? 0.0;
                        }
                        return new
                        {
                            Damage = damage,
                            Gold = gold,
                            KP = num,
                        };
                    }
                )
                .ToList();
            int damageRank = GetRankInt(allStats.Select(s => s.Damage).ToList(), selfDamage);
            int goldRank = GetRankInt(allStats.Select(s => s.Gold).ToList(), selfGold);
            int kpRank = GetRankDouble(allStats.Select(s => s.KP).ToList(), selfKP);
            return (damageRank, goldRank, kpRank);
        }

        private int GetRankInt(List<int> allValues, int currentValue)
        {
            List<int> sorted = allValues.OrderByDescending((v) => v).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (currentValue == sorted[i])
                {
                    return i + 1;
                }
            }
            return sorted.Count;
        }

        private int GetRankDouble(List<double> allValues, double currentValue, double eps = 1E-06)
        {
            List<double> sorted = allValues.OrderByDescending((v) => v).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (Math.Abs(currentValue - sorted[i]) <= eps)
                {
                    return i + 1;
                }
            }
            for (int i2 = 0; i2 < sorted.Count; i2++)
            {
                if (currentValue > sorted[i2])
                {
                    return i2 + 1;
                }
            }
            return sorted.Count;
        }

        private int GetRank<T>(List<T> allValues, T currentValue)
            where T : IComparable
        {
            List<T> sorted = allValues.OrderByDescending((v) => v).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (currentValue.CompareTo(sorted[i]) == 0)
                {
                    return i + 1;
                }
            }
            return sorted.Count;
        }

        private (int damageTaken, string damageTakenText) ExtractDamageTakenData(
            JObject selfParticipant,
            JArray participants
        )
        {
            try
            {
                int damageTaken = selfParticipant["totalDamageTaken"]?.Value<int>() ?? 0;
                int damageTakenRank = CalculateDamageTakenRank(participants, damageTaken);
                string damageTakenText = $"{damageTaken / 1000f:0.#}K |{damageTakenRank}";
                return (damageTaken, damageTakenText);
            }
            catch (Exception ex)
            {
                AppLog.Shared.Warn(LogCategory.Data, "提取承伤数据异常", ex);
                return (damageTaken: 0, damageTakenText: "0K |1");
            }
        }

        private int CalculateDamageTakenRank(JArray participants, int selfDamageTaken)
        {
            if (participants == null)
            {
                return 1;
            }
            List<int> allDamageTaken = (
                from JObject p in participants
                select p["totalDamageTaken"]?.Value<int>() ?? 0
            ).ToList();
            return GetRank(allDamageTaken, selfDamageTaken);
        }

        private async Task<RuneInfo[]> LoadStatRunesAsync(JObject participant)
        {
            JToken perks = participant["perks"];
            if (perks == null)
            {
                return new RuneInfo[3];
            }
            JToken statPerks = perks["statPerks"];
            if (statPerks == null)
            {
                return new RuneInfo[3];
            }
            int[] statRuneIds = new int[3]
            {
                statPerks["offense"]?.Value<int>() ?? 0,
                statPerks["flex"]?.Value<int>() ?? 0,
                statPerks["defense"]?.Value<int>() ?? 0,
            };
            IEnumerable<Task<RuneInfo>> statRuneTasks = statRuneIds.Select(
                async delegate(int runeId, int index)
                {
                    if (runeId <= 0)
                    {
                        return new RuneInfo();
                    }
                    var (image, name, description) = await FormMain.Globals.resLoading.GetRuneInfoAsync(runeId);
                    return new RuneInfo
                    {
                        id = runeId, // 或 RuneId = runeId
                        RuneId = runeId,
                        Icon = image,
                        name = name, // 补上小写
                        Name = name,
                        longDesc = description,
                        Description = description,
                    };
                }
            );
            return await Task.WhenAll(statRuneTasks);
        }

        private async Task<(
            Image champIcon,
            string champName,
            Image spellImg1,
            Image spellImg2,
            string spellName1,
            string spellDesc1,
            string spellName2,
            string spellDesc2,
            RuneInfo[] primaryRunes,
            RuneInfo[] secondaryRunes,
            RuneInfo[] statRunes,
            Item[] items
        )> LoadEssentialResourcesSimple(JObject participant)
        {
            int championId = participant["championId"]?.Value<int>() ?? 0;
            int spell1Id = participant["spell1Id"]?.Value<int>() ?? 0;
            int spell2Id = participant["spell2Id"]?.Value<int>() ?? 0;
            Task<(Image image, string name, string description)> championTask =
                FormMain.Globals.resLoading.GetChampionInfoAsync(championId);
            Task<(Image image, string name, string description)> spell1Task =
                FormMain.Globals.resLoading.GetSpellInfoAsync(spell1Id);
            Task<(Image image, string name, string description)> spell2Task =
                FormMain.Globals.resLoading.GetSpellInfoAsync(spell2Id);
            Task<(RuneInfo[] Primary, RuneInfo[] Secondary)> runesTask = LoadRunesSimpleAsync(participant);
            Task<RuneInfo[]> statRunesTask = LoadStatRunesAsync(participant);
            Task<Item[]> itemsTask = LoadItemsSimpleAsync(participant);
            await Task.WhenAll(championTask, spell1Task, spell2Task, runesTask, statRunesTask, itemsTask);
            var (champIcon, champName, _) = await championTask;
            var (spellImg1, spellName1, spellDesc1) = await spell1Task;
            var (spellImg2, spellName2, spellDesc2) = await spell2Task;
            RuneInfo[] primaryRunes;
            RuneInfo[] secondaryRunes;
            (primaryRunes, secondaryRunes) = await runesTask;
            return (
                champIcon,
                champName,
                spellImg1,
                spellImg2,
                spellName1,
                spellDesc1,
                spellName2,
                spellDesc2,
                primaryRunes,
                secondaryRunes,
                statRunes: await statRunesTask,
                items: await itemsTask
            );
        }

        private async Task<(RuneInfo[] Primary, RuneInfo[] Secondary)> LoadRunesSimpleAsync(JObject participant)
        {
            JToken perks = participant["perks"];
            if (perks == null)
            {
                return (Primary: null, Secondary: null);
            }
            if (!(perks["styles"] is JArray styles))
            {
                return (Primary: null, Secondary: null);
            }
            JToken primaryStyle = styles.FirstOrDefault((s) => s["description"]?.ToString() == "primaryStyle");
            return new ValueTuple<RuneInfo[], RuneInfo[]>(
                item2: await LoadRuneStyleAsync(
                    styles.FirstOrDefault((s) => s["description"]?.ToString() == "subStyle"),
                    2
                ),
                item1: await LoadRuneStyleAsync(primaryStyle, 4)
            );
        }

        private async Task<RuneInfo[]> LoadRuneStyleAsync(JToken style, int maxCount)
        {
            if (style == null)
            {
                return null;
            }
            if (!(style["selections"] is JArray selections))
            {
                return null;
            }
            IEnumerable<Task<RuneInfo>> runeTasks = selections
                .Cast<JObject>()
                .Take(maxCount)
                .Select(
                    async delegate(JObject selection)
                    {
                        int runeId = selection["perk"]?.Value<int>() ?? 0;
                        if (runeId <= 0)
                        {
                            return null;
                        }
                        var (image, name, description) = await FormMain.Globals.resLoading.GetRuneInfoAsync(runeId);
                        return new RuneInfo
                        {
                            id = runeId, // 或 RuneId = runeId
                            RuneId = runeId,
                            Icon = image,
                            name = name, // 补上小写
                            Name = name,
                            longDesc = description,
                            Description = description,
                        };
                    }
                );
            return (await Task.WhenAll(runeTasks)).Where((r) => r != null).ToArray();
        }

        private async Task<Item[]> LoadItemsSimpleAsync(JObject participant)
        {
            IEnumerable<Task<Item>> itemTasks = Enumerable
                .Range(0, 6)
                .Select(
                    async delegate(int i)
                    {
                        int itemId = participant[$"item{i}"]?.Value<int>() ?? 0;
                        if (itemId <= 0)
                        {
                            return null;
                        }
                        var (image, name, desc) = await FormMain.Globals.resLoading.GetItemInfoAsync(itemId);
                        return new Item
                        {
                            Id = itemId,
                            Icon = image,
                            Name = name,
                            Description = desc,
                        };
                    }
                );
            return (await Task.WhenAll(itemTasks)).Where((item) => item != null).ToArray();
        }

        private async Task LoadTeamMembers(
            MatchInfo matchInfo,
            List<JObject> participants,
            string summonerId,
            string selfGameName,
            string selfTagLine
        )
        {
            List<PlayerInfo> blueTeam = new List<PlayerInfo>();
            List<PlayerInfo> redTeam = new List<PlayerInfo>();

            // The ten icons are looked up together rather than one after another; the results keep the roster order.
            var players = await Task.WhenAll(
                participants.Select(participant => CreatePlayerInfo(participant, summonerId, selfGameName, selfTagLine))
            );
            foreach (PlayerInfo player in players)
            {
                if (player != null)
                {
                    if (player.TeamId == 100)
                    {
                        blueTeam.Add(player);
                    }
                    else if (player.TeamId == 200)
                    {
                        redTeam.Add(player);
                    }
                }
            }

            matchInfo.BlueTeam = blueTeam.OrderBy((p) => p.FullName).ToList();
            matchInfo.RedTeam = redTeam.OrderBy((p) => p.FullName).ToList();
        }

        // 更新CreatePlayerInfo方法签名
        private async Task<PlayerInfo> CreatePlayerInfo(
            JObject participant,
            string summonerId,
            string selfGameName,
            string selfTagLine
        )
        {
            try
            {
                string fullName = GetDisplayNameFromParticipant(participant);
                int champId = participant["championId"]?.Value<int>() ?? 0;
                int teamId = participant["teamId"]?.Value<int>() ?? 100;
                long participantSummonerId = participant["summonerId"]?.Value<long>() ?? 0;

                var tuple = await FormMain.Globals.resLoading.GetChampionInfoAsync(champId);
                var (champIcon, _, _) = tuple;

                // 使用新的CheckIsSelf方法
                bool isSelf = CheckIsSelf(participant, summonerId, selfGameName, selfTagLine);

                return new PlayerInfo
                {
                    FullName = fullName,
                    Avatar = champIcon,
                    SummonerId = participantSummonerId,
                    TeamId = teamId,
                    IsSelf = isSelf,
                };
            }
            catch (Exception ex)
            {
                AppLog.Shared.Warn(LogCategory.Data, "创建玩家信息异常", ex);
                return null;
            }
        }

        private string GetDisplayNameFromParticipant(JObject p)
        {
            if (p == null)
            {
                return "Unknown";
            }
            string riotName = p["riotIdGameName"]?.ToString();
            string summonerName = p["summonerName"]?.ToString();
            if (!string.IsNullOrWhiteSpace(riotName))
            {
                return riotName;
            }
            if (!string.IsNullOrWhiteSpace(summonerName))
            {
                return summonerName;
            }
            return "Unknown";
        }

        private bool CheckIsSelf(JObject p, string summonerId, string gameName, string selfTagLine)
        {
            // 优先使用summonerId检查
            if (!string.IsNullOrEmpty(summonerId))
            {
                string participantSummonerId = p["summonerId"]?.ToString();
                if (!string.IsNullOrEmpty(participantSummonerId))
                {
                    return string.Equals(participantSummonerId, summonerId, StringComparison.OrdinalIgnoreCase);
                }
            }

            // 如果summonerId不可用，fallback到名称匹配
            string playerRiotName = p["riotIdGameName"]?.ToString();
            string playerSummoner = p["summonerName"]?.ToString();

            if (!string.IsNullOrEmpty(playerRiotName))
            {
                return string.Equals(playerRiotName, gameName, StringComparison.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrEmpty(playerSummoner))
            {
                return string.Equals(playerSummoner, gameName, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private MatchInfo CreateMatchInfo(
            int index,
            long gameId,
            string currPuuid,
            string currSummonerId,
            bool isWin,
            int teamId,
            int duration,
            DateTime createTime,
            int kills,
            int deaths,
            int assists,
            string damageText,
            string goldText,
            string kpText,
            string damageTakenText,
            string mode,
            string queueId,
            Image champIcon,
            string champName,
            Image spellImg1,
            Image spellImg2,
            RuneInfo[] primaryRunes,
            RuneInfo[] secondaryRunes,
            RuneInfo[] statRunes,
            Item[] items,
            string selfName,
            JObject rawGameData,
            List<JObject> allParticipants,
            RuneInfo[] augments,
            Dictionary<string, RuneInfo[]> allPlayersAugments
        )
        {
            MatchInfo matchInfo = new MatchInfo();
            matchInfo.Index = index;
            matchInfo.GameId = gameId;
            matchInfo.CurrPuuid = currPuuid;
            matchInfo.CurrSummonerId = currSummonerId;
            matchInfo.ResultText = isWin ? "胜利" : "失败";
            matchInfo.DurationText = FormatDuration(duration);
            matchInfo.Mode = mode;
            matchInfo.QueueId = queueId;
            matchInfo.GameTime = createTime > DateTime.MinValue ? createTime.ToString("yyyy-MM-dd HH:mm") : "未知时间";
            matchInfo.HeroIcon = champIcon;
            matchInfo.ChampionName = champName;
            matchInfo.Kills = kills;
            matchInfo.Deaths = deaths;
            matchInfo.Assists = assists;
            matchInfo.DamageText = damageText;
            matchInfo.GoldText = goldText;
            matchInfo.KPPercentage = kpText;
            matchInfo.DamageTakenText = damageTakenText;
            matchInfo.SummonerSpells = new Image[2] { spellImg1, spellImg2 };
            matchInfo.Items = items ?? new Item[0];
            matchInfo.PrimaryRunes = primaryRunes ?? new RuneInfo[0];
            matchInfo.SecondaryRunes = secondaryRunes ?? new RuneInfo[0];
            matchInfo.Augments = augments ?? Array.Empty<RuneInfo>(); // 当前玩家的海克斯符文
            matchInfo.AllPlayersAugments = allPlayersAugments ?? new Dictionary<string, RuneInfo[]>(); // 所有玩家的海克斯符文
            matchInfo.StatRunes = statRunes ?? new RuneInfo[3];
            matchInfo.BlueTeam = new List<PlayerInfo>();
            matchInfo.RedTeam = new List<PlayerInfo>();
            matchInfo.SelfPlayer = new PlayerInfo
            {
                FullName = selfName,
                Avatar = champIcon,
                TeamId = teamId,
                IsSelf = true,
            };
            matchInfo.AllParticipants = allParticipants;
            return matchInfo;
        }

        private string FormatDuration(int duration)
        {
            if (duration <= 0)
            {
                return "0分0秒";
            }
            int minutes = duration / 60;
            int seconds = duration % 60;
            return $"{minutes}分{seconds}秒";
        }

        private Panel CreateEmptyPanel()
        {
            return new Panel { Size = new Size(0, 0) };
        }
    }
}
