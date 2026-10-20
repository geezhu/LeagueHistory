using System.Globalization;
using League.Models;

namespace League.Services;

public sealed record AugmentChatEntry(string Name, double? WinRate, double? PickRate);

/// <summary>The chosen augments of one hero with arammeta's rates, in the order the player picked them.</summary>
public static class AugmentChatMessage
{
    private const int MaxLength = 180;

    /// <summary>
    /// The picked augments found in the hero's statistics, in the picked order; ids without statistics are skipped.
    /// <paramref name="clientName"/> gives the client's own name, which the detail page also shows.
    /// </summary>
    public static AugmentChatEntry[] Select(
        IEnumerable<AramMetaRecommendation> rows,
        IEnumerable<int> picked,
        Func<int, string?> clientName
    )
    {
        var byId = rows.Where(row => row.Ids.Length > 0).DistinctBy(row => row.Ids[0]).ToDictionary(r => r.Ids[0]);
        return picked
            .Where(byId.ContainsKey)
            .Select(id =>
            {
                var row = byId[id];
                string? name = clientName(id);
                return new AugmentChatEntry(
                    string.IsNullOrWhiteSpace(name) ? row.Name : name,
                    row.WinRate,
                    row.PickRate
                );
            })
            .ToArray();
    }

    public static IReadOnlyList<string> Build(string champion, string patch, IEnumerable<AugmentChatEntry> augments)
    {
        string prefix = $"【{Clean(champion)}·海克斯】arammeta {Clean(patch)} 调整后胜率/选取率：";
        var messages = new List<string>();
        string line = prefix;
        foreach (var augment in augments)
        {
            string item = $"{Clean(augment.Name)} {Rate(augment.WinRate)}/{Rate(augment.PickRate)}";
            if (line.Length > prefix.Length && line.Length + item.Length + 1 > MaxLength)
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

    private static string Clean(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string Rate(double? value) =>
        value is double rate && double.IsFinite(rate) && rate >= 0 && rate <= 1
            ? (rate * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%"
            : "—";
}
