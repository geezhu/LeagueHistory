using League.Models;

namespace League.Services;

/// <summary>
/// The Game Bar's record message: one chat line per player in the format of the F9/F11 summary
/// (ChatMessageBuilder), from the same cached history and with the same send settings.
/// </summary>
public static class OverlayRecordChatMessage
{
    private const int Limit = 180;

    public static IReadOnlyList<string> Build(
        LoadingRoster roster,
        OverlayChatRequest request,
        IEnumerable<PlayerMatchInfo> cached,
        Func<int, string> champion,
        bool hideSelf,
        bool useChampionName
    )
    {
        // Hiding yourself applies to team summaries; picking your own hero sends it on purpose.
        var players = OverlayTierChatMessage
            .Select(roster, request)
            .Where(p => !(hideSelf && p.Self && request.Target != "single"))
            .ToArray();
        if (players.Length == 0)
        {
            throw new InvalidOperationException("未发送：无法确定所选阵营或英雄，请等待名单更新。");
        }

        var infos = cached.ToArray();
        var lines = players
            .Select(p => (Player: p, Info: OverlayPlayerRecords.Find(roster, p, infos)))
            .Select(entry => Line(Name(entry.Player, champion, useChampionName), entry.Info))
            .ToArray();
        if (lines.All(line => line.EndsWith(" 暂无战绩", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("未发送：主程序还没有读取这些玩家的战绩，请稍后重试。");
        }

        return lines.Select(line => line.Length <= Limit ? line : line[..(Limit - 1)] + "…").ToArray();
    }

    public static string Line(string name, PlayerMatchInfo? info)
    {
        int games = info?.WinHistory.Count ?? 0;
        if (games == 0)
        {
            return name + " 暂无战绩";
        }

        int wins = info!.WinHistory.Count(win => win);
        string line = $"{name} 近{games}场 胜率 [{wins * 100.0 / games:F0}%]";
        string[] tags = OverlayRecordSections.Tags(info.Behavior);
        if (tags.Length == 0)
        {
            return line;
        }

        var b = info.Behavior;
        var signals = new List<string>();
        if (b.AvgMissingPings > 0)
        {
            signals.Add($"发问号 {b.AvgMissingPings:F1}");
        }

        if (b.AvgVisionPings > 0)
        {
            signals.Add($"发提醒 {b.AvgVisionPings:F1}");
        }

        if (b.AvgGetBackPings > 0)
        {
            signals.Add($"发撤退 {b.AvgGetBackPings:F1}");
        }

        if (b.AvgDangerPings > 0)
        {
            signals.Add($"发危险 {b.AvgDangerPings:F1}");
        }

        return line
            + (signals.Count > 0 ? " | " + string.Join(" | ", signals) : "")
            + $" 特征 [{string.Join("、", tags)}]";
    }

    // Hidden players and the champion-name setting use the hero; player names drop the tag line, as F9/F11 do.
    private static string Name(LoadingPlayer player, Func<int, string> champion, bool useChampionName)
    {
        string name = player.Name.Split('#')[0].Trim();
        return useChampionName || player.IdentityHidden || name.Length == 0 ? champion(player.ChampionId) : name;
    }
}
