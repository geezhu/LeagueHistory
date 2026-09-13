using League.Models;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using static League.FormMain;

namespace League.Parsers
{
    /// <summary>
    /// 负责所有外部数据获取（LCU / SGP 调用）
    /// </summary>
    public class PlayerMatchDataFetcher
    {
        public async Task<JObject> GetGameNameBySummonerIdAsync(string summonerId)
        {
            return await Globals.lcuClient.GetGameNameBySummonerId(summonerId);
        }

        public async Task<Dictionary<string, RankedStats>> GetRankedStatsAsync(string puuid)
        {
            var rankedJson = await Globals.lcuClient.GetCurrentRankedStatsAsync(puuid);
            return RankedStats.FromJson(rankedJson);
        }

        public async Task<JArray> GetPlayerMatchesAsync(string puuid, string currentQueueId, int count=20)
        {
            count=count is 10 or 20 or 30 or 50?count:20;
            string queueFilter = GetQueueFilter(currentQueueId);
            return await Globals.sgpClient.SgpFetchLatestMatches(puuid, 0, count, queueFilter);
        }

        public async Task<string> GetCurrentQueueIdAsync()
        {
            try
            {
                var session = await Globals.lcuClient.GetGameSession();
                var queueId = session?["gameData"]?["queue"]?["id"]?.ToString();
                if (!string.IsNullOrEmpty(queueId) && queueId != "0") return queueId;
                var selection = await Globals.lcuClient.GetChampSelectSession();
                return selection?["queueId"]?.ToString() ?? "";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[模式过滤] 获取 session 失败: {ex.Message}");
            }
            return "";
        }

        public static string GetQueueFilter(string queueId)
        {
            return queueId switch
            {
                // 排位
                "420" => "q_420",
                "440" => "q_440",

                // 匹配
                "400" => "q_400",
                "430" => "q_430",

                // 大乱斗 & 无限
                "450" => "q_450",
                "480" => "q_480",
                "490" => "q_490",
                "2400" => "q_2400",
                _ => ""
            };
        }
    }
}
