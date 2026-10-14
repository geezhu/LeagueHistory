using League.Models;

namespace League.Services;

/// <summary>
/// One chat line with a side's suspected premades, largest group first, from the same history association as the
/// roster colors. Ungrouped players with a loaded history are listed as solo; players without one are unknown, never
/// assumed solo.
/// </summary>
public static class OverlayPartyChatMessage
{
    private const int Limit = 180;

    public static IReadOnlyList<string> Build(
        LoadingRoster roster,
        OverlayChatRequest request,
        IEnumerable<PlayerMatchInfo> cached,
        Func<int, string> champion
    )
    {
        if (request.Target is not ("own" or "enemy"))
        {
            throw new InvalidOperationException("未发送：组排情况只能按我方或对方发送。");
        }

        var players = OverlayTierChatMessage.Select(roster, request);
        if (players.Length == 0)
        {
            throw new InvalidOperationException("未发送：无法确定所选阵营，请等待名单更新。");
        }

        var infos = cached.ToArray();
        var known = players.Where(p => OverlayPlayerRecords.Find(roster, p, infos) != null).ToHashSet();
        if (known.Count == 0)
        {
            throw new InvalidOperationException("未发送：主程序还没有读取这些玩家的战绩，无法推测组排。");
        }

        var markers = OverlayPartyAnalysis.ForRoster(roster, infos);
        string Names(IEnumerable<LoadingPlayer> group) =>
            "[" + string.Join("、", group.Select(p => champion(p.ChampionId))) + "]";
        var parts = players
            .Where(p => markers.ContainsKey(p.Key))
            .GroupBy(p => markers[p.Key])
            .OrderByDescending(g => g.Key.Size)
            .Select(g => $"{g.Key.Size}排：{Names(g)}")
            .ToList();
        if (parts.Count == 0)
        {
            parts.Add("未发现组排");
        }

        var solo = players.Where(p => !markers.ContainsKey(p.Key) && known.Contains(p)).ToArray();
        if (solo.Length > 0)
        {
            parts.Add("单排：" + Names(solo));
        }

        var unknown = players.Where(p => !known.Contains(p)).ToArray();
        if (unknown.Length > 0)
        {
            parts.Add("未知：" + Names(unknown));
        }

        string side = request.Target == "own" ? "我方" : "对方";
        string text = $"【疑似组排·{side}】" + string.Join("；", parts) + "（按近期同队战绩推测）";
        return [text.Length <= Limit ? text : text[..(Limit - 1)] + "…"];
    }
}
