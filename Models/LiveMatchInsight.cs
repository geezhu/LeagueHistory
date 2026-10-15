namespace League.Models;

public sealed record LivePlayerInsight(
    int ItemGold,
    int? Kills,
    int? Deaths,
    int? Assists,
    double? KillParticipation,
    double Respawn
)
{
    public string Score => Kills is int k && Deaths is int d && Assists is int a ? $"{k}/{d}/{a}" : "";

    /// <summary>The inventory as the Tab scoreboard shows it.</summary>
    public LiveMatchItem[] Items { get; init; } = [];

    /// <summary>The champion level from 2999; 0 when it is not reported.</summary>
    public int Level { get; init; }
}

public sealed record LiveTeamInsight(int Kills, int ItemGold, int Aces, int Multikills, int LargestMultikill);

/// <summary>A cluster of kills close together in time; both sides' losses are counted.</summary>
public sealed record LiveFight(double Start, int OwnKills, int EnemyKills);

/// <summary>
/// Scoreboard-level facts from 2999: the same information as the in-game Tab panel and kill feed,
/// summarized for the overlay. Item value stands in for gold, which 2999 does not report for other players.
/// </summary>
public sealed record LiveMatchInsight(
    double Time,
    LiveTeamInsight Own,
    LiveTeamInsight Enemy,
    IReadOnlyDictionary<string, LivePlayerInsight> Players,
    LiveFight[] Fights,
    string Result
)
{
    // Kills further apart than this start a new fight; ARAM fights rarely pause longer.
    private const double FightGapSeconds = 12;
    private const int FightMinimumKills = 3;

    public int GoldLead => Own.ItemGold - Enemy.ItemGold;
    public int FightsWon => Fights.Count(f => f.OwnKills > f.EnemyKills);
    public int FightsLost => Fights.Count(f => f.OwnKills < f.EnemyKills);

    /// <param name="itemValue">Total cost of one unit of an item, including its components.</param>
    public static LiveMatchInsight? Calculate(
        LoadingRoster roster,
        LiveMatchSnapshot snapshot,
        Func<LiveMatchItem, int> itemValue
    )
    {
        int own =
            roster.Players.FirstOrDefault(p => p.Self)?.Team
            ?? LiveMatchSnapshot.TeamId(snapshot.Players.FirstOrDefault(p => p.Self)?.Team ?? "");
        if (own is not (100 or 200))
        {
            return null;
        }

        var teamKills = snapshot
            .Players.GroupBy(p => LiveMatchSnapshot.TeamId(p.Team))
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Kills ?? 0));
        var gold = snapshot.Players.ToDictionary(p => p.Key, p => (p.Items ?? []).Sum(i => itemValue(i) * i.Count));
        var players = new Dictionary<string, LivePlayerInsight>();
        foreach (var player in roster.Players)
        {
            if (snapshot.Find(player) is not { } live)
            {
                continue;
            }

            int kills = teamKills.GetValueOrDefault(player.Team);
            double? participation =
                kills > 0 && live.Kills is int k && live.Assists is int a ? Math.Min(1, (double)(k + a) / kills) : null;
            players[player.Key] = new(
                gold[live.Key],
                live.Kills,
                live.Deaths,
                live.Assists,
                participation,
                live.IsDead ? live.RespawnTimer : 0
            )
            {
                Items = live.Items ?? [],
                Level = live.Level,
            };
        }

        var events = (snapshot.Events ?? []).OrderBy(e => e.Time).ThenBy(e => e.Id).ToArray();
        var team = snapshot.Players.ToDictionary(p => p.Key, p => LiveMatchSnapshot.TeamId(p.Team));
        var aliases = snapshot.EventAliasMap();
        int TeamOf(string name) => aliases.TryGetValue(name, out string? key) ? team[key] : 0;
        LiveTeamInsight Team(int id) =>
            new(
                teamKills.GetValueOrDefault(id),
                snapshot.Players.Where(p => team[p.Key] == id).Sum(p => gold[p.Key]),
                events.Count(e => e.Name == "Ace" && LiveMatchSnapshot.TeamId(e.Team) == id),
                // A triple kill also reports the double before it; count each run once by its double.
                events.Count(e => e.Name == "Multikill" && e.Streak == 2 && TeamOf(e.Killer) == id),
                events
                    .Where(e => e.Name == "Multikill" && TeamOf(e.Killer) == id)
                    .Select(e => e.Streak)
                    .DefaultIfEmpty(0)
                    .Max()
            );
        return new(
            snapshot.Time,
            Team(own),
            Team(own == 100 ? 200 : 100),
            players,
            FindFights(events, own, TeamOf),
            events.LastOrDefault(e => e.Name == "GameEnd")?.Result ?? ""
        );
    }

    private static LiveFight[] FindFights(LiveMatchEvent[] events, int own, Func<string, int> teamOf)
    {
        var fights = new List<LiveFight>();
        var current = new List<(double Time, bool OwnKill)>();
        void Close()
        {
            if (current.Count >= FightMinimumKills)
            {
                fights.Add(new(current[0].Time, current.Count(k => k.OwnKill), current.Count(k => !k.OwnKill)));
            }

            current.Clear();
        }

        foreach (var kill in events.Where(e => e.Name == "ChampionKill"))
        {
            // The victim's side is known even for tower or minion executions; fall back to the killer's.
            int victim = teamOf(kill.Victim),
                killer = teamOf(kill.Killer);
            if (victim == 0 && killer == 0)
            {
                continue;
            }

            if (current.Count > 0 && kill.Time - current[^1].Time > FightGapSeconds)
            {
                Close();
            }

            current.Add((kill.Time, victim != 0 ? victim != own : killer == own));
        }

        Close();
        return fights.ToArray();
    }
}
