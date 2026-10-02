using League.Models;

namespace League.Services;

public static class OverlayMasteryChatMessage
{
    public static IReadOnlyList<string> Build(LoadingRoster roster, OverlayChatRequest request,
        IReadOnlyDictionary<string, PlayerChampionMastery> masteries, Func<int, string> champion)
    {
        var players = OverlayTierChatMessage.Select(roster, request);
        if (players.Length == 0)
            throw new InvalidOperationException("未发送：无法确定所选阵营或英雄，请等待名单更新。");
        return ChampionMasteryChatMessage.Build(
            OverlayChampionDetailService.ModeName(PlayerChampionTier.NormalizeQueue(roster.Queue, roster.Mode)),
            request.Target switch { "own" => "我方", "enemy" => "对方", _ => "英雄" },
            players.Select(p =>
            {
                var mastery = masteries.GetValueOrDefault(p.Key);
                if (mastery?.ChampionId != p.ChampionId) mastery = null;
                return new ChampionMasteryChatEntry(champion(p.ChampionId), mastery);
            }));
    }
}
