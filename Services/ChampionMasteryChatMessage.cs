using League.Models;

namespace League.Services;

public sealed record ChampionMasteryChatEntry(string Champion, PlayerChampionMastery? Mastery);

public static class ChampionMasteryChatMessage
{
    public static IReadOnlyList<string> Build(string map, string group, IEnumerable<ChampionMasteryChatEntry> entries)
    {
        var rows = entries.ToArray();
        if (rows.Length == 0 || !rows.Any(r => r.Mastery?.Available == true))
        {
            throw new InvalidOperationException("未发送：英雄熟练度尚未就绪或无法查询，请稍后重试。");
        }

        static string Clean(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        string prefix = $"【{Clean(map)}·{Clean(group)}】英雄熟练度：";
        var messages = new List<string>();
        string line = prefix;
        foreach (var entry in rows)
        {
            var mastery = entry.Mastery;
            string text =
                mastery?.Available != true
                    ? mastery?.Message.Contains("未公开") == true
                        ? "身份未公开"
                        : "暂不可用"
                    : mastery.Points.HasValue
                        ? (mastery.Level.HasValue ? $"{mastery.Level}级 / " : "") + $"{mastery.Points.Value:N0}点"
                        : "暂无成就记录";
            if (mastery is { Available: true, Stale: true })
            {
                text += "（缓存）";
            }

            string item = Clean(entry.Champion) + " " + text;
            if (line.Length > prefix.Length && line.Length + item.Length + 1 > 180)
            {
                messages.Add(line);
                line = prefix;
            }
            line += (line.Length > prefix.Length ? "、" : "") + item;
        }
        if (line.Length > prefix.Length)
        {
            messages.Add(line);
        }

        return messages;
    }
}
