using League.Models;

namespace League.Services;

public static class OverlayDeathStreakChatMessage
{
    public static IReadOnlyList<string> Build(
        LoadingRoster roster,
        OverlayChatRequest request,
        IReadOnlyDictionary<string, LiveDeathStreak> streaks,
        Func<int, string> champion
    )
    {
        var players = OverlayTierChatMessage.Select(roster, request);
        if (players.Length == 0)
        {
            throw new InvalidOperationException("未发送：无法确定所选阵营或英雄，请等待名单更新。");
        }

        var rows = players
            .Select(p =>
                (Champion: champion(p.ChampionId), Streak: p.IdentityHidden ? null : streaks.GetValueOrDefault(p.Key))
            )
            .ToArray();
        static bool Available(LiveDeathStreak? value) => value?.Current is >= 0 && value.Highest >= value.Current;
        if (!rows.Any(r => Available(r.Streak)))
        {
            throw new InvalidOperationException("未发送：本局连死记录尚未就绪或无法匹配玩家，请稍后重试。");
        }

        string map = OverlayChampionDetailService.ModeName(
            PlayerChampionTier.NormalizeQueue(roster.Queue, roster.Mode)
        );
        string group = request.Target switch
        {
            "own" => "我方",
            "enemy" => "对方",
            _ => "英雄",
        };
        string prefix = $"【{map}·{group}】连死/本局最高：";
        var messages = new List<string>();
        string line = prefix;
        foreach (var row in rows)
        {
            var streak = row.Streak;
            string text = Available(streak)
                ? $"{streak!.Current}/{streak.Highest}" + (streak.Complete ? "" : "（已记录）")
                : "未知";
            string name = row.Champion.Replace('\r', ' ').Replace('\n', ' ').Trim();
            // Keep each entry inside the chat limit, including unusual resource names.
            int maxName = 180 - prefix.Length - text.Length - 1;
            if (name.Length > maxName)
            {
                name = name[..(maxName - 1)] + "…";
            }

            string item = name + " " + text;
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
