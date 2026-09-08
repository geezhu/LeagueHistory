using Newtonsoft.Json.Linq;

namespace League.Models;

/// <summary>Scans only as far as needed to fill the requested result page.</summary>
public sealed class OutcomeHistoryPager
{
    private readonly List<JObject> _records = new();
    public int Scanned => _records.Count;
    public bool Exhausted { get; private set; }
    public int Matches { get; private set; }

    public async Task<JArray> LoadPage(string puuid, bool win, int page, int size,
        Func<int,int,Task<JArray>> fetch)
    {
        var matches = _records.Where(g => MatchOutcomeFilter.GetWin(g,puuid) == win).ToList();
        int wanted = checked(page * size);
        while (matches.Count < wanted && !Exhausted)
        {
            int count = Math.Min(wanted - matches.Count, 200 - Scanned);
            if (count <= 0) { Exhausted = true; break; }
            var batch = await fetch(Scanned,count);
            if (batch == null) throw new InvalidOperationException("获取筛选记录失败，请重试");
            if (batch.Count == 0) { Exhausted = true; break; }
            foreach (var token in batch.Take(count))
            {
                var record = token as JObject ?? new JObject();
                _records.Add((JObject)record.DeepClone());
                if (MatchOutcomeFilter.GetWin(record,puuid) == win) matches.Add(record);
            }
            if (Scanned >= 200) Exhausted = true;
        }
        Matches = matches.Count;
        return new JArray(matches.Skip((page-1)*size).Take(size));
    }
}
