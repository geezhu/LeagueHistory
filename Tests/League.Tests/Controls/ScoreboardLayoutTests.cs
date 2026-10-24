using League.Controls;
using League.Models;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Controls;

/// <summary>
/// The residue of the scoreboard layout check: the header sort, the compact metric page, the rank emblem a ranked
/// game puts on a row and the compact reflow. The scoreboard is driven through its own buttons, so the test sends the
/// window messages Windows would send. A ranked row asks the client for the current rank, but this process has no
/// client, so the lookup answers "unavailable" without opening a socket and only the rank-independent structure is
/// asserted. The expected values come from the checks in Tests/StatisticsChecks/ScoreboardLayoutChecks.cs, which this
/// class replaces.
/// </summary>
[Trait("Category", "UI")]
public class ScoreboardLayoutTests
{
    private static readonly string[] PrimaryMetrics = ["甲", "乙", "丙"];
    private static readonly string[] SecondaryMetrics = ["乙", "乙", "丙"];

    /// <summary>A wide board fits every metric, so both pages are on screen at once.</summary>
    private static readonly string[] WideMetrics = ["甲", "乙", "丙", "乙", "乙", "丙"];

    /// <summary>Each scoreboard metric cell and the participant the fixture makes its leader.</summary>
    private static readonly Dictionary<string, string> MetricLeaders = new()
    {
        ["KdaValues"] = "甲",
        ["goldEarned"] = "乙",
        ["totalDamageDealtToChampions"] = "丙",
        ["killParticipation"] = "乙",
        ["totalCs"] = "乙",
        ["totalDamageTaken"] = "丙",
    };

    [Theory]
    [InlineData("kda", "甲", "乙", "丙")]
    [InlineData("goldEarned", "乙", "丙", "甲")]
    [InlineData("totalDamageDealtToChampions", "甲", "丙", "乙")]
    [InlineData("kp", "乙", "丙", "甲")]
    [InlineData("cs", "乙", "丙", "甲")]
    [InlineData("totalDamageTaken", "丙", "甲", "乙")]
    public void SortHeader_Clicked_ListsTheRowsByThatMetricDescending(
        string key,
        string first,
        string second,
        string third
    )
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);
            await detail.PrepareAsync();
            var section = Section(detail, "甲");
            var header = Header(section, SortHeaderTitle(key));

            ClickHeader(header);

            Assert.Equal(SortHeaderTitle(key) + " ↓", header.Text);
            Assert.Equal([first, second, third], RowNames(section));
        });
    }

    [Fact]
    public void SortHeader_ClickedTwice_ReversesTheOrderUnderAnUpArrow()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);
            await detail.PrepareAsync();
            var section = Section(detail, "甲");
            var header = Header(section, "经济");

            ClickHeader(header);
            Assert.Equal("经济 ↓", header.Text);
            ClickHeader(header);

            Assert.Equal(["甲", "丙", "乙"], RowNames(section));
            Assert.Equal("经济 ↑", header.Text);
        });
    }

    [Fact]
    public void SortHeader_SwitchedToAnotherMetric_LeavesTheArrowOnTheActiveHeadingOnly()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);
            await detail.PrepareAsync();
            var section = Section(detail, "甲");

            ClickHeader(Header(section, "经济"));
            ClickHeader(Header(section, "英雄伤害"));

            Assert.Equal(["甲", "丙", "乙"], RowNames(section));
            Assert.Equal(
                ["英雄伤害 ↓", "KDA", "经济", "参团率", "补刀", "承受伤害"],
                SortingHeaderTexts(section, "英雄伤害")
            );
        });
    }

    [Theory]
    [InlineData(900, true)]
    [InlineData(1300, false)]
    public void ViewWidth_BoardThatFitsTheMetrics_OffersTheMetricPageOnlyWhenCompact(int width, bool expected)
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);
            host.ClientSize = new Size(width, 640);
            await detail.PrepareAsync();
            var section = Section(detail, "甲");

            Assert.Equal(width, detail.Width);
            Assert.Equal(expected, MetricPage(detail, "甲").Visible);
            Assert.Equal(expected, section.Width < 1120);
        });
    }

    [Theory]
    [InlineData("goldEarned")]
    [InlineData("totalDamageDealtToChampions")]
    public void MetricPage_Clicked_SwapsTheMetricsWithoutMovingThem(string key)
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);
            host.ClientSize = new Size(900, 640);
            await detail.PrepareAsync();
            var row = Row(detail, "甲");
            var page = MetricPage(detail, "甲");
            int primaryX = row.Controls[key]!.Left;

            WindowMessages.Click(page, new Point(4, 4));

            Assert.False(row.Controls[key]!.Visible);
            Assert.Contains("乙", VisibleMetrics(row));
            Assert.Equal(primaryX, row.Controls[key]!.Left);
        });
    }

    [Fact]
    public void MetricPage_ClickedTwice_RestoresThePrimaryMetrics()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);
            host.ClientSize = new Size(900, 640);
            await detail.PrepareAsync();
            var row = Row(detail, "甲");
            var page = MetricPage(detail, "甲");
            int primaryX = row.Controls["goldEarned"]!.Left;
            int secondaryX = row.Controls["killParticipation"]!.Left;

            WindowMessages.Click(page, new Point(4, 4));
            WindowMessages.Click(page, new Point(4, 4));

            Assert.True(row.Controls["goldEarned"]!.Visible);
            Assert.False(row.Controls["killParticipation"]!.Visible);
            Assert.Equal(primaryX, row.Controls["goldEarned"]!.Left);
            Assert.Equal(secondaryX, row.Controls["killParticipation"]!.Left);
        });
    }

    [Fact]
    public void PreparedScoreboard_CompactBoard_KeepsTheMetricsOnTwoPages()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);
            host.ClientSize = new Size(900, 640);
            await detail.PrepareAsync();
            var row = Row(detail, "甲");
            var page = MetricPage(detail, "甲");

            Assert.Equal("›", page.Text);
            Assert.Equal(PrimaryMetrics, VisibleMetrics(row));

            WindowMessages.Click(page, new Point(4, 4));

            Assert.Equal("‹", page.Text);
            Assert.Equal(SecondaryMetrics, VisibleMetrics(row));
        });
    }

    [Fact]
    public void HostWidth_Widened_RestoresTheWideMetricLayout()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);
            host.ClientSize = new Size(900, 640);
            await detail.PrepareAsync();
            var row = Row(detail, "甲");

            host.ClientSize = new Size(1300, 640);
            host.PerformLayout();
            detail.Refresh();

            Assert.Equal(686, row.Controls["goldEarned"]!.Left);
            Assert.Equal(180, row.Controls["PlayerName"]!.Width);
            Assert.False(MetricPage(detail, "甲").Visible);
            Assert.Equal(WideMetrics, VisibleMetrics(row));
        });
    }

    [Fact]
    public void PrepareAsync_RankedGameWithAPuuid_PlacesTheRankEmblemBesideTheName()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            match.QueueId = "q_420";
            var detail = View(host, match);

            await detail.PrepareAsync();

            var row = Row(detail, "甲");
            var emblem = row.Controls.OfType<RankEmblem>().Single();
            Assert.Equal(new Rectangle(206, 5, 30, 28), emblem.Bounds);
            Assert.Equal(144, row.Controls["PlayerName"]!.Width);
        });
    }

    [Fact]
    public void PrepareAsync_MayhemGame_PutsAScoreEstimateOnEveryRow()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            match.QueueId = "q_2400";
            var detail = View(host, match);

            await detail.PrepareAsync();

            Assert.All(
                ["甲", "乙", "丙", "丁"],
                player => Assert.NotNull(Row(detail, player).Controls["ScoreEstimate"])
            );
        });
    }

    [Fact]
    public void PrepareAsync_WideMayhemBoard_PutsTheScoreRightOfTheMetrics()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            match.QueueId = "q_2400";
            var detail = View(host, match);
            host.ClientSize = new Size(1400, 800);

            await detail.PrepareAsync();

            var row = Row(detail, "甲");
            Assert.Equal(row.Controls["totalDamageTaken"]!.Right + 8, row.Controls["ScoreEstimate"]!.Left);
            Assert.False(MetricPage(detail, "甲").Visible);
        });
    }

    [Fact]
    public void PrepareAsync_CompactMayhemBoard_PinsTheScoreToTheRightEdge()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            match.QueueId = "q_2400";
            var detail = View(host, match);
            host.ClientSize = new Size(900, 640);

            await detail.PrepareAsync();

            var row = Row(detail, "甲");
            Assert.True(MetricPage(detail, "甲").Right <= row.Controls["ScoreEstimate"]!.Left);
            Assert.True(row.Controls["ScoreEstimate"]!.Right <= row.Width);
        });
    }

    [Fact]
    public void PrepareAsync_CompactMayhemBoard_GivesKdaItsFullWidth()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            match.QueueId = "q_2400";
            var detail = View(host, match);
            host.ClientSize = new Size(900, 640);

            await detail.PrepareAsync();

            Assert.Equal(90, Row(detail, "甲").Controls["KdaValues"]!.Width);
        });
    }

    [Fact]
    public void MetricPage_ClickedOnAMayhemBoard_KeepsTheScoreInPlace()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            match.QueueId = "q_2400";
            var detail = View(host, match);
            host.ClientSize = new Size(900, 640);
            await detail.PrepareAsync();
            var score = Row(detail, "甲").Controls["ScoreEstimate"]!;
            int left = score.Left;

            WindowMessages.Click(MetricPage(detail, "甲"), new Point(4, 4));

            Assert.True(score.Visible);
            Assert.Equal(left, score.Left);
        });
    }

    [Fact]
    public void PrepareAsync_SummonersRiftGame_HasNoScoreEstimate()
    {
        UiHost.Run(async host =>
        {
            using var match = Fixture();
            var detail = View(host, match);

            await detail.PrepareAsync();

            Assert.Null(Row(detail, "甲").Controls["ScoreEstimate"]);
        });
    }

    private static string SortHeaderTitle(string key) =>
        key switch
        {
            "kda" => "KDA",
            "goldEarned" => "经济",
            "totalDamageDealtToChampions" => "英雄伤害",
            "kp" => "参团率",
            "cs" => "补刀",
            _ => "承受伤害",
        };

    /// <summary>The scoreboard team section holding a player.</summary>
    private static Panel Section(Control detail, string player) =>
        Row(detail, player).Parent as Panel ?? throw new InvalidOperationException("the row has no team section");

    /// <summary>The scoreboard row holding a player, addressed by the name part of the Riot ID.</summary>
    private static Control Row(Control detail, string player) =>
        Descendants(detail).OfType<PlayerNameLink>().Single(link => link.PlayerName == player + "#0001").Parent!;

    private static Button Header(Control section, string title) =>
        section.Controls.OfType<Button>().Single(button => button.Text.TrimEnd(' ', '↑', '↓') == title);

    /// <summary>
    /// Clicks a header and settles the queue the message went through. The control tree is built from an awaited
    /// prepare, so the click is the first thing the message loop sees; draining it keeps the delivery deterministic
    /// when another class left messages behind.
    /// </summary>
    private static void ClickHeader(Button header)
    {
        WindowMessages.Click(header, new Point(4, 4));
        Application.DoEvents();
        header.Update();
    }

    /// <summary>The sortable headings in board order, with the active one named first.</summary>
    private static string[] SortingHeaderTexts(Control section, string active) =>
        [
            .. section
                .Controls.OfType<Button>()
                .Where(button => button.Name != "MetricPage" && button.Cursor == Cursors.Hand)
                .OrderByDescending(button => button.Text.TrimEnd(' ', '↑', '↓') == active)
                .ThenBy(button => button.Left)
                .Select(button => button.Text),
        ];

    private static Button MetricPage(Control detail, string player) =>
        (Button)Section(detail, player).Controls["MetricPage"]!;

    /// <summary>The players of a team, read top to bottom.</summary>
    private static string[] RowNames(Control section) =>
        [
            .. section
                .Controls.OfType<Panel>()
                .Where(row => row.Controls.OfType<PlayerNameLink>().Any())
                .OrderBy(row => row.Top)
                .Select(row => row.Controls.OfType<PlayerNameLink>().Single().PlayerName.Split('#')[0]),
        ];

    /// <summary>The metric a viewer can currently read from a row, in board order, named by its leader.</summary>
    private static string[] VisibleMetrics(Control row) =>
        [
            .. row
                .Controls.OfType<Control>()
                .Where(control => control.Visible && MetricLeaders.ContainsKey(control.Name))
                .OrderBy(control => control.Left)
                .Select(control => MetricLeaders[control.Name]),
        ];

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private static MatchDetailView View(Form host, MatchInfo match)
    {
        host.ClientSize = new Size(1300, 800);
        var detail = new MatchDetailView(match) { Dock = DockStyle.Fill };
        host.Controls.Add(detail);
        return detail;
    }

    /// <summary>One game whose blue team holds three players, each leading a different column.</summary>
    private static MatchInfo Fixture() =>
        new()
        {
            Index = 1,
            GameId = 7412345678L,
            CurrPuuid = "puuid-甲",
            ResultText = "胜利",
            Mode = "召唤师峡谷",
            QueueId = "q_400",
            GameTime = "2026-09-08 01:54",
            DurationText = "22分5秒",
            AllParticipants =
            [
                Participant(100, "甲", 9000, 20000, 15000, 30, 0.4, 2, 1),
                Participant(100, "乙", 15000, 9000, 12000, 60, 0.7, 9, 3),
                Participant(100, "丙", 10000, 12000, 30000, 45, 0.5, 3, 5),
                Participant(200, "丁", 8000, 10000, 9000, 20, 0.2, 1, 9),
            ],
            RawGameData = new JObject
            {
                ["teams"] = new JArray(
                    new JObject { ["teamId"] = 100, ["bans"] = new JArray(new JObject { ["championId"] = 12 }) }
                ),
            },
        };

    private static JObject Participant(
        int teamId,
        string name,
        int gold,
        int damage,
        int damageTaken,
        int minions,
        double participation,
        int kills,
        int deaths
    ) =>
        new()
        {
            ["participantId"] = name,
            ["teamId"] = teamId,
            ["puuid"] = "puuid-" + name,
            ["summonerId"] = name,
            ["riotIdGameName"] = name,
            ["riotIdTagline"] = "0001",
            ["championId"] = 1,
            ["championName"] = "拉克丝",
            ["kills"] = kills,
            ["deaths"] = deaths,
            ["assists"] = 10,
            ["goldEarned"] = gold,
            ["totalDamageDealtToChampions"] = damage,
            ["totalDamageTaken"] = damageTaken,
            ["timeCCingOthers"] = 0,
            ["totalMinionsKilled"] = minions,
            ["neutralMinionsKilled"] = 0,
            ["win"] = true,
            ["spell1Id"] = 4,
            ["spell2Id"] = 14,
            ["challenges"] = new JObject { ["killParticipation"] = participation },
        };
}
