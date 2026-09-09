using System.Globalization;

namespace League.Services;

public sealed record ChampionTierChatEntry(string Name, string Tier, double? WinRate, double? PickRate);

public static class ChampionTierChatMessage
{
    public static IReadOnlyList<string> Build(string map, string source, string version, string group,
        IEnumerable<ChampionTierChatEntry> champions, bool adjustedRates = false)
    {
        static string Clean(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        string prefix = $"【{Clean(map)}·{Clean(group)}】{Clean(source)} {Clean(version)}：";
        var messages = new List<string>();
        string line = prefix;
        static string Rate(double? value) => value is double rate && double.IsFinite(rate) && rate >= 0 && rate <= 1
            ? (rate * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "暂无数据";
        foreach (var champion in champions)
        {
            string item = $"{Clean(champion.Name)} {Clean(champion.Tier)}｜{(adjustedRates ? "调整后胜率" : "胜率")} {Rate(champion.WinRate)}｜{(adjustedRates ? "样本选取率" : "登场率")} {Rate(champion.PickRate)}";
            if (line.Length > prefix.Length && line.Length + item.Length + 2 > 180)
            { messages.Add(line); line = prefix; }
            line += (line.Length > prefix.Length ? "、" : "") + item;
        }
        if (line.Length > prefix.Length) messages.Add(line);
        return messages;
    }
}
