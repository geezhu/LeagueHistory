using League.Models;

namespace League.Services;

public static class OverlayTierChatMessage
{
    public static LoadingPlayer[] Select(LoadingRoster roster, OverlayChatRequest request)
    {
        var self = roster.Players.FirstOrDefault(p => p.Self);
        if (request.Target == "single")
        {
            int[] teams = self?.Team == 200 ? [200, 100] : [100, 200];
            if (request.Slot is < 0 or >= 10)
            {
                return [];
            }

            var player = roster
                .Players.Where(p => p.Team == teams[request.Slot / 5])
                .Take(5)
                .ElementAtOrDefault(request.Slot % 5);
            return player?.ChampionId == request.ChampionId ? [player] : [];
        }
        if (self == null || request.Target is not ("own" or "enemy"))
        {
            return [];
        }

        return roster
            .Players.Where(p =>
                p.ChampionId > 0 && (request.Target == "own" ? p.Team == self.Team : p.Team != self.Team)
            )
            .Take(5)
            .ToArray();
    }

    public static IReadOnlyList<string> Build(
        LoadingRoster roster,
        OverlayChatRequest request,
        IReadOnlyDictionary<string, PlayerChampionTier> ranks,
        Func<int, string> champion
    )
    {
        var players = Select(roster, request);
        if (players.Length == 0)
        {
            throw new InvalidOperationException("未发送：无法确定所选阵营或英雄，请等待名单更新。");
        }

        if (!players.Any(p => ranks.GetValueOrDefault(p.Key)?.WinRateValue != null))
        {
            throw new InvalidOperationException("未发送：英雄统计尚未就绪或当前模式暂无统计，请稍后重试。");
        }

        bool mayhem = PlayerChampionTier.NormalizeQueue(roster.Queue, roster.Mode) == 2400;
        var messages = new List<string>();
        foreach (var versionGroup in players.GroupBy(p => ranks.GetValueOrDefault(p.Key)?.Version ?? ""))
        {
            var entries = versionGroup.Select(p =>
            {
                var rank = ranks.GetValueOrDefault(p.Key);
                string name = champion(p.ChampionId);
                if (rank?.Position is string pos && pos != "none")
                {
                    name +=
                        "（"
                        + (rank.ReferencePosition ? "参考" : "")
                        + (
                            pos switch
                            {
                                "top" => "上路",
                                "jungle" => "打野",
                                "mid" => "中路",
                                "adc" => "下路",
                                "support" => "辅助",
                                _ => pos,
                            }
                        )
                        + "）";
                }

                return new ChampionTierChatEntry(
                    name,
                    rank?.Tier is string tier && tier != "—" ? tier : "暂无梯队",
                    rank?.WinRateValue,
                    rank?.PickRateValue
                );
            });
            messages.AddRange(
                ChampionTierChatMessage.Build(
                    OverlayChampionDetailService.ModeName(PlayerChampionTier.NormalizeQueue(roster.Queue, roster.Mode)),
                    mayhem ? "arammeta" : "OP.GG",
                    versionGroup.Key,
                    request.Target switch
                    {
                        "own" => "我方",
                        "enemy" => "对方",
                        _ => "英雄",
                    },
                    entries,
                    mayhem
                )
            );
        }
        return messages;
    }
}
