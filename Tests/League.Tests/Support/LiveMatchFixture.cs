using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Support;

/// <summary>
/// Live snapshots for the pure-logic services, built the way the old checks in
/// <c>Tests/StatisticsChecks</c> built them. Everything is in memory: no client, no game and no disk.
/// </summary>
internal static class LiveMatchFixture
{
    /// <summary>The name of the player the snapshot marks as self.</summary>
    public const string SelfName = "我";

    /// <summary>
    /// Ten players on the Howling Abyss at the given game time: self and four allies on ORDER, five enemies on
    /// CHAOS. Self is the first ORDER player at level 8; <paramref name="dead"/> marks players as dead with the
    /// remaining respawn time, by index.
    /// </summary>
    public static LiveMatchSnapshot AramGame(
        double time,
        double gold,
        double health,
        params (int Index, double Respawn)[] dead
    )
    {
        var players = Enumerable.Range(0, 10).Select(index => Player(index, dead)).ToArray();
        var stats = new LiveChampionStats(1000, 0, 0, 0, 0, 0, 0, 0) { CurrentHealth = health };
        return new("ARAM", 12, time, players)
        {
            Active = new(stats, []) { CurrentGold = gold },
            Events = [],
        };
    }

    /// <summary>
    /// One player alone on the Howling Abyss, for the turret arithmetic. <paramref name="stats"/> is the active
    /// player's champion stats, or null when the snapshot carries none.
    /// </summary>
    public static LiveMatchSnapshot SinglePlayer(
        string mode,
        double time,
        LiveChampionStats? stats,
        params LiveMatchEvent[] events
    ) => new(mode, 12, time, [Player(0, [])]) { Active = stats == null ? null : new(stats, []), Events = events };

    /// <summary>Summoner's Rift at 14:06, with three lanes, the 14:00 interval change and a fallen bot inhibitor.</summary>
    public static LiveMatchSnapshot RiftGame()
    {
        var players = Enumerable
            .Range(0, 10)
            .Select(index => new LiveMatchPlayer(
                $"p|{index}",
                $"测试玩家 {index + 1}",
                "Ahri",
                $"样例英雄 {index + 1}",
                index < 5 ? "ORDER" : "CHAOS",
                index == 0,
                9 + index % 5,
                null
            ))
            .ToArray();
        return new("CLASSIC", 11, 846, players)
        {
            Events =
            [
                Structure("MinionsSpawning", 30, ""),
                Structure("TurretKilled", 610, "Turret_T2_R_03_A"),
                Structure("TurretKilled", 680, "Turret_T2_R_02_A"),
                Structure("TurretKilled", 742, "Turret_T2_C_07_A"),
                Structure("InhibKilled", 780, "Barracks_T2_R1"),
                Structure("TurretKilled", 701, "Turret_T1_C_05_A"),
            ],
        };
    }

    /// <summary>The same snapshot with every player at one level, for the level-gated rules.</summary>
    public static LiveMatchSnapshot WithLevel(LiveMatchSnapshot snapshot, int level) =>
        snapshot with
        {
            Players = [.. snapshot.Players.Select(player => player with { Level = level })],
        };

    /// <summary>
    /// The active player's champion stats with 2000 max health and 1000 left, at the given basic-attack numbers.
    /// </summary>
    public static LiveChampionStats Attacker(double attackDamage, double abilityPower, double attackRange) =>
        new(2000, 50, 40, 0, 0, 0, 0, 0)
        {
            CurrentHealth = 1000,
            AttackDamage = attackDamage,
            AbilityPower = abilityPower,
            AttackSpeed = 1,
            AttackRange = attackRange,
        };

    /// <summary>
    /// One player on a lane map at a game time, for the wave schedule. <paramref name="team"/> is the team the
    /// player counts as self on, which decides which side of the lane the forecast calls own.
    /// </summary>
    public static LiveMatchSnapshot WaveGame(
        string mode,
        int map,
        double time,
        string team,
        params LiveMatchEvent[] events
    ) => new(mode, map, time, [new("self|0", SelfName, "Ahri", "阿狸", team, true, 1, null)]) { Events = events };

    /// <summary>The same snapshot with every player on one team.</summary>
    public static LiveMatchSnapshot WithTeam(LiveMatchSnapshot snapshot, string team) =>
        snapshot with
        {
            Players = [.. snapshot.Players.Select(player => player with { Team = team })],
        };

    /// <summary>A structure event; the old checks passed the id, the killer and the victim as empty.</summary>
    public static LiveMatchEvent Structure(string name, double time, string structure) =>
        new(null, name, time, "", "") { Structure = structure };

    /// <summary>A champion kill on the Howling Abyss, named by the aliases the players carry.</summary>
    public static LiveMatchEvent Kill(long id, double time, string killer, string victim) =>
        new(id, "ChampionKill", time, killer, victim);

    /// <summary>
    /// The snapshot as the body of a <c>liveclientdata/allgamedata</c> response. The view reaches its poll, card and
    /// recommendation paths through <see cref="League.Clients.LiveMatchClient"/>, whose only entry point is that
    /// document, so a snapshot built above has to go back through the same JSON shape the game serves. Everything
    /// <c>LiveMatchSnapshot.Parse</c> keeps is written: the active player, the game metadata, each player's scores,
    /// items and raw champion name, and the events. Cheaper than hand-writing a document per test, and it keeps the
    /// builders above as the single source of the sample.
    /// </summary>
    public static JObject ToJson(LiveMatchSnapshot snapshot)
    {
        var active = snapshot.Players.FirstOrDefault(player => player.Self);
        return new JObject
        {
            ["activePlayer"] = new JObject { ["riotId"] = active?.Name, ["summonerName"] = active?.Name },
            ["gameData"] = new JObject
            {
                ["gameMode"] = snapshot.Mode,
                ["mapNumber"] = snapshot.Map,
                ["gameTime"] = snapshot.Time,
            },
            ["allPlayers"] = new JArray(
                snapshot.Players.Select(player =>
                    (JToken)
                        new JObject
                        {
                            ["riotId"] = player.Name,
                            ["summonerName"] = player.Name,
                            ["rawChampionName"] =
                                player.RawChampionName.Length > 0
                                    ? player.RawChampionName
                                    : "game_character_displayname_" + player.ChampionAlias,
                            ["championName"] = player.ChampionName,
                            ["team"] = player.Team,
                            ["level"] = player.Level,
                            ["scores"] = new JObject
                            {
                                ["kills"] = player.Kills,
                                ["deaths"] = player.Deaths,
                                ["assists"] = player.Assists,
                            },
                            ["items"] =
                                player.Items == null
                                    ? null
                                    : new JArray(
                                        player.Items.Select(item =>
                                            (JToken)
                                                new JObject
                                                {
                                                    ["itemID"] = item.Id,
                                                    ["displayName"] = item.Name,
                                                    ["count"] = item.Count,
                                                }
                                        )
                                    ),
                            ["isDead"] = player.IsDead,
                            ["respawnTimer"] = player.RespawnTimer,
                        }
                )
            ),
            ["events"] = new JObject
            {
                ["Events"] = new JArray(
                    (snapshot.Events ?? []).Select(entry =>
                        (JToken)
                            new JObject
                            {
                                ["EventID"] = entry.Id,
                                ["EventName"] = entry.Name,
                                ["EventTime"] = entry.Time,
                                ["KillerName"] = entry.Killer,
                                ["VictimName"] = entry.Victim,
                                [entry.Name] = entry.Structure,
                            }
                    )
                ),
            },
        };
    }

    private static LiveMatchPlayer Player(int index, (int Index, double Respawn)[] dead)
    {
        string name =
            index == 0 ? SelfName
            : index < 5 ? $"队友{index}"
            : $"敌方{index - 4}";
        bool isDead = dead.Any(entry => entry.Index == index);
        return new LiveMatchPlayer(
            $"{name}|{index}",
            name,
            "Lux",
            "拉克丝",
            index < 5 ? "ORDER" : "CHAOS",
            index == 0,
            8,
            null
        )
        {
            EventAliases = [name],
            IsDead = isDead,
            RespawnTimer = dead.FirstOrDefault(entry => entry.Index == index).Respawn,
        };
    }
}
