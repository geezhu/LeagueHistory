using System.Diagnostics;
using League.Models;
using Newtonsoft.Json.Linq;
using static League.FormMain;

namespace League.Parsers
{
    /// <summary>
    /// 负责战绩数据的解析和转换
    /// </summary>
    public class MatchDataParser
    {
        public async Task<PlayerMatchInfo> ParsePlayerMatchInfo(
            string puuid,
            JArray matches,
            CancellationToken cancellationToken = default
        )
        {
            var result = new PlayerMatchInfo();
            if (matches == null || matches.Count == 0)
            {
                Debug.WriteLine("matches 数据为空");
                return result;
            }

            try
            {
                foreach (JObject match in matches.Cast<JObject>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    JObject gameJson = match["json"] as JObject ?? match;
                    if (gameJson == null || gameJson["gameId"]?.Value<long>() == 0)
                    {
                        continue;
                    }

                    var participant = FindParticipant(gameJson, puuid);
                    if (participant == null)
                    {
                        continue;
                    }

                    await ProcessMatchData(result, gameJson, participant, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"解析比赛数据异常: {ex.Message}");
            }

            return result;
        }

        private async Task ProcessMatchData(
            PlayerMatchInfo result,
            JObject gameJson,
            JObject participant,
            CancellationToken cancellationToken
        )
        {
            long gameId = gameJson["gameId"]?.Value<long>() ?? 0;
            int championId = participant["championId"]?.Value<int>() ?? 0;
            string championName = participant["championName"]?.ToString() ?? "";
            int kills = participant["kills"]?.Value<int>() ?? 0;
            int deaths = participant["deaths"]?.Value<int>() ?? 0;
            int assists = participant["assists"]?.Value<int>() ?? 0;
            bool isWin = participant["win"]?.Value<bool>() ?? false;

            string gameMode = ExtractGameMode(gameJson); // ← 关键调用

            long gameStart =
                gameJson["gameStartTimestamp"]?.Value<long>() ?? gameJson["gameCreation"]?.Value<long>() ?? 0;
            string gameDate =
                gameStart > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(gameStart).ToString("yyyy-MM-dd") : "未知";

            int gameDurationSeconds =
                gameJson["gameDuration"]?.Value<int>() ?? gameJson["gameLength"]?.Value<int>() ?? 0;

            int durationMinutes = gameDurationSeconds > 0 ? gameDurationSeconds / 60 : 0;
            bool gameEndedInSurrender = gameJson["gameEndedInSurrender"]?.Value<bool>() ?? false;
            bool gameEndedInEarlySurrender = gameJson["gameEndedInEarlySurrender"]?.Value<bool>() ?? false;

            int missingPings = participant["enemyMissingPings"]?.Value<int>() ?? 0;
            int visionPings = participant["enemyVisionPings"]?.Value<int>() ?? 0;
            int getBackPings = participant["getBackPings"]?.Value<int>() ?? 0;
            int dangerPings = participant["dangerPings"]?.Value<int>() ?? 0;

            //Debug.WriteLine(
            //    $"[比赛调试] {participant["riotIdGameName"]?.ToString() ?? "未知"} | " +
            //    $"Duration: {durationMinutes} | " +
            //    $"Surrender: {gameEndedInSurrender} | " +
            //    $"EarlySurrender: {gameEndedInEarlySurrender} | " +
            //    $"Missing: {missingPings} | " +
            //    $"Vision: {visionPings} | " +
            //    $"GetBack: {getBackPings} | " +
            //    $"Danger: {dangerPings}"
            //);

            result.RecentMatches.Add(
                new MatchStat
                {
                    Kills = kills,
                    Deaths = deaths,
                    Assists = assists,

                    GameEndedInSurrender = gameEndedInSurrender,
                    GameEndedInEarlySurrender = gameEndedInEarlySurrender,
                    GameDurationMinutes = durationMinutes,

                    EnemyMissingPings = missingPings,
                    EnemyVisionPings = visionPings,
                    GetBackPings = getBackPings,
                    DangerPings = dangerPings,
                }
            );
            result.WinHistory.Add(isWin);

            var item = new ListViewItem
            {
                ImageKey = championName.Replace(" ", "").Replace("'", ""),
                ForeColor =
                    MatchResult.IsRemake(gameJson) ? MatchResult.RemakeColor
                    : isWin ? Color.Green
                    : Color.Red,
                Tag = new MatchMetadata
                {
                    GameId = gameId,
                    TeamId = participant["teamId"]?.Value<int>() ?? -1,
                    GameData = (JObject)gameJson.DeepClone(),
                },
            };

            item.SubItems.AddRange(new[] { gameMode, $"{kills}/{deaths}/{assists}", gameDate });
            result.MatchItems.Add(item);
            result.MatchKeys.Add($"{gameId}_{participant["teamId"]?.Value<int>() ?? -1}");

            await CacheChampionIcon(championName, championId, result.HeroIcons, cancellationToken);
        }

        private JObject FindParticipant(JObject gameJson, string puuid)
        {
            var participants = gameJson["participants"] as JArray;
            return participants?.Cast<JObject>().FirstOrDefault(p => p["puuid"]?.ToString() == puuid);
        }

        private string ExtractGameMode(JObject gameJson)
        {
            if (gameJson == null)
            {
                return "未知模式";
            }

            // 1. 优先尝试 metadata.tags（原始逻辑）
            var tags = gameJson["metadata"]?["tags"] as JArray;
            if (tags != null && tags.Count > 0)
            {
                string mode = MapGameTags(tags);
                if (!mode.StartsWith("未知("))
                {
                    return mode;
                }
            }

            // 2. 尝试直接从顶层 queueId 获取（SUMMARY 接口常用）
            int queueId = gameJson["queueId"]?.Value<int>() ?? -1;
            if (queueId > 0)
            {
                string modeFromQueue = GetModeFromQueueId(queueId);
                if (!string.IsNullOrEmpty(modeFromQueue))
                {
                    return modeFromQueue;
                }
            }

            // 3. 尝试 gameMode 字段
            string gameModeStr = gameJson["gameMode"]?.ToString();
            if (!string.IsNullOrEmpty(gameModeStr))
            {
                return GameMod.GetModeName(queueId, gameModeStr);
            }

            // 4. 兜底
            return $"未知(queueId={queueId})";
        }

        /// <summary>
        /// 此处用于在卡片中显示
        /// </summary>
        /// <param name="queueId"></param>
        /// <returns></returns>
        private string GetModeFromQueueId(int queueId)
        {
            return queueId switch
            {
                // 排位
                420 => "单双排",
                440 => "灵活排位",

                // 匹配
                400 => "匹配征召",
                430 => "匹配盲选",

                // 大乱斗 & 无限
                450 => "大乱斗",
                900 => "无限乱斗",
                1900 => "自选无限火力",

                // 快速模式
                480 => "快速模式",

                // 斗魂 / 海克斯
                1700 => "斗魂竞技场",
                2400 => "海克斯乱斗",
                2450 => "经典海克斯乱斗",

                // 其他特殊
                1020 => "克隆大作战",
                1300 => "极限闪击",
                1400 => "终极魔典",
                700 => "冠军杯赛",

                // ========== 人机（完整） ==========
                830 => "人机入门",
                840 => "人机新手",
                850 => "人机中等",
                870 => "人机入门",
                880 => "人机新手",
                890 => "人机中等",

                // 新手教程
                2000 => "新手教程1",
                2010 => "新手教程2",
                2020 => "新手教程3",

                // 云顶
                1090 => "云顶匹配",
                1100 => "云顶排位",
                1130 => "云顶疾速",
                1160 => "云顶双人",

                // 自定义 / 训练 / 经典
                0 => "自定义",
                3100 => "自定义峡谷",
                3140 => "训练模式",
                3270 => "自定义海克斯",
                4310 => "经典峡谷",
                4320 => "经典人机",

                // 末日人机
                950 => "末日人机",
                960 => "末日人机",

                _ => "",
            };
        }

        /// <summary>
        /// 战绩标签对应的模式名，按优先级排列：同一场比赛带多个标签时取最先匹配的一项。
        /// </summary>
        private static readonly (string Tag, string Mode)[] TagModes =
        {
            ("q_420", "单双排"),
            ("q_440", "灵活排位"),
            ("q_400", "匹配征召"),
            ("q_430", "匹配盲选"),
            ("q_450", "大乱斗"),
            ("q_900", "无限乱斗"),
            ("q_1900", "自选无限火力"),
            ("q_480", "快速模式"),
            ("q_1700", "斗魂竞技场"),
            ("q_2400", "海克斯乱斗"),
            ("q_2450", "经典海克斯乱斗"),
            ("q_1020", "克隆大作战"),
            ("q_1300", "极限闪击"),
            ("q_1400", "终极魔典"),
            ("q_700", "冠军杯赛"),
            ("q_830", "人机入门"),
            ("q_840", "人机新手"),
            ("q_850", "人机中等"),
            ("q_870", "人机入门"),
            ("q_880", "人机新手"),
            ("q_890", "人机中等"),
            ("q_2000", "新手教程1"),
            ("q_2010", "新手教程2"),
            ("q_2020", "新手教程3"),
            ("q_1090", "云顶匹配"),
            ("q_1100", "云顶排位"),
            ("q_1130", "云顶疾速"),
            ("q_1160", "云顶双人"),
            ("q_3100", "自定义峡谷"),
            ("q_3140", "训练模式"),
            ("q_3270", "自定义海克斯"),
            ("q_4310", "经典峡谷"),
            ("q_4320", "经典人机"),
            ("q_950", "末日人机"),
            ("q_960", "末日人机"),
            ("mode_practicetool", "训练模式"),
            ("mode_classic", "自定义峡谷"),
            ("mode_aram", "自定义大乱斗"),
            ("mode_cherry", "自定义海克斯"),
            ("mode_kiwi", "自定义海克斯"),
            ("type_CUSTOM_GAME", "自定义"),
            ("mode_jade", "经典峡谷"),
            ("mdoe_jade", "经典峡谷"),
        };

        /// <summary>
        /// 此处用于在用户列表中显示
        /// </summary>
        /// <param name="tags"></param>
        /// <returns></returns>
        private string MapGameTags(JArray tags)
        {
            if (tags == null || tags.Count == 0)
            {
                return "未知模式";
            }

            var tagList = tags.Select(t => t.ToString()).ToList();
            foreach (var (tag, mode) in TagModes)
            {
                if (tagList.Contains(tag))
                {
                    return mode;
                }
            }

            return $"未知({string.Join(",", tagList)})";
        }

        private async Task CacheChampionIcon(
            string championName,
            int championId,
            ImageList heroIcons,
            CancellationToken cancellationToken
        )
        {
            string cleanName = championName.Replace(" ", "").Replace("'", "");
            if (!MatchQueryProcessor._imageCache.TryGetValue(cleanName, out var image))
            {
                image = await Globals.resLoading.GetChampionIconAsync(championId).WaitAsync(cancellationToken);
                if (image != null)
                {
                    MatchQueryProcessor._imageCache.TryAdd(cleanName, image);
                }
            }

            if (image != null && !heroIcons.Images.ContainsKey(cleanName))
            {
                heroIcons.Images.Add(cleanName, image);
            }
        }
    }
}
