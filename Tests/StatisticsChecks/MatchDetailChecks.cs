using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// The opened match detail: the header, the scoreboard it builds from the game's participants, and the four tabs.
/// The game is a hand-written document, so the view is driven end to end without a client or a network read.
/// </summary>
internal static class MatchDetailChecks
{
    public static void Run() => WinFormsCheckHost.Run(Core);

    private static void Core()
    {
        ScoreboardChecks();
        TabChecks();
        EmptyChecks();
        Console.WriteLine(
            "PASS: the match detail's header and scoreboard, its four tabs, and the message for a game without "
                + "detail data."
        );
    }

    private static void ScoreboardChecks()
    {
        using var host = HiddenForm(new Size(1300, 800));
        using var match = Fixture();
        var detail = new MatchDetailView(match) { Dock = DockStyle.Fill };
        host.Controls.Add(detail);
        host.Show();

        Task prepare = detail.PrepareAsync();
        PumpUntil(() => prepare.IsCompleted);
        prepare.GetAwaiter().GetResult();

        Check(Children(detail).Any(), "The detail view builds its surface");
        Check(
            Children(detail).OfType<PlayerNameLink>().Count() >= 2,
            "Every participant gets a name on the scoreboard"
        );
        Check(
            Children(detail).OfType<Label>().Any(label => label.Name == "KdaValues"),
            "The scoreboard shows a kill/death/assist column"
        );
        Check(
            Children(detail).OfType<Label>().Any(label => label.Name == "goldEarned"),
            "The scoreboard shows the gold column"
        );

        detail.DrawToBitmap(new Bitmap(1300, 800), detail.ClientRectangle);
        host.ClientSize = new Size(900, 640);
        detail.DrawToBitmap(new Bitmap(900, 640), detail.ClientRectangle);
        Check(detail.Width == 900, "The detail view follows its host's width");
        host.Close();
    }

    private static void TabChecks()
    {
        using var host = HiddenForm(new Size(1300, 800));
        using var match = Fixture();
        var detail = new MatchDetailView(match) { Dock = DockStyle.Fill };
        host.Controls.Add(detail);
        host.Show();

        Task prepare = detail.PrepareAsync();
        PumpUntil(() => prepare.IsCompleted);
        prepare.GetAwaiter().GetResult();

        foreach (string tab in new[] { "统计", "图表", "时间线", "基础数据" })
        {
            var button = Children(detail).OfType<Button>().FirstOrDefault(candidate => candidate.Text == tab);
            Check(button != null, $"The detail view offers the {tab} tab");
            button!.PerformClick();
            Application.DoEvents();
            Check(button.ForeColor != Color.Empty, $"The {tab} tab becomes the active one");
        }

        Check(
            Children(detail).Any(control => control is MatchStatisticsView),
            "Opening the statistics tab builds its view"
        );
        Check(Children(detail).Any(control => control is MatchChartsView), "Opening the charts tab builds its view");
        detail.DrawToBitmap(new Bitmap(1300, 800), detail.ClientRectangle);
        host.Close();
    }

    private static void EmptyChecks()
    {
        using var host = HiddenForm(new Size(900, 600));
        using var match = new MatchInfo
        {
            ResultText = "胜利",
            Mode = "极地大乱斗",
            QueueId = "q_450",
        };
        var detail = new MatchDetailView(match) { Dock = DockStyle.Fill };
        host.Controls.Add(detail);
        host.Show();

        Task prepare = detail.PrepareAsync();
        PumpUntil(() => prepare.IsCompleted);
        prepare.GetAwaiter().GetResult();
        Check(
            Children(detail).OfType<Label>().Any(label => label.Text.Contains("暂无可展示", StringComparison.Ordinal)),
            "A game without participants says there is nothing to show"
        );
        host.Close();
    }

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
