using League.Models;

namespace League.Services;

/// <summary>
/// One side's crowd control as the control page shows it: each champion's abilities with hard control, plus the
/// ranged strong slows unless the request is "hardControls". Lines longer than one chat message continue in the
/// next, split between champions.
/// </summary>
public static class OverlayControlChatMessage
{
    private const int Limit = 180;

    public static IReadOnlyList<string> Build(
        LoadingRoster roster,
        OverlayChatRequest request,
        IReadOnlyDictionary<int, ControlSkill[]> controls,
        OverlayLiveState? live,
        Func<int, string> champion
    )
    {
        if (request.Target is not ("own" or "enemy"))
        {
            throw new InvalidOperationException("未发送：控制技能只能按我方或对方发送。");
        }

        var players = OverlayTierChatMessage.Select(roster, request);
        if (players.Length == 0)
        {
            throw new InvalidOperationException("未发送：无法确定所选阵营，请等待名单更新。");
        }

        if (!players.Any(p => controls.ContainsKey(p.ChampionId)))
        {
            throw new InvalidOperationException("未发送：英雄技能还在读取，请稍后重试。");
        }

        bool slows = request.Kind != "hardControls";
        var entries = players.Select(p =>
            champion(p.ChampionId)
            + " "
            + (
                controls.GetValueOrDefault(p.ChampionId) is { } skills
                    ? Skills(skills, live?.Effects.GetValueOrDefault(p.Key)?.Cooldown?.UltimateSealed == true, slows)
                    : "读取中"
            )
        );
        string side = request.Target == "own" ? "我方" : "对方";
        return Split($"【{(slows ? "控制" : "硬控")}·{side}】", entries.ToList());
    }

    // A sealed ultimate (Back to Basics) cannot be cast, as on the control page. Without slows, an ability that
    // only slows has nothing left and drops out.
    private static string Skills(ControlSkill[] skills, bool ultimateSealed, bool slows)
    {
        var parts = skills
            .Where(skill => !(skill.Key == "R" && ultimateSealed))
            .Select(skill =>
                skill.Key + string.Join("/", slows && skill.Slow != null ? skill.Effects.Append("减速") : skill.Effects)
            )
            .Where(part => part.Length > 1)
            .ToArray();
        return parts.Length > 0 ? string.Join(" ", parts) : "无硬控";
    }

    private static List<string> Split(string title, List<string> entries)
    {
        var messages = new List<string>();
        string current = title;
        foreach (string entry in entries)
        {
            string next = current.Length == title.Length ? current + entry : current + "；" + entry;
            if (next.Length > Limit && current.Length > title.Length)
            {
                messages.Add(current);
                next = title + entry;
            }
            current = next.Length <= Limit ? next : next[..(Limit - 1)] + "…";
        }
        messages.Add(current);
        return messages;
    }
}
