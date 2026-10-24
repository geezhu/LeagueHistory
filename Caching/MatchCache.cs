using League.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace League.Caching
{
    /// <summary>
    /// 战绩列表前面的缓存：每个玩家每种筛选存一段从新到旧的连续对局（<see cref="MatchRun"/>），分页直接在这一段上切。
    /// 翻到还没取过的页只请求缺的尾部；强制刷新只取最新一段对齐头部，不再整段重取。
    /// </summary>
    public static class MatchCache
    {
        /// <summary>SGP 最多能查到最近 200 场。</summary>
        public const int Limit = 200;

        // 尾部请求多取一场已有的最后一场作锚点：服务器在两次请求之间插入了新对局时，锚点会往后错，按错开的场数再取，有限次数。
        private const int MaxTailAttempts = 3;

        private static readonly object _gate = new object();
        private static readonly Dictionary<(string Puuid, string Tag), MatchRun> _runs =
            new Dictionary<(string Puuid, string Tag), MatchRun>();

        /// <summary>
        /// 取第 begIndex 场起的 pageSize 场。refresh 时先用服务器最新一段对齐头部，该玩家其他筛选的缓存下次读取时也会对齐。
        /// fetch(start, count) 请求服务器上的一段，null 表示服务器不可用；此时返回 null，缓存不变。
        /// </summary>
        public static async Task<JArray?> GetPageAsync(
            string puuid,
            string? tag,
            int begIndex,
            int pageSize,
            bool refresh,
            Func<int, int, Task<JArray?>> fetch
        )
        {
            var key = (puuid, tag ?? "all");
            MatchRun run;
            lock (_gate)
            {
                if (refresh)
                {
                    MarkPlayerStale(puuid);
                }
                if (!_runs.TryGetValue(key, out run!))
                {
                    run = new MatchRun { HeadStale = true };
                    _runs[key] = run;
                }
            }

            if (run.HeadStale && !await RefreshHeadAsync(run, begIndex, pageSize, fetch))
            {
                return null;
            }
            if (!await FillTailAsync(run, begIndex, pageSize, fetch))
            {
                return null;
            }

            lock (_gate)
            {
                JArray page = run.Slice(begIndex, pageSize);
                if (run.Count == 0)
                {
                    // 一场都没有不值得记住，下次照常去问服务器。
                    _runs.Remove(key);
                }
                return page;
            }
        }

        /// <summary>对局 ID。SGP 的对局包在 json 里；没有 gameId 的就用整段文本区分，不至于把不同的对局当成同一场。</summary>
        public static string GameId(JToken game)
        {
            JToken? id = game["json"]?["gameId"] ?? game["gameId"];
            return id?.ToString() ?? game.ToString(Formatting.None);
        }

        public static void ClearAll()
        {
            lock (_gate)
            {
                _runs.Clear();
            }
        }

        private static async Task<bool> RefreshHeadAsync(
            MatchRun run,
            int begIndex,
            int pageSize,
            Func<int, int, Task<JArray?>> fetch
        )
        {
            int requested = Math.Min(begIndex + pageSize, Limit);
            JArray? latest = await fetch(0, requested);
            if (latest == null)
            {
                return false;
            }
            int before;
            int after;
            lock (_gate)
            {
                before = run.Count;
                run.MergeHead(latest, requested);
                after = run.Count;
            }
            // 临时诊断：游戏结束后的刷新是否拿到了刚结束的那一局，确认后删除。
            AppLog.Shared.Info(
                LogCategory.Data,
                $"[战绩刷新·临时] 请求最新 {requested} 场，返回 {latest.Count} 场，缓存 {before}→{after} 场，最新一局{NewestEndText(latest)}"
            );
            return true;
        }

        private static string NewestEndText(JArray latest)
        {
            JToken? newest = latest.Count > 0 ? latest[0]["json"] ?? latest[0] : null;
            long ended = newest?["gameEndTimestamp"]?.Value<long>() ?? newest?["gameCreation"]?.Value<long>() ?? 0;
            if (ended <= 0)
            {
                return "没有时间";
            }
            double seconds = (DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(ended)).TotalSeconds;
            return $"结束于 {seconds:F0} 秒前";
        }

        private static async Task<bool> FillTailAsync(
            MatchRun run,
            int begIndex,
            int pageSize,
            Func<int, int, Task<JArray?>> fetch
        )
        {
            int shift = 0;
            for (int attempt = 0; attempt < MaxTailAttempts; attempt++)
            {
                int start;
                int count;
                bool anchored;
                lock (_gate)
                {
                    if (run.Covers(begIndex, pageSize))
                    {
                        return true;
                    }
                    anchored = run.Count > 0;
                    int missing = Math.Min(begIndex + pageSize, Limit) - run.Count;
                    start = anchored ? run.Count - 1 + shift : 0;
                    count = anchored ? missing + 1 : missing;
                }

                JArray? older = await fetch(start, count);
                if (older == null)
                {
                    return false;
                }
                lock (_gate)
                {
                    int anchor = anchored ? run.TailIndexIn(older) : -1;
                    if (anchored && anchor < 0)
                    {
                        // 这一段整个都比尾部新，锚点还在更后面。
                        shift += older.Count;
                    }
                    else
                    {
                        shift += Math.Max(anchor, 0);
                        run.Append(older, anchor + 1);
                    }
                    if (older.Count < count)
                    {
                        run.MarkComplete();
                    }
                }
            }
            // 仍然没补齐就按现有的切：这一页会短一些，不会重复或错位。
            return true;
        }

        private static void MarkPlayerStale(string puuid)
        {
            foreach (var (key, run) in _runs)
            {
                if (key.Puuid == puuid)
                {
                    run.HeadStale = true;
                }
            }
        }
    }
}
