using System.Text.RegularExpressions;

namespace League.Models;

/// <summary>
/// The player card's short form of a ranked queue. The query layer formats a rank as "璀璨钻石 II(56)"; two of those
/// do not fit side by side in a card a fifth of the window wide, so the card shows "钻石 II·56" and keeps the full
/// text in the tooltip. An unranked queue has nothing to show and comes back empty.
/// </summary>
public static partial class RankText
{
    public static bool IsRanked(string? rank) =>
        !string.IsNullOrWhiteSpace(rank)
        && !rank.Contains("未定级")
        && !rank.Contains("未评级")
        && !rank.Contains("未知");

    public static string Compact(string? rank)
    {
        if (!IsRanked(rank))
        {
            return "";
        }

        string text = rank!.Trim();
        var match = FormattedRank().Match(text);
        if (!match.Success)
        {
            // Placeholders such as 隐藏 or 加载中... are not ranks and are shown as they are.
            return text;
        }

        // Every tier name is a two-character epithet before the tier itself: 璀璨钻石, 超凡大师, 最强王者.
        string tier = match.Groups["tier"].Value[2..];
        string division = match.Groups["division"].Success ? " " + match.Groups["division"].Value : "";
        string points = match.Groups["points"].Success ? "·" + match.Groups["points"].Value : "";
        return tier + division + points;
    }

    [GeneratedRegex(@"^(?<tier>\p{IsCJKUnifiedIdeographs}{4})(?: (?<division>IV|I{1,3}))?(?:\((?<points>\d+)\))?$")]
    private static partial Regex FormattedRank();
}
