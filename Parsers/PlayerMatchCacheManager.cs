using System.Collections.Concurrent;
using System.Diagnostics;
using League.Models;

namespace League.Parsers
{
    public class PlayerMatchCacheManager
    {
        private readonly ConcurrentDictionary<(long SummonerId, string Filter), PlayerMatchInfo> _playerMatchCache =
            new();

        public bool TryGetPlayerMatch(long summonerId, string filter, out PlayerMatchInfo info)
        {
            return _playerMatchCache.TryGetValue((summonerId, filter), out info);
        }

        public void CachePlayerMatch(long summonerId, string filter, PlayerMatchInfo info)
        {
            if (summonerId != 0)
                _playerMatchCache[(summonerId, filter)] = info;
        }

        public void ClearCache()
        {
            _playerMatchCache.Clear();
            Debug.WriteLine("[缓存清理] PlayerMatchCache 已清空");
        }
    }
}
