using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace League.Caching
{
    /// <summary>
    /// 一个玩家在一种筛选下的对局，按时间从新到旧连续排列，对应服务器上从 0 开始的那一段。
    /// 新对局插到头部，更早的对局接到尾部；以对局 ID 去重。对局存成紧凑 JSON 文本，读取时重新解析，调用方改不到缓存。
    /// </summary>
    internal sealed class MatchRun
    {
        private readonly List<string> _games = new List<string>();
        private readonly List<string> _ids = new List<string>();
        private readonly HashSet<string> _known = new HashSet<string>();

        public int Count => _games.Count;

        /// <summary>服务器已经没有更早的对局，尾部之后不用再请求。</summary>
        public bool Complete { get; private set; }

        /// <summary>该玩家有过强制刷新、而这一段还没对过头部：下次读取前先补上新对局。</summary>
        public bool HeadStale { get; set; }

        public bool Covers(int begIndex, int pageSize)
        {
            return Complete || _games.Count >= Math.Min(begIndex + pageSize, MatchCache.Limit);
        }

        public JArray Slice(int begIndex, int pageSize)
        {
            var page = new JArray();
            int end = Math.Min(begIndex + pageSize, _games.Count);
            for (int i = begIndex; i < end; i++)
            {
                page.Add(JToken.Parse(_games[i]));
            }
            return page;
        }

        /// <summary>
        /// 用服务器最新的一段（从 0 开始、请求了 requested 场）对齐头部：遇到已有的对局之前的都是新对局，插到前面。
        /// 一场都对不上说明中间可能隔着没取到的对局，只能用这一段重建。
        /// </summary>
        public void MergeHead(JArray latest, int requested)
        {
            HeadStale = false;
            var fresh = new List<JToken>();
            bool overlaps = false;
            foreach (JToken game in latest)
            {
                if (_known.Contains(GameId(game)))
                {
                    overlaps = true;
                    break;
                }
                fresh.Add(game);
            }

            if (!overlaps)
            {
                Reset();
                Append(latest);
                Complete = latest.Count < requested;
                return;
            }

            for (int i = fresh.Count - 1; i >= 0; i--)
            {
                Insert(0, fresh[i]);
            }
            TrimToLimit();
        }

        /// <summary>尾部那场在 games 里的位置，没有就是 -1。</summary>
        public int TailIndexIn(JArray games)
        {
            if (_ids.Count == 0)
            {
                return -1;
            }
            string tail = _ids[_ids.Count - 1];
            for (int i = 0; i < games.Count; i++)
            {
                if (GameId(games[i]) == tail)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>把 older 从 from 起的对局接到尾部，已有的跳过。</summary>
        public void Append(JArray older, int from = 0)
        {
            for (int i = from; i < older.Count; i++)
            {
                if (!_known.Contains(GameId(older[i])))
                {
                    Insert(_games.Count, older[i]);
                }
            }
            TrimToLimit();
        }

        public void MarkComplete()
        {
            Complete = true;
        }

        private void Insert(int index, JToken game)
        {
            string id = GameId(game);
            _games.Insert(index, game.ToString(Formatting.None));
            _ids.Insert(index, id);
            _known.Add(id);
        }

        private void TrimToLimit()
        {
            while (_games.Count > MatchCache.Limit)
            {
                int last = _games.Count - 1;
                _known.Remove(_ids[last]);
                _games.RemoveAt(last);
                _ids.RemoveAt(last);
                Complete = false;
            }
        }

        private void Reset()
        {
            _games.Clear();
            _ids.Clear();
            _known.Clear();
            Complete = false;
        }

        // SGP 的对局包在 json 里；没有 gameId 的就用整段文本区分，不至于把不同的对局当成同一场。
        private static string GameId(JToken game)
        {
            JToken? id = game["json"]?["gameId"] ?? game["gameId"];
            return id?.ToString() ?? game.ToString(Formatting.None);
        }
    }
}
