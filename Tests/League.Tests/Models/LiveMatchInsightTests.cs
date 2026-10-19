using League.Models;

namespace League.Tests.Models;

/// <summary>
/// The scoreboard insight built from a 2999 snapshot: the gold lead, the fights won and lost, what
/// <see cref="LiveMatchInsight.Calculate"/> derives for every player and team, and the score line
/// <see cref="LivePlayerInsight.Score"/> shows. The fixture and the expected values come from the checks in
/// Tests/StatisticsChecks/LiveInsightChecks.cs; everything is in memory, with no client, no game and no disk.
/// </summary>
public class LiveMatchInsightTests
{
    // The old check's catalog: 3071 is worth 3000 and 1001 is worth 300, anything else its own reported price.
    private static readonly Dictionary<int, int> Prices = new() { [3071] = 3000, [1001] = 300 };

    private static readonly LiveTeamInsight EmptyTeam = new(0, 0, 0, 0, 0);

    private static readonly LiveFight[] NoFights = [];

    private static readonly LiveMatchEvent[] SampleEvents =
    [
        new(0, "GameStart", 0.02, "", ""),
        Kill(1, 100, "A1", "B1"),
        Kill(2, 104, "A2", "B2"),
        new(3, "Multikill", 104, "A1", "") { Streak = 2 },
        new(4, "Multikill", 105, "A1", "") { Streak = 3 },
        new(5, "Ace", 105, "", "") { Team = "ORDER" },
        Kill(6, 109, "B1", "A2"),
        Kill(7, 300, "A1", "B2"),
        new(8, "GameEnd", 600, "", "") { Result = "Win" },
    ];

    private static LiveMatchItem Item(int id, int count, int price) => new(id, "装备", count) { Price = price };

    private static LiveMatchEvent Kill(long id, double time, string killer, string victim) =>
        new(id, "ChampionKill", time, killer, victim);

    private static LoadingPlayer RosterPlayer(
        string key,
        string name,
        int team,
        int championId = 1,
        bool self = false,
        bool hidden = false
    ) => new(key, name, "", 0, championId, team, self, hidden);

    private static LiveMatchPlayer Player(
        string key,
        string name,
        string team,
        bool self = false,
        int? kills = 0,
        int? deaths = 0,
        int? assists = 0,
        LiveMatchItem[]? items = null,
        string[]? aliases = null,
        int level = 1,
        bool dead = false,
        double respawn = 0,
        string[]? spells = null
    ) =>
        new(key, name, "Ziggs", "吉格斯", team, self, level, items)
        {
            EventAliases = aliases ?? new[] { name },
            Kills = kills,
            Deaths = deaths,
            Assists = assists,
            IsDead = dead,
            RespawnTimer = respawn,
            SummonerSpells = spells ?? Array.Empty<string>(),
        };

    /// <summary>The old check's roster: four accounts, one per player, with the self entry on ORDER.</summary>
    private static LoadingRoster SampleRoster() =>
        new(
            "game-1",
            2400,
            "KIWI",
            [
                RosterPlayer("r-a1", "A1#1", 100, 1, self: true),
                RosterPlayer("r-a2", "A2#1", 100, 2),
                RosterPlayer("r-b1", "B1#1", 200, 3),
                RosterPlayer("r-b2", "B2#1", 200, 4),
            ]
        );

    /// <summary>The old check's snapshot, literal for literal: one item per side and nine events.</summary>
    private static LiveMatchSnapshot SampleSnapshot() =>
        new(
            "KIWI",
            12,
            610,
            [
                Player(
                    "a1",
                    "A1#1",
                    "ORDER",
                    self: true,
                    kills: 2,
                    deaths: 0,
                    assists: 1,
                    items: [Item(3071, 1, 200)],
                    aliases: ["A1#1", "A1"],
                    level: 10,
                    spells: ["SummonerFlash", "SummonerHeal"]
                ),
                Player(
                    "a2",
                    "A2#1",
                    "ORDER",
                    kills: 2,
                    deaths: 1,
                    assists: 0,
                    items: [],
                    aliases: ["A2#1", "A2"],
                    level: 10
                ),
                Player(
                    "b1",
                    "B1#1",
                    "CHAOS",
                    kills: 1,
                    deaths: 1,
                    assists: 0,
                    items: [Item(1001, 2, 300)],
                    aliases: ["B1#1", "B1"],
                    level: 10
                ),
                Player(
                    "b2",
                    "B2#1",
                    "CHAOS",
                    kills: 0,
                    deaths: 2,
                    assists: 0,
                    items: [],
                    aliases: ["B2#1", "B2"],
                    level: 10,
                    dead: true,
                    respawn: 17.5
                ),
            ]
        )
        {
            Events = SampleEvents,
        };

    private static LoadingRoster Roster(params LoadingPlayer[] players) => new("game-1", 2400, "KIWI", players);

    private static LiveMatchSnapshot Snapshot(params LiveMatchPlayer[] players) => new("KIWI", 12, 610, players);

    /// <summary>Runs the calculation with the old check's item-value function.</summary>
    private static LiveMatchInsight Insight(LoadingRoster roster, LiveMatchSnapshot snapshot) =>
        LiveMatchInsight.Calculate(roster, snapshot, item => Prices.GetValueOrDefault(item.Id, item.Price))
        ?? throw new InvalidOperationException("样例数据必须产出对局洞察（own 为 100 或 200）。");

    private static LiveMatchInsight Sample() => Insight(SampleRoster(), SampleSnapshot());

    /// <summary>An insight with nothing but the fields a rule under test needs.</summary>
    private static LiveMatchInsight Match(
        LiveTeamInsight? own = null,
        LiveTeamInsight? enemy = null,
        LiveFight[]? fights = null
    ) =>
        new(
            0,
            own ?? EmptyTeam,
            enemy ?? EmptyTeam,
            new Dictionary<string, LivePlayerInsight>(),
            fights ?? NoFights,
            ""
        );

    private static LiveTeamInsight Side(int itemGold) => new(0, itemGold, 0, 0, 0);

    private static LiveFight[] Fights(params (int Own, int Enemy)[] fights) =>
        [.. fights.Select((fight, index) => new LiveFight(index, fight.Own, fight.Enemy))];

    [Theory]
    [InlineData(2, 0, 1, "2/0/1")]
    [InlineData(0, 0, 0, "0/0/0")]
    [InlineData(10, 5, 20, "10/5/20")]
    public void Score_KillsDeathsAndAssistsPresent_ReturnsThemSlashSeparated(
        int kills,
        int deaths,
        int assists,
        string expected
    )
    {
        var player = new LivePlayerInsight(0, kills, deaths, assists, null, 0);

        Assert.Equal(expected, player.Score);
    }

    [Theory]
    [InlineData(null, 0, 1)]
    [InlineData(2, null, 1)]
    [InlineData(2, 0, null)]
    [InlineData(null, null, null)]
    public void Score_AnyOfTheThreeMissing_ReturnsEmptyString(int? kills, int? deaths, int? assists)
    {
        var player = new LivePlayerInsight(0, kills, deaths, assists, null, 0);

        Assert.Equal("", player.Score);
    }

    [Fact]
    public void GoldLead_OwnItemGoldHigher_ReturnsTheDifference()
    {
        var insight = Match(own: Side(3000), enemy: Side(600));

        Assert.Equal(2400, insight.GoldLead);
    }

    [Fact]
    public void GoldLead_EnemyItemGoldHigher_ReturnsANegativeDifference()
    {
        var insight = Match(own: Side(600), enemy: Side(3000));

        Assert.Equal(-2400, insight.GoldLead);
    }

    [Fact]
    public void GoldLead_EqualItemGold_ReturnsZero()
    {
        var insight = Match(own: Side(3000), enemy: Side(3000));

        Assert.Equal(0, insight.GoldLead);
    }

    [Fact]
    public void FightsWon_OwnKillsHigher_CountsEverySuchFight()
    {
        var insight = Match(fights: Fights((3, 0), (2, 1)));

        Assert.Equal(2, insight.FightsWon);
        Assert.Equal(0, insight.FightsLost);
    }

    [Fact]
    public void FightsLost_EnemyKillsHigher_CountsEverySuchFight()
    {
        var insight = Match(fights: Fights((1, 2), (0, 3)));

        Assert.Equal(2, insight.FightsLost);
        Assert.Equal(0, insight.FightsWon);
    }

    [Fact]
    public void FightsWonAndLost_TiedFight_CountInNeither()
    {
        var insight = Match(fights: Fights((2, 2)));

        Assert.Single(insight.Fights);
        Assert.Equal(0, insight.FightsWon);
        Assert.Equal(0, insight.FightsLost);
    }

    [Fact]
    public void FightsWonAndLost_MixedFights_CountOnlyTheDecidedOnes()
    {
        var insight = Match(fights: Fights((2, 1), (1, 2), (2, 2)));

        Assert.Equal(3, insight.Fights.Length);
        Assert.Equal(1, insight.FightsWon);
        Assert.Equal(1, insight.FightsLost);
    }

    [Fact]
    public void FightsWonAndLost_NoFights_ReturnZero()
    {
        var insight = Match();

        Assert.Empty(insight.Fights);
        Assert.Equal(0, insight.FightsWon);
        Assert.Equal(0, insight.FightsLost);
    }

    [Fact]
    public void Calculate_ItemValueFunction_MultipliesTheCatalogPriceByTheStackCount()
    {
        var insight = Sample();

        Assert.Equal(3000, insight.Own.ItemGold);
        Assert.Equal(600, insight.Enemy.ItemGold);
        Assert.Equal(2400, insight.GoldLead);
    }

    [Fact]
    public void Calculate_TeamKills_SumTheScoreboardKillsOfEachSide()
    {
        var insight = Sample();

        Assert.Equal(4, insight.Own.Kills);
        Assert.Equal(1, insight.Enemy.Kills);
    }

    [Fact]
    public void Calculate_Participation_IsKillsPlusAssistsOverTeamKills()
    {
        var self = Sample().Players["r-a1"];

        Assert.Equal(0.75, self.KillParticipation);
        Assert.Equal("2/0/1", self.Score);
    }

    [Fact]
    public void Calculate_Respawn_IsReportedOnlyForTheDeadPlayer()
    {
        var players = Sample().Players;

        Assert.Equal(17.5, players["r-b2"].Respawn);
        Assert.Equal(0, players["r-a1"].Respawn);
    }

    [Fact]
    public void Calculate_Fights_ThreeCloseKillsFormOneFight()
    {
        var insight = Sample();

        // The lone kill at 300s is under the three-kill minimum, so it forms no fight of its own.
        var fight = Assert.Single(insight.Fights);
        Assert.Equal(100, fight.Start);
        Assert.Equal(2, fight.OwnKills);
        Assert.Equal(1, fight.EnemyKills);
        Assert.Equal(1, insight.FightsWon);
        Assert.Equal(0, insight.FightsLost);
    }

    [Fact]
    public void Calculate_Multikill_CountsEachRunOnceByItsDouble()
    {
        var insight = Sample();

        // A double and a triple are one run of three, not two kills.
        Assert.Equal(1, insight.Own.Multikills);
        Assert.Equal(3, insight.Own.LargestMultikill);
        Assert.Equal(0, insight.Enemy.Multikills);
        Assert.Equal(0, insight.Enemy.LargestMultikill);
    }

    [Fact]
    public void Calculate_Aces_AreCreditedToTheAcingTeam()
    {
        var insight = Sample();

        Assert.Equal(1, insight.Own.Aces);
        Assert.Equal(0, insight.Enemy.Aces);
    }

    [Fact]
    public void Calculate_GameEndResult_ComesFromTheLastGameEndEvent()
    {
        Assert.Equal("Win", Sample().Result);
    }

    [Fact]
    public void Calculate_Time_IsCopiedFromTheSnapshot()
    {
        Assert.Equal(610, Sample().Time);
    }

    [Fact]
    public void Calculate_SelfPlayer_KeepsLevelItemsAndSummonerSpells()
    {
        var self = Sample().Players["r-a1"];

        Assert.Equal(10, self.Level);
        var item = Assert.Single(self.Items);
        Assert.Equal(3071, item.Id);
        Assert.Equal(1, item.Count);
        Assert.Equal(new[] { "SummonerFlash", "SummonerHeal" }, self.SummonerSpells);
    }

    [Fact]
    public void Calculate_SelfOnChaos_ReportsTheChaosSideAsOwn()
    {
        var roster = Roster(
            RosterPlayer("r-a1", "A1#1", 100, 1),
            RosterPlayer("r-a2", "A2#1", 100, 2),
            RosterPlayer("r-b1", "B1#1", 200, 3, self: true),
            RosterPlayer("r-b2", "B2#1", 200, 4)
        );

        var insight = Insight(roster, SampleSnapshot());

        Assert.Equal(1, insight.Own.Kills);
        Assert.Equal(600, insight.Own.ItemGold);
        Assert.Equal(4, insight.Enemy.Kills);
        Assert.Equal(3000, insight.Enemy.ItemGold);
        Assert.Equal(-2400, insight.GoldLead);
        Assert.Equal(1, insight.Enemy.Aces);
    }

    [Fact]
    public void Calculate_RosterWithoutSelf_FallsBackToTheSnapshotsSelf()
    {
        var roster = Roster(RosterPlayer("r-a1", "A1#1", 100, 1));

        var insight = Insight(roster, SampleSnapshot());

        Assert.Equal(4, insight.Own.Kills);
        Assert.Equal(3000, insight.Own.ItemGold);
        Assert.Equal(1, insight.Enemy.Kills);
    }

    [Fact]
    public void Calculate_EmptyRosterAndSnapshotWithoutSelf_ReturnsNull()
    {
        var snapshot = Snapshot(Player("a1", "A1#1", "ORDER"));

        Assert.Null(LiveMatchInsight.Calculate(Roster(), snapshot, item => item.Price));
    }

    [Fact]
    public void Calculate_SnapshotSelfOnAnUnknownTeam_ReturnsNull()
    {
        var roster = Roster(RosterPlayer("r-a1", "A1#1", 0, 1));
        var snapshot = Snapshot(Player("a1", "A1#1", "UNKNOWN", self: true));

        Assert.Null(LiveMatchInsight.Calculate(roster, snapshot, item => item.Price));
    }

    [Fact]
    public void Calculate_RosterSelfOnAnUnknownTeam_DoesNotFallBackToTheSnapshot()
    {
        var roster = Roster(RosterPlayer("r-a1", "A1#1", 0, 1, self: true));

        Assert.Null(LiveMatchInsight.Calculate(roster, SampleSnapshot(), item => item.Price));
    }

    [Fact]
    public void Calculate_RosterPlayerMissingFromTheSnapshot_HasNoEntry()
    {
        var roster = Roster(RosterPlayer("r-a1", "A1#1", 100, 1, self: true), RosterPlayer("r-x1", "X1#1", 100, 5));
        var snapshot = Snapshot(Player("a1", "A1#1", "ORDER", self: true));

        var insight = Insight(roster, snapshot);

        Assert.DoesNotContain("r-x1", insight.Players.Keys);
        Assert.Contains("r-a1", insight.Players.Keys);
    }

    [Fact]
    public void Calculate_HiddenIdentityPlayer_HasNoEntry()
    {
        var roster = Roster(RosterPlayer("r-a1", "A1#1", 100, 1, hidden: true), RosterPlayer("r-a2", "A2#1", 100, 2));
        var snapshot = Snapshot(Player("a1", "A1#1", "ORDER", self: true), Player("a2", "A2#1", "ORDER"));

        var insight = Insight(roster, snapshot);

        Assert.DoesNotContain("r-a1", insight.Players.Keys);
        Assert.Contains("r-a2", insight.Players.Keys);
    }

    [Fact]
    public void Calculate_TwoSnapshotPlayersWithTheSameNameAndTeam_HaveNoEntry()
    {
        var roster = Roster(RosterPlayer("r-a1", "A1#1", 100, 1, self: true));
        var snapshot = Snapshot(
            Player("a1", "A1#1", "ORDER", self: true, kills: 2),
            Player("a1-copy", "A1#1", "ORDER", kills: 3)
        );

        var insight = Insight(roster, snapshot);

        Assert.DoesNotContain("r-a1", insight.Players.Keys);
        Assert.Equal(5, insight.Own.Kills);
    }

    [Fact]
    public void Calculate_TeamWithoutKills_HasNullParticipation()
    {
        var roster = Roster(RosterPlayer("r-a1", "A1#1", 100, 1, self: true));
        var snapshot = Snapshot(Player("a1", "A1#1", "ORDER", self: true, kills: 0, deaths: 0, assists: 3));

        var self = Insight(roster, snapshot).Players["r-a1"];

        Assert.Null(self.KillParticipation);
        Assert.Equal("0/0/3", self.Score);
    }

    [Fact]
    public void Calculate_ParticipationAboveTeamKills_IsClampedToOne()
    {
        var roster = Roster(RosterPlayer("r-a1", "A1#1", 100, 1, self: true));
        var snapshot = Snapshot(
            Player("a1", "A1#1", "ORDER", self: true, kills: 2, deaths: 0, assists: 2),
            Player("a2", "A2#1", "ORDER", kills: 0, deaths: 0, assists: 5)
        );

        Assert.Equal(1.0, Insight(roster, snapshot).Players["r-a1"].KillParticipation);
    }

    [Fact]
    public void Calculate_PlayerWithoutScoreFields_HasNullParticipation()
    {
        var roster = Roster(RosterPlayer("r-a1", "A1#1", 100, 1, self: true));
        var snapshot = Snapshot(Player("a1", "A1#1", "ORDER", self: true, kills: 3, deaths: null, assists: null));

        var self = Insight(roster, snapshot).Players["r-a1"];

        Assert.Null(self.KillParticipation);
        Assert.Null(self.Deaths);
        Assert.Equal("", self.Score);
    }

    [Fact]
    public void Calculate_PlayerWithoutItems_HasEmptyItemsAndNoGold()
    {
        var roster = Roster(RosterPlayer("r-a1", "A1#1", 100, 1, self: true));
        var snapshot = Snapshot(Player("a1", "A1#1", "ORDER", self: true, items: null));

        var insight = Insight(roster, snapshot);

        Assert.Empty(insight.Players["r-a1"].Items);
        Assert.Equal(0, insight.Own.ItemGold);
    }

    [Fact]
    public void Calculate_PlayerOnAnUnknownTeam_CountsTowardsNeitherSide()
    {
        var roster = Roster(RosterPlayer("r-a1", "A1#1", 100, 1, self: true));
        var snapshot = Snapshot(
            Player("a1", "A1#1", "ORDER", self: true, items: [Item(3071, 1, 200)]),
            Player("x1", "X1#1", "UNKNOWN", items: [Item(3071, 1, 200)])
        );

        var insight = Insight(roster, snapshot);

        Assert.Equal(3000, insight.Own.ItemGold);
        Assert.Equal(0, insight.Enemy.ItemGold);
    }

    [Fact]
    public void Calculate_MultikillByAnUnknownKiller_IsCreditedToNoTeam()
    {
        var roster = Roster(RosterPlayer("r-a1", "A1#1", 100, 1, self: true));
        var snapshot = Snapshot(Player("a1", "A1#1", "ORDER", self: true)) with
        {
            Events = [new(1, "Multikill", 100, "无名", "") { Streak = 2 }],
        };

        var insight = Insight(roster, snapshot);

        Assert.Equal(0, insight.Own.Multikills);
        Assert.Equal(0, insight.Enemy.Multikills);
    }

    [Fact]
    public void Calculate_KillWithAnUnknownVictim_FallsBackToTheKillersSide()
    {
        var roster = Roster(RosterPlayer("r-a1", "A1#1", 100, 1, self: true));
        var snapshot = Snapshot(Player("a1", "A1#1", "ORDER", self: true)) with
        {
            Events =
            [
                Kill(1, 100, "A1#1", "Turret_T1_C_05_A"),
                Kill(2, 104, "A1#1", "Turret_T1_C_05_A"),
                Kill(3, 108, "A1#1", "Turret_T1_C_05_A"),
            ],
        };

        var fight = Assert.Single(Insight(roster, snapshot).Fights);

        Assert.Equal(3, fight.OwnKills);
        Assert.Equal(0, fight.EnemyKills);
    }

    [Fact]
    public void Calculate_KillWithNeitherSideKnown_IsLeftOutOfTheFights()
    {
        var roster = Roster(RosterPlayer("r-a1", "A1#1", 100, 1, self: true));
        var snapshot = Snapshot(Player("a1", "A1#1", "ORDER", self: true)) with
        {
            Events = [Kill(1, 100, "无名", "无名2"), Kill(2, 104, "无名", "无名2"), Kill(3, 108, "无名", "无名2")],
        };

        Assert.Empty(Insight(roster, snapshot).Fights);
    }

    [Fact]
    public void Calculate_Fights_TwelveSecondPauseStaysOneFight()
    {
        var insight = Clustered(108, 120);

        Assert.Equal(6, Assert.Single(insight.Fights).OwnKills);
    }

    [Fact]
    public void Calculate_Fights_PauseBeyondTwelveSecondsStartsANewFight()
    {
        var insight = Clustered(108, 120.5);

        Assert.Collection(
            insight.Fights,
            first =>
            {
                Assert.Equal(100, first.Start);
                Assert.Equal(3, first.OwnKills);
            },
            second =>
            {
                Assert.Equal(120.5, second.Start);
                Assert.Equal(3, second.OwnKills);
            }
        );
    }

    [Fact]
    public void Calculate_TwoCloseKills_AreNotAFight()
    {
        var roster = Roster(RosterPlayer("r-a1", "A1#1", 100, 1, self: true));
        var snapshot = Snapshot(Player("a1", "A1#1", "ORDER", self: true)) with
        {
            Events = [Kill(1, 100, "A1#1", "B1#1"), Kill(2, 104, "A1#1", "B1#1")],
        };

        Assert.Empty(Insight(roster, snapshot).Fights);
    }

    [Fact]
    public void Calculate_EventsOutOfOrder_AreSortedBeforeTheFightsAreBuilt()
    {
        var snapshot = SampleSnapshot() with { Events = [.. SampleEvents.Reverse()] };

        var fight = Assert.Single(Insight(SampleRoster(), snapshot).Fights);

        Assert.Equal(100, fight.Start);
        Assert.Equal(2, fight.OwnKills);
        Assert.Equal(1, fight.EnemyKills);
    }

    [Fact]
    public void Calculate_WithoutGameEndEvent_HasEmptyResult()
    {
        var snapshot = SampleSnapshot() with { Events = [.. SampleEvents.Where(entry => entry.Name != "GameEnd")] };

        Assert.Equal("", Insight(SampleRoster(), snapshot).Result);
    }

    [Fact]
    public void Calculate_GameEndWithoutResult_HasEmptyResult()
    {
        var snapshot = Snapshot(Player("a1", "A1#1", "ORDER", self: true)) with
        {
            Events = [new(1, "GameEnd", 600, "", "")],
        };

        Assert.Equal("", Insight(SampleRoster(), snapshot).Result);
    }

    [Fact]
    public void Calculate_MultipleGameEndEvents_UseTheLastOne()
    {
        var snapshot = Snapshot(Player("a1", "A1#1", "ORDER", self: true)) with
        {
            Events =
            [
                new(1, "GameEnd", 500, "", "") { Result = "Lose" },
                new(2, "GameEnd", 600, "", "") { Result = "Win" },
            ],
        };

        Assert.Equal("Win", Insight(SampleRoster(), snapshot).Result);
    }

    /// <summary>Three own kills ending at <paramref name="firstEnd"/>, then three more from <paramref name="last"/>.</summary>
    private static LiveMatchInsight Clustered(double firstEnd, double last)
    {
        var roster = Roster(RosterPlayer("r-a1", "A1#1", 100, 1, self: true));
        var snapshot = Snapshot(Player("a1", "A1#1", "ORDER", self: true)) with
        {
            Events =
            [
                Kill(1, 100, "A1#1", "B1#1"),
                Kill(2, 104, "A1#1", "B1#1"),
                Kill(3, firstEnd, "A1#1", "B1#1"),
                Kill(4, last, "A1#1", "B1#1"),
                Kill(5, last + 4, "A1#1", "B1#1"),
                Kill(6, last + 8, "A1#1", "B1#1"),
            ],
        };
        return Insight(roster, snapshot);
    }
}
