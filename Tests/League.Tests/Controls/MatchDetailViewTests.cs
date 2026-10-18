using League.Controls;
using League.Models;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Controls;

/// <summary>
/// The opened match detail: the header, the scoreboard it builds from the game's participants and the four tabs.
/// The game is a hand-written document, so the view is driven end to end without a client or a network read. The
/// expected values come from the checks in Tests/StatisticsChecks/MatchDetailChecks.cs, which this class replaces.
/// </summary>
[Trait("Category", "UI")]
public class MatchDetailViewTests
{
    [Fact]
    public void PrepareAsync_GameWithParticipants_BuildsTheScoreboardSurface()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);

            await detail.PrepareAsync();

            Assert.NotEmpty(Descendants(detail));
        });
    }

    [Fact]
    public void PrepareAsync_GameWithParticipants_NamesEveryParticipantOnTheScoreboard()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);

            await detail.PrepareAsync();

            // No client is consulted: both halves of the Riot ID are already in the game document.
            Assert.Equal(["甲#0001", "乙#0001"], PlayerNames(detail));
        });
    }

    [Theory]
    [InlineData("甲#0001", "12 / 2 / 18")]
    [InlineData("乙#0001", "3 / 9 / 4")]
    public void PrepareAsync_GameWithParticipants_ShowsTheKillDeathAssistColumn(string player, string expected)
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);

            await detail.PrepareAsync();

            Assert.Equal(expected, Metric(detail, player, "KdaValues").Text);
        });
    }

    [Theory]
    [InlineData("甲#0001", "12.3k")]
    [InlineData("乙#0001", "8.0k")]
    public void PrepareAsync_GameWithParticipants_ShowsTheGoldColumn(string player, string expected)
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);

            await detail.PrepareAsync();

            Assert.Equal(expected, Metric(detail, player, "goldEarned").Text);
        });
    }

    [Fact]
    public void HostClientSize_Changed_DetailViewFollowsTheHostWidth()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);
            await detail.PrepareAsync();

            using var wide = new Bitmap(1300, 800);
            detail.DrawToBitmap(wide, detail.ClientRectangle);
            Assert.Equal(1300, detail.Width);

            host.ClientSize = new Size(900, 640);
            using var narrow = new Bitmap(900, 640);
            detail.DrawToBitmap(narrow, detail.ClientRectangle);

            Assert.Equal(900, detail.Width);
        });
    }

    private static string[] PlayerNames(Control detail) =>
        [.. Descendants(detail).OfType<PlayerNameLink>().Select(link => link.PlayerName)];

    /// <summary>The label a scoreboard column wrote into the participant's row.</summary>
    private static Label Metric(Control detail, string player, string name) =>
        Row(detail, player).Controls.OfType<Label>().Single(label => label.Name == name);

    private static Control Row(Control detail, string player) =>
        Descendants(detail).OfType<PlayerNameLink>().Single(link => link.PlayerName == player).Parent!;

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private static MatchDetailView View(Form host, MatchInfo match)
    {
        host.ClientSize = new Size(1300, 800);
        var detail = new MatchDetailView(match) { Dock = DockStyle.Fill };
        host.Controls.Add(detail);
        return detail;
    }

    /// <summary>One game with two participants, one of them the player whose history was opened.</summary>
    private static MatchInfo Fixture() =>
        new()
        {
            Index = 1,
            GameId = 7412345678L,
            CurrPuuid = "self-puuid-0001",
            CurrSummonerId = "7",
            ResultText = "胜利",
            Mode = "召唤师峡谷",
            QueueId = "q_420",
            GameTime = "2026-09-08 01:54",
            DurationText = "22分5秒",
            Kills = 12,
            Deaths = 2,
            Assists = 18,
            GoldText = "12.3K |1",
            DamageText = "20K |1",
            KPPercentage = "75% |1",
            DamageTakenText = "15K |1",
            ChampionName = "拉克丝",
            HeroIcon = new Bitmap(8, 8),
            Items = [],
            PrimaryRunes = [],
            SecondaryRunes = [],
            StatRunes = [],
            AllParticipants = [Participant(100, 7, "甲", true), Participant(200, 8, "乙", false)],
            RawGameData = Game(),
        };

    private static JObject Game() =>
        new()
        {
            ["gameId"] = 7412345678L,
            ["gameDuration"] = 1325,
            ["gameMode"] = "CLASSIC",
            ["queueId"] = 420,
            ["gameStartTimestamp"] = 1767225600000L,
            ["participants"] = new JArray(Participant(100, 7, "甲", true), Participant(200, 8, "乙", false)),
            ["teams"] = new JArray(
                new JObject
                {
                    ["teamId"] = 100,
                    ["win"] = true,
                    ["bans"] = new JArray(new JObject { ["championId"] = 1 }),
                    ["objectives"] = new JObject
                    {
                        ["baron"] = new JObject { ["kills"] = 1 },
                        ["dragon"] = new JObject { ["kills"] = 3 },
                        ["tower"] = new JObject { ["kills"] = 8 },
                    },
                },
                new JObject
                {
                    ["teamId"] = 200,
                    ["win"] = false,
                    ["bans"] = new JArray(new JObject { ["championId"] = 2 }),
                }
            ),
        };

    private static JObject Participant(int teamId, long summonerId, string name, bool self) =>
        new()
        {
            ["participantId"] = teamId == 100 ? 1 : 6,
            ["teamId"] = teamId,
            ["summonerId"] = summonerId,
            ["puuid"] = self ? "self-puuid-0001" : $"other-puuid-000{summonerId}",
            ["riotIdGameName"] = name,
            ["riotIdTagline"] = "0001",
            ["championId"] = 1,
            ["championName"] = "拉克丝",
            ["kills"] = self ? 12 : 3,
            ["deaths"] = self ? 2 : 9,
            ["assists"] = self ? 18 : 4,
            ["goldEarned"] = self ? 12345 : 8000,
            ["totalDamageDealtToChampions"] = self ? 20000 : 9000,
            ["totalDamageTaken"] = self ? 15000 : 22000,
            ["totalMinionsKilled"] = self ? 35 : 12,
            ["neutralMinionsKilled"] = 0,
            ["win"] = self,
            ["spell1Id"] = 4,
            ["spell2Id"] = 14,
            ["firstBloodKill"] = self,
            ["pentaKills"] = 0,
            ["challenges"] = new JObject { ["killParticipation"] = self ? 0.75 : 0.4 },
            ["perks"] = new JObject
            {
                ["statPerks"] = new JObject
                {
                    ["offense"] = 5008,
                    ["flex"] = 5008,
                    ["defense"] = 5002,
                },
                ["styles"] = new JArray(
                    new JObject
                    {
                        ["description"] = "primaryStyle",
                        ["selections"] = new JArray(new JObject { ["perk"] = 8005 }, new JObject { ["perk"] = 9101 }),
                    },
                    new JObject
                    {
                        ["description"] = "subStyle",
                        ["selections"] = new JArray(new JObject { ["perk"] = 8226 }),
                    }
                ),
            },
        };
}
