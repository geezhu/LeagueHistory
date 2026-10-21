using System.Diagnostics;
using System.Text.RegularExpressions;
using League.Controls;
using League.Models;
using League.Services;
using League.UIHelpers;
using Newtonsoft.Json.Linq;

namespace League.UIState
{
    public partial class DetailedViewLayoutManager : IDisposable
    {
        private static readonly Dictionary<int, (Image, string, string)> _championCache =
            new Dictionary<int, (Image, string, string)>();

        private static readonly Dictionary<int, (Image, string, string)> _spellCache =
            new Dictionary<int, (Image, string, string)>();

        private static readonly Dictionary<int, (Image, string, string)> _itemCache =
            new Dictionary<int, (Image, string, string)>();

        private static readonly Dictionary<int, (Image, string, string)> _runeCache =
            new Dictionary<int, (Image, string, string)>();

        private const int PANEL_W = 1200;

        private const int ITEM_H = 25;

        private const int ROW2_H = 30;

        private ToolTip _toolTip;

        public event Action<string>? PlayerProfileRequested;
        public Func<string, Task<JObject?>>? ResolvePlayerProfile { get; set; }

        public DetailedViewLayoutManager(ToolTip toolTip)
        {
            _toolTip = toolTip;
        }

        public void Dispose()
        {
            _toolTip?.Dispose();
        }

        public async Task<Panel> CreateDetailedPlayerPanel(
            JObject participant,
            MatchInfo matchInfo,
            int x,
            int y,
            int teamId
        )
        {
            (string name, bool isSelf) tuple = await GetPlayerInfoAsync(participant, matchInfo);
            string playerName = tuple.name;
            bool isSelf = tuple.isSelf;
            Color backColor = GetPanelBackColor(teamId, isSelf);
            Panel panel = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(1120, 44),
                BorderStyle = BorderStyle.None,
                BackColor = isSelf ? Theme.AccentSoft : Color.White,
            };
            await PreloadAllAsync(participant, matchInfo); // 修改：传入matchInfo
            await RetryMissingAugmentsAsync(participant, matchInfo);
            (
                int damage,
                int damageRank,
                int gold,
                int goldRank,
                double kp,
                int kpRank,
                int damageTaken,
                int damageTakenRank
            ) ranks = CalculateRanks(participant, matchInfo);
            panel.SuspendLayout();
            CreateScoreboardRow(panel, participant, playerName, isSelf, ranks, matchInfo);
            panel.ResumeLayout(performLayout: false);
            return panel;
        }

        private async Task<(string name, bool isSelf)> GetPlayerInfoAsync(JObject participant, MatchInfo matchInfo)
        {
            string summonerId = participant["summonerId"]?.ToString();
            string riotName = participant["riotIdGameName"]?.ToString();
            string ritoTag = participant["riotIdTagline"]?.ToString();
            bool isSelf =
                (!string.IsNullOrEmpty(matchInfo.CurrPuuid) && participant["puuid"]?.ToString() == matchInfo.CurrPuuid)
                || (!string.IsNullOrEmpty(matchInfo.CurrSummonerId) && summonerId == matchInfo.CurrSummonerId);
            if (!string.IsNullOrEmpty(riotName) && !string.IsNullOrEmpty(ritoTag))
            {
                return (riotName + "#" + ritoTag, isSelf);
            }
            if (!string.IsNullOrEmpty(summonerId))
            {
                JObject summonerData = await FormMain.Globals.lcuClient.GetGameNameBySummonerId(summonerId);
                if (summonerData != null)
                {
                    return ($"{summonerData["gameName"]}#{summonerData["tagLine"]}", isSelf);
                }
            }
            return (name: "未知玩家", isSelf: false);
        }

        private Color GetPanelBackColor(int teamId, bool isSelf)
        {
            return isSelf
                ? Color.LightYellow
                : teamId switch
                {
                    200 => Color.FromArgb(245, 240, 245),
                    100 => Color.FromArgb(200, 240, 245),
                    _ => Color.FloralWhite,
                };
        }

        // 修改：更准确的模式判断
        private bool IsHextechArenaMode(JObject participant)
        {
            try
            {
                // 方法1：通过队列ID判断（从matchInfo获取更准确）
                // 这里需要从matchInfo获取队列信息，或者从participant的父级获取

                // 方法2：通过游戏模式字段判断
                string gameMode = participant["gameMode"]?.ToString() ?? "";
                if (
                    gameMode.Contains("cherry", StringComparison.OrdinalIgnoreCase)
                    || gameMode.Contains("hextech", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }

                // 方法3：如果有海克斯符文数据，也认为是海克斯大乱斗
                int[] augments = ExtractAugments(participant);
                return augments.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        // 修改：获取当前玩家的海克斯符文
        private RuneInfo[] GetSelfAugments(JObject participant, MatchInfo matchInfo)
        {
            int[] ids = ExtractAugments(participant);
            string participantPuuid = participant["puuid"]?.ToString();
            if (!string.IsNullOrEmpty(participantPuuid) && matchInfo.AllPlayersAugments != null)
            {
                if (matchInfo.AllPlayersAugments.TryGetValue(participantPuuid, out var augments))
                {
                    if (ids.Length == 0)
                    {
                        return augments ?? Array.Empty<RuneInfo>();
                    }

                    return ids.Select(id =>
                            augments?.FirstOrDefault(a => a.id == id)
                            ?? new RuneInfo
                            {
                                id = id,
                                name = $"海克斯 {id}",
                                longDesc = "图片暂未加载",
                            }
                        )
                        .ToArray();
                }
            }

            // 备用方案：如果AllPlayersAugments中没有，尝试从participant数据中提取
            return ids.Select(id => new RuneInfo
                {
                    id = id,
                    name = $"海克斯 {id}",
                    longDesc = "图片暂未加载",
                })
                .ToArray();
        }

        private async Task RetryMissingAugmentsAsync(JObject participant, MatchInfo matchInfo)
        {
            if (!IsHextechArenaMode(participant))
            {
                return;
            }

            var augments = GetSelfAugments(participant, matchInfo);
            foreach (var augment in augments.Where(a => a.Icon == null))
            {
                try
                {
                    var (image, name, description) = await FormMain.Globals.resLoading.GetAugmentInfoAsync(augment.id);
                    augment.Icon = image;
                    augment.name = name;
                    augment.longDesc = image == null ? description + "\n图片未加载，请重新打开详情重试" : description;
                }
                catch (Exception ex)
                {
                    AppLog.Shared.Warn(LogCategory.Ui, $"海克斯 {augment.id} 重试失败", ex);
                }
            }
            string? puuid = participant["puuid"]?.ToString();
            if (!string.IsNullOrEmpty(puuid))
            {
                lock (matchInfo.AllPlayersAugments)
                {
                    matchInfo.AllPlayersAugments[puuid] = augments;
                }

                if (puuid == matchInfo.CurrPuuid)
                {
                    matchInfo.Augments = augments;
                }
            }
        }

        // 提取海克斯符文ID
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

        // 修改：简化预加载，海克斯符文不需要重新加载
        private async Task PreloadAllAsync(JObject participant, MatchInfo matchInfo)
        {
            List<Task> tasks = new List<Task>();
            int championId = participant["championId"]?.Value<int>() ?? 0;
            if (championId > 0 && !_championCache.ContainsKey(championId))
            {
                tasks.Add(
                    PreloadResourceAsync(championId, _championCache, FormMain.Globals.resLoading.GetChampionInfoAsync)
                );
            }

            for (int i = 1; i <= 2; i++)
            {
                int spellId = participant[$"spell{i}Id"]?.Value<int>() ?? 0;
                if (spellId > 0 && !_spellCache.ContainsKey(spellId))
                {
                    tasks.Add(
                        PreloadResourceAsync(spellId, _spellCache, FormMain.Globals.resLoading.GetSpellInfoAsync)
                    );
                }
            }

            for (int j = 0; j < 7; j++)
            {
                int itemId = participant[$"item{j}"]?.Value<int>() ?? 0;
                if (itemId > 0 && !_itemCache.ContainsKey(itemId))
                {
                    tasks.Add(PreloadResourceAsync(itemId, _itemCache, FormMain.Globals.resLoading.GetItemInfoAsync));
                }
            }

            // 判断是否为海克斯大乱斗模式
            bool isHextechArena = IsHextechArenaMode(participant);

            if (!isHextechArena)
            {
                // 只有普通模式才需要预加载符文
                (RuneInfo[] Primary, RuneInfo[] Secondary, RuneInfo[] Shards) runesData = LoadRunesForParticipant(
                    participant
                );
                PreloadRuneArray(runesData.Primary, tasks);
                PreloadRuneArray(runesData.Secondary, tasks);
                PreloadRuneArray(runesData.Shards, tasks);
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
        }

        private void PreloadRuneArray(RuneInfo[] runes, List<Task> tasks)
        {
            if (runes == null)
            {
                return;
            }
            foreach (RuneInfo rune in runes.Where((r) => r != null && !_runeCache.ContainsKey(r.RuneId)))
            {
                tasks.Add(PreloadResourceAsync(rune.RuneId, _runeCache, FormMain.Globals.resLoading.GetRuneInfoAsync));
            }
        }

        private async Task PreloadResourceAsync(
            int resourceId,
            Dictionary<int, (Image, string, string)> cache,
            Func<int, Task<(Image, string, string)>> loader
        )
        {
            try
            {
                (Image, string, string) resource = await loader(resourceId);
                if (resource.Item1 != null)
                {
                    lock (cache)
                    {
                        cache[resourceId] = resource;
                    }
                }
            }
            catch (Exception ex)
            {
                Exception ex2 = ex;
                Debug.WriteLine($"预加载资源 {resourceId} 失败: {ex2.Message}");
            }
        }

        private (
            int damage,
            int damageRank,
            int gold,
            int goldRank,
            double kp,
            int kpRank,
            int damageTaken,
            int damageTakenRank
        ) CalculateRanks(JObject participant, MatchInfo matchInfo)
        {
            List<JObject> allParticipants = matchInfo.AllParticipants;
            int currentDamage = participant["totalDamageDealtToChampions"]?.Value<int>() ?? 0;
            int currentGold = participant["goldEarned"]?.Value<int>() ?? 0;
            double currentKP = (participant["challenges"]?["killParticipation"]?.Value<double>()).GetValueOrDefault();
            int currentDamageTaken = participant["totalDamageTaken"]?.Value<int>() ?? 0;
            return (
                damage: currentDamage,
                damageRank: GetRank(
                    allParticipants.Select((p) => p["totalDamageDealtToChampions"]?.Value<int>() ?? 0).ToList(),
                    currentDamage
                ),
                gold: currentGold,
                goldRank: GetRank(
                    allParticipants.Select((p) => p["goldEarned"]?.Value<int>() ?? 0).ToList(),
                    currentGold
                ),
                kp: currentKP,
                kpRank: GetRank(
                    allParticipants
                        .Select((p) => (p["challenges"]?["killParticipation"]?.Value<double>()).GetValueOrDefault())
                        .ToList(),
                    currentKP
                ),
                damageTaken: currentDamageTaken,
                damageTakenRank: GetRank(
                    allParticipants.Select((p) => p["totalDamageTaken"]?.Value<int>() ?? 0).ToList(),
                    currentDamageTaken
                )
            );
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

        private (RuneInfo[] Primary, RuneInfo[] Secondary, RuneInfo[] Shards) LoadRunesForParticipant(
            JObject participant
        )
        {
            JToken perks = participant["perks"];
            if (!(perks?["styles"] is JArray styles))
            {
                return (Primary: null, Secondary: null, Shards: null);
            }
            RuneInfo[] primaryRunes = (
                from r in styles
                    .FirstOrDefault((s) => s["description"]?.ToString() == "primaryStyle")
                    ?["selections"]?.Cast<JObject>()
                    .Take(4)
                    .Select(
                        delegate(JObject selection)
                        {
                            int num = selection["perk"]?.Value<int>() ?? 0;
                            return num > 0 ? new RuneInfo { RuneId = num } : null;
                        }
                    )
                where r != null
                select r
            ).ToArray();
            RuneInfo[] secondaryRunes = (
                from r in styles
                    .FirstOrDefault((s) => s["description"]?.ToString() == "subStyle")
                    ?["selections"]?.Cast<JObject>()
                    .Take(2)
                    .Select(
                        delegate(JObject selection)
                        {
                            int num = selection["perk"]?.Value<int>() ?? 0;
                            return num > 0 ? new RuneInfo { RuneId = num } : null;
                        }
                    )
                where r != null
                select r
            ).ToArray();
            JToken statPerks = perks["statPerks"];
            RuneInfo[] shards = null;
            if (statPerks != null)
            {
                shards = new RuneInfo[3]
                {
                    CreateShard(statPerks["offense"]?.Value<int>() ?? 0),
                    CreateShard(statPerks["flex"]?.Value<int>() ?? 0),
                    CreateShard(statPerks["defense"]?.Value<int>() ?? 0),
                }
                    .Where((r) => r != null)
                    .ToArray();
            }
            return (Primary: primaryRunes, Secondary: secondaryRunes, Shards: shards);
        }

        private RuneInfo CreateShard(int shardId)
        {
            return shardId > 0 ? new RuneInfo { RuneId = shardId } : null;
        }

        private string StripHtmlTags(string input)
        {
            return string.IsNullOrEmpty(input)
                ? string.Empty
                : Regex
                    .Replace(input.Replace("<br>", "\n").Replace("<br/>", "\n").Replace("<br />", "\n"), "<.*?>", "")
                    .Trim();
        }
    }
}
