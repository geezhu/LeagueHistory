using System.Diagnostics;
using League.Models;
using Newtonsoft.Json.Linq;
using static League.FormMain;

namespace League.Parsers
{
    /// <summary>
    /// 负责所有外部数据获取（LCU / SGP 调用）
    /// </summary>
    public class PlayerMatchDataFetcher
    {
        public async Task<JObject> GetGameNameBySummonerIdAsync(
            string summonerId,
            CancellationToken cancellationToken = default
        )
        {
            return await Globals.lcuClient.GetGameNameBySummonerId(summonerId, cancellationToken);
        }

        public async Task<Dictionary<string, RankedStats>> GetRankedStatsAsync(
            string puuid,
            CancellationToken cancellationToken = default
        )
        {
            var rankedJson = await Globals.lcuClient.GetCurrentRankedStatsAsync(puuid, cancellationToken);
            return RankedStats.FromJson(rankedJson);
        }

        public async Task<JArray> GetPlayerMatchesAsync(
            string puuid,
            string currentQueueId,
            int count = 20,
            CancellationToken cancellationToken = default
        )
        {
            count = count is 10 or 20 or 30 or 50 ? count : 20;
            string queueFilter = GetQueueFilter(currentQueueId);
            var matches = await Globals.sgpClient.SgpFetchLatestMatches(
                puuid,
                0,
                count,
                queueFilter,
                cancellationToken
            );
            cancellationToken.ThrowIfCancellationRequested();
            return matches ?? throw new HttpRequestException("Match history unavailable");
        }

        public async Task<string> GetCurrentQueueIdAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var session = await Globals.lcuClient.GetGameSession(cancellationToken);
                var queueId = session?["gameData"]?["queue"]?["id"]?.ToString();
                if (!string.IsNullOrEmpty(queueId) && queueId != "0")
                {
                    return queueId;
                }

                var selection = await Globals.lcuClient.GetChampSelectSession(cancellationToken);
                return selection?["queueId"]?.ToString() ?? "";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
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
                "420" => "q_420",
                "440" => "q_440",
                "400" => "q_400",
                "430" => "q_430",
                "450" => "q_450",
                "480" => "q_480",
                "490" => "q_490",
                "2400" => "q_2400",
                _ => "",
            };
        }
    }
}
