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
    private static readonly string[] Tabs = ["统计", "图表", "时间线", "基础数据"];
    private static readonly Color ActiveTabInk = Color.FromArgb(46, 103, 190);
    private static readonly Color IdleTabInk = Color.FromArgb(111, 123, 139);

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

    [Theory]
    [InlineData("统计")]
    [InlineData("图表")]
    [InlineData("时间线")]
    [InlineData("基础数据")]
    public void PrepareAsync_GameWithParticipants_OffersTheTab(string tab)
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);
            await detail.PrepareAsync();

            int offered = Descendants(detail).OfType<Button>().Count(button => button.Text == tab);

            Assert.Equal(1, offered);
        });
    }

    [Theory]
    [InlineData("统计")]
    [InlineData("图表")]
    [InlineData("时间线")]
    [InlineData("基础数据")]
    public void Tab_Clicked_BecomesTheActiveTab(string tab)
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);
            await detail.PrepareAsync();
            var button = TabButton(detail, tab);
            var others = TabButtons(detail).Where(other => other != button).ToArray();

            WindowMessages.Click(button, new Point(4, 4));

            Assert.Equal(ActiveTabInk, button.ForeColor);
            Assert.All(others, other => Assert.Equal(IdleTabInk, other.ForeColor));
        });
    }

    [Fact]
    public void StatisticsTab_Clicked_BuildsTheStatisticsView()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);
            await detail.PrepareAsync();

            WindowMessages.Click(TabButton(detail, "统计"), new Point(4, 4));

            Assert.Single(Descendants(detail).OfType<MatchStatisticsView>());
        });
    }

    [Fact]
    public void ChartsTab_Clicked_BuildsTheChartsView()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);
            await detail.PrepareAsync();

            WindowMessages.Click(TabButton(detail, "图表"), new Point(4, 4));

            Assert.Single(Descendants(detail).OfType<MatchChartsView>());
        });
    }

    [Fact]
    public void TimelineTab_Clicked_BuildsTheTimelineView()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);
            await detail.PrepareAsync();

            WindowMessages.Click(TabButton(detail, "时间线"), new Point(4, 4));

            Assert.Single(Descendants(detail).OfType<TimelineView>());
        });
    }

    [Fact]
    public void PrepareAsync_GameWhereBothTeamsBanned_OffersTheBanListOfEachTeam()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);
            await detail.PrepareAsync();

            // The bans of a team are drawn only because its entry in RawGameData["teams"] carries them.
            int lists = Descendants(detail).OfType<Button>().Count(button => button.Name == "TeamBans");

            Assert.Equal(2, lists);
        });
    }

    [Fact]
    public void PrepareAsync_GameWithParticipants_ShowsACardPerHighlightTheGameCarries()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);
            await detail.PrepareAsync();

            var strip = Descendants(detail).OfType<FlowLayoutPanel>().Single(panel => panel.Name == "MatchHighlights");

            // Damage dealt and damage taken are integers in the game document; it carries no largestKillingSpree.
            Assert.Equal(2, strip.Controls.Count);
        });
    }

    [Theory]
    [InlineData("时间", "09-08 01:54")]
    [InlineData("类型", "召唤师峡谷")]
    [InlineData("地图", "未知地图")]
    [InlineData("比赛时长", "22分5秒")]
    [InlineData("击杀", "12 / 3")]
    public void PrepareAsync_GameWithParticipants_ShowsTheHeaderValue(string caption, string expected)
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);
            await detail.PrepareAsync();

            Assert.Equal(expected, HeaderValue(detail, caption));
        });
    }

    [Fact]
    public void PrepareAsync_GameWithoutParticipants_SaysThereIsNothingToShow()
    {
        UiHost.Run(async host =>
        {
            using var match = new MatchInfo
            {
                ResultText = "胜利",
                Mode = "极地大乱斗",
                QueueId = "q_450",
            };
            var detail = View(host, match);

            await detail.PrepareAsync();
            string[] texts = [.. Descendants(detail).OfType<Label>().Select(label => label.Text)];

            Assert.Contains("这场比赛暂无可展示的详细数据。", texts);
        });
    }

    [Fact]
    public void ReplayButton_Clicked_RaisesTheReplayRequest()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);
            await detail.PrepareAsync();
            int requests = 0;
            detail.ReplayRequested += () => requests++;

            WindowMessages.Click(
                Descendants(detail).OfType<Button>().Single(button => button.Text == "查看回放"),
                new Point(4, 4)
            );

            Assert.Equal(1, requests);
        });
    }

    private static Button TabButton(Control detail, string tab) =>
        Descendants(detail).OfType<Button>().Single(button => button.Text == tab);

    private static IEnumerable<Button> TabButtons(Control detail) =>
        Descendants(detail).OfType<Button>().Where(button => Tabs.Contains(button.Text));

    /// <summary>The value cell of a header column, addressed by the caption above it.</summary>
    private static string HeaderValue(Control detail, string caption) =>
        Descendants(detail)
            .OfType<Panel>()
            .Single(cell =>
                cell.Controls.OfType<Label>().Any(label => label.Text == caption && label.Dock == DockStyle.Top)
            )
            .Controls.OfType<Label>()
            .Single(label => label.Dock == DockStyle.Fill)
            .Text;

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
