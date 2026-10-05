namespace League.Models;

public sealed record LiveMatchEvent(long? Id, string Name, double Time, string Killer, string Victim);

public sealed record LiveDeathStreak(int? Current, int? Highest, bool Complete, string Description)
{
    public static LiveDeathStreak Unavailable(string reason = "等待 2999 事件数据。") => new(null, null, false, reason);
    public string Text => Current.HasValue ? $"连死 {Current} · 最高 {Highest}" + (Complete ? "" : "*") : "连死 — · 最高 —";
    public string Rule => Description + "\n死亡加一，击杀归零，助攻不归零；最高值仅统计本局。";
}

/// <summary>Rebuild from the current game's history. Polls, reconnects and program restarts never accumulate duplicates.</summary>
public static class LiveDeathStreakAnalysis
{
    public static IReadOnlyDictionary<string, LiveDeathStreak> ForRoster(LoadingRoster roster, LiveMatchSnapshot? snapshot)
    {
        return roster.Players.ToDictionary(p => p.Key, p =>
        {
            if (p.IdentityHidden) return LiveDeathStreak.Unavailable("玩家身份隐藏，不能唯一匹配死亡事件。");
            var matches = snapshot?.Players.Where(l => l.Name == p.Name && (l.Team == "ORDER" ? 100 : l.Team == "CHAOS" ? 200 : 0) == p.Team).ToArray() ?? [];
            return matches.Length == 1 ? snapshot!.DeathStreaks.GetValueOrDefault(matches[0].Key) ?? LiveDeathStreak.Unavailable()
                : LiveDeathStreak.Unavailable("等待游戏事件与玩家名单匹配。");
        });
    }
    public static IReadOnlyDictionary<string, LiveDeathStreak> Calculate(LiveMatchSnapshot snapshot)
    {
        var results = snapshot.Players.ToDictionary(p => p.Key, _ => LiveDeathStreak.Unavailable());
        if (snapshot.Events == null) return results;
        var aliases = snapshot.Players.Where(p=>p.Name.Trim()!="#").SelectMany(p => p.EventAliases.Distinct().Select(name => (name, p.Key)))
            .Where(p => !string.IsNullOrWhiteSpace(p.name) && p.name.Trim() != "#")
            .GroupBy(p => p.name, StringComparer.Ordinal).Where(g => g.Select(p => p.Key).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);
        var counts = snapshot.Players.ToDictionary(p => p.Key, _ => (Current: 0, Highest: 0, Deaths: 0, Kills: 0));
        var events = snapshot.Events.Where(e => e.Time <= snapshot.Time + 1)
            .DistinctBy(e => (e.Id, e.Name, e.Time, e.Killer, e.Victim)).OrderBy(e => e.Time).ThenBy(e => e.Id).ToArray();
        bool started = events.Any(e => e.Name == "GameStart" && e.Time <= 1);
        foreach (var ev in events.Where(e => e.Name == "ChampionKill"))
        {
            string? victim=aliases.GetValueOrDefault(ev.Victim);
            if (aliases.TryGetValue(ev.Killer, out string? killer) && killer != victim)
            {
                var count = counts[killer]; counts[killer] = (0, count.Highest, count.Deaths, count.Kills + 1);
            }
            if (victim!=null)
            {
                var count = counts[victim]; int current = count.Current + 1;
                counts[victim] = (current, Math.Max(count.Highest, current), count.Deaths + 1, count.Kills);
            }
        }
        foreach (var player in snapshot.Players)
        {
            if (!aliases.Values.Contains(player.Key))
            { results[player.Key] = LiveDeathStreak.Unavailable("玩家身份隐藏或无法唯一匹配事件，不能推测连死次数。"); continue; }
            var count = counts[player.Key];
            if(count.Deaths==0 && count.Kills==0 && player.Deaths>0)
            { results[player.Key]=LiveDeathStreak.Unavailable("尚未取得该玩家的死亡事件，不能将缺失记录显示为零。");continue; }
            if (player.Deaths < count.Deaths || player.Kills < count.Kills)
            { results[player.Key] = LiveDeathStreak.Unavailable("事件记录与当前比分尚未同步，等待更新。"); continue; }
            bool consistent = (!player.Deaths.HasValue || player.Deaths == count.Deaths) && (!player.Kills.HasValue || player.Kills == count.Kills);
            bool complete = consistent && (started || player.Deaths.HasValue && player.Kills.HasValue);
            results[player.Key] = new(count.Current, count.Highest, complete, complete ? "依据本局事件历史统计。"
                : "* 仅统计已取得的事件；历史不完整，数值不代表完整的连死记录。");
        }
        return results;
    }
}
