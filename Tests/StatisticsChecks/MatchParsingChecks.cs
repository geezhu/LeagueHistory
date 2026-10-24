using System.Reflection;
using League.Controls;
using League.Models;
using League.Parsers;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// The two match parsers. Both read a game document and build a row from it; the resource tables are left empty,
/// so every icon lookup misses without any file or network access and the values below are the parser's own.
/// </summary>
internal static class MatchParsingChecks
{
    public static async Task RunAsync()
    {
        await HistoryRowChecks();
        await HistoryCancellationChecks();
        await HistoryFailureChecks();
        ModeTableChecks();
        await SgpPanelChecks();
        await SgpFailureChecks();
        SgpModeChecks();
        Console.WriteLine(
            "PASS: history rows, the queue-to-mode table, the SGP panel's summary, runes, items and augments, "
                + "and both parsers' missing-data paths."
        );
    }

    private static async Task HistoryRowChecks()
    {
        Check(
            (await Parser.ParsePlayerMatchInfo("self-puuid-0001", null!)).RecentMatches.Count == 0,
            "A missing history is empty"
        );
        Check(
            (await Parser.ParsePlayerMatchInfo("self-puuid-0001", new JArray())).RecentMatches.Count == 0,
            "An empty history is empty"
        );

        var history = await Parser.ParsePlayerMatchInfo("self-puuid-0001", History());
        Check(history.RecentMatches.Count == 2, "Only the games the player took part in are read");
        Check(history.RecentMatches[0].GameDurationMinutes == 22, "The duration is read in whole minutes");
        Check(history.WinHistory.SequenceEqual([true, false]), "Every game records whether it was won");
        Check(history.MatchKeys.SequenceEqual(["7412345678_100", "2_100"]), "Every key is the game and the team");
        Check(history.MatchItems[0].ImageKey == "ParseCheckChampion", "The champion names the icon key");
        Check(history.MatchItems[0].ForeColor == Color.Green, "A win is shown in green");
        Check(history.MatchItems[1].ForeColor == MatchResult.RemakeColor, "A remake has its own colour");
        Check(history.MatchItems[0].SubItems[1].Text == "单双排", "The row names the queue's mode");
        Check(history.MatchItems[0].SubItems[2].Text == "10/2/7", "The row shows the score");
        Check(history.MatchItems[0].SubItems[3].Text == "2026-01-01", "The row shows the day the game started");
        Check(((MatchMetadata)history.MatchItems[0].Tag!).GameId == 7412345678L, "The row carries its game id");
        Check(((MatchMetadata)history.MatchItems[0].Tag!).TeamId == 100, "The row carries the team");
        Check(
            ((MatchMetadata)history.MatchItems[0].Tag!).GameData?["gameId"] != null,
            "The row carries its own copy of the game"
        );
        Check(history.HeroIcons.Images.Count == 0, "An icon that cannot be loaded is simply left out");
        Check(history.Player == null, "The player is filled in by the caller, not by the history read");

        var quirk = await Parser.ParsePlayerMatchInfo("self-puuid-0001", new JArray(Match(0L), Matchless()));
        Check(
            quirk.MatchKeys.SequenceEqual(["0_-1"]),
            "An explicit zero game id is skipped while a missing one is kept without a team"
        );

        var nested = new JArray(JObject.Parse("""{"json":{"info":{"participants":[{"puuid":"self-puuid-0001"}]}}}"""));
        Check(
            (await Parser.ParsePlayerMatchInfo("self-puuid-0001", nested)).RecentMatches.Count == 0,
            "Only a participants array beside the game fields is read"
        );
    }

    private static async Task HistoryCancellationChecks()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        bool stopped = false;
        try
        {
            await Parser.ParsePlayerMatchInfo("self-puuid-0001", History(), cancelled.Token);
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }

        Check(stopped, "A cancelled read stops instead of returning a partial history");
    }

    private static async Task HistoryFailureChecks()
    {
        var loader = League.FormMain.Globals.resLoading;
        try
        {
            League.FormMain.Globals.resLoading = null!;
            var failed = await Parser.ParsePlayerMatchInfo("self-puuid-0001", History());
            Check(failed.RecentMatches.Count == 1, "A failure while reading one game gives up on the games after it");
            Check(failed.MatchItems.Count == 1, "The game that failed still leaves its row behind");
        }
        finally
        {
            League.FormMain.Globals.resLoading = loader;
        }
    }

    private static void ModeTableChecks()
    {
        var expected = new (int Queue, string Mode)[]
        {
            (420, "单双排"),
            (440, "灵活排位"),
            (400, "匹配征召"),
            (430, "匹配盲选"),
            (450, "大乱斗"),
            (900, "无限乱斗"),
            (1900, "自选无限火力"),
            (480, "快速模式"),
            (1700, "斗魂竞技场"),
            (2400, "海克斯乱斗"),
            (2450, "经典海克斯乱斗"),
            (1020, "克隆大作战"),
            (1300, "极限闪击"),
            (1400, "终极魔典"),
            (700, "冠军杯赛"),
            (830, "人机入门"),
            (840, "人机新手"),
            (850, "人机中等"),
            (870, "人机入门"),
            (880, "人机新手"),
            (890, "人机中等"),
            (2000, "新手教程1"),
            (2010, "新手教程2"),
            (2020, "新手教程3"),
            (1090, "云顶匹配"),
            (1100, "云顶排位"),
            (1130, "云顶疾速"),
            (1160, "云顶双人"),
            (3100, "自定义峡谷"),
            (3140, "训练模式"),
            (3270, "自定义海克斯"),
            (4310, "经典峡谷"),
            (4320, "经典人机"),
            (950, "末日人机"),
            (960, "末日人机"),
        };
        foreach (var (queue, mode) in expected)
        {
            Check(ModeFor(queue) == mode, $"Queue {queue} is named {mode}");
        }

        Check(ModeFor(0) == "未知(queueId=0)", "Queue zero cannot name a mode on its own");
        Check(ModeFor(99999) == "未知(queueId=99999)", "An unknown queue keeps its number");
        Check(Tags(null) == "未知模式" && Tags(new JArray()) == "未知模式", "No tags means no mode");
    }

    private static async Task SgpPanelChecks()
    {
        var game = Game(2400, championId: 103);
        var panel = (MatchListPanel)
            await new MatchParserSgp().ParseGameToPanelFromSgpAsync(game, "12345", "测试玩家#0001", "0001", 3);
        Check(game.Count == 0, "The parser releases the game document it was given");
        Check(panel.MatchInfo.BlueTeam.Count == 0, "A parsed row loads its teams only for a full row");
        // Spells, runes, items, augments and teams load when a full row first paints.
        await panel.EnsureFullRowAsync();

        var info = panel.MatchInfo;
        Check(info.Index == 3, "The row keeps the index it was given");
        Check(info.GameId == 7412345678L, "The game id is read");
        Check(
            info.CurrPuuid == "self-puuid-0001" && info.CurrSummonerId == "12345",
            "The player's identities are read"
        );
        Check(info.ResultText == "胜利", "A win says so");
        Check(info.DurationText == "22分5秒", "The duration is written out in minutes and seconds");
        Check(info.Mode == "海克斯乱斗" && info.QueueId == "q_2400", "The queue names the mode");
        Check(info.Kills == 10 && info.Deaths == 2 && info.Assists == 7, "The score is read");
        Check(
            info.DamageText.StartsWith("20K", StringComparison.Ordinal)
                && info.GoldText.StartsWith("12K", StringComparison.Ordinal)
                && info.KPPercentage.StartsWith("50%", StringComparison.Ordinal),
            "Damage, gold and kill participation are ranked against the other players"
        );
        Check(
            info.ChampionName == "未知英雄",
            "A champion the resource table does not know keeps its placeholder name"
        );
        Check(info.StatRunes.Length == 3, "A player without rune pages still has three stat runes");
        Check(info.AllPlayersAugments.ContainsKey("self-puuid-0001"), "Every player gets an augment entry");
        Check(info.BlueTeam.Count == 1 && info.BlueTeam[0].IsSelf, "The row lists our own team and marks us");
        Check(info.RedTeam.Count == 0, "The enemy team is empty when only we are in the game");
        Check(info.RawGameData != null, "The whole game is kept for the detail view");

        var rich = (MatchListPanel)
            await new MatchParserSgp().ParseGameToPanelFromSgpAsync(RichGame(), "12345", "测试玩家#0001", "0001", 0);
        await rich.EnsureFullRowAsync();
        var detailed = rich.MatchInfo;
        Check(detailed.PrimaryRunes.Length == 4, "The primary rune page is read");
        Check(detailed.SecondaryRunes.Length == 2, "The secondary rune page is read");
        Check(
            detailed.StatRunes.Select(rune => rune.id).OrderBy(id => id).SequenceEqual([5002, 5008, 5008]),
            "The three stat runes are read in page order"
        );
        Check(detailed.Items.Length == 2, "Only the item slots that hold an item are kept");
        Check(detailed.Augments.Length == 1 && detailed.Augments[0].id == 1234, "The player's augments are read");
        Check(
            detailed.Augments[0].longDesc.Contains("图片未加载", StringComparison.Ordinal),
            "An augment without artwork says the picture is missing"
        );
    }

    private static async Task SgpFailureChecks()
    {
        var parser = new MatchParserSgp();
        Check(
            (await parser.ParseGameToPanelFromSgpAsync(null!, "1", "name", "tag", 0)).Size == new Size(0, 0),
            "A missing game yields the empty placeholder panel"
        );
        Check(
            (await parser.ParseGameToPanelFromSgpAsync(new JObject(), "1", "name", "tag", 0)).Size == new Size(0, 0),
            "A game without participants yields the empty placeholder panel"
        );
        Check(
            (
                await parser.ParseGameToPanelFromSgpAsync(
                    JObject.Parse("""{"json":5,"participants":[]}"""),
                    "1",
                    "name",
                    "tag",
                    0
                )
            ).Size == new Size(0, 0),
            "A game whose json field is not an object yields the empty placeholder panel"
        );

        var stray = Game(420, championId: 103);
        stray["participants"]![0]!["puuid"] = null;
        Check(
            (await parser.ParseGameToPanelFromSgpAsync(stray, "999", "别的玩家#0001", "0001", 0)).Size
                == new Size(0, 0),
            "A game without a puuid cannot be turned into a row"
        );
    }

    private static void SgpModeChecks()
    {
        Check(SgpMode(new JObject { ["queueId"] = 2400 }) == ("海克斯乱斗", "q_2400"), "A known queue names its mode");
        Check(
            SgpMode(new JObject { ["gameType"] = "CUSTOM_GAME", ["gameMode"] = "CLASSIC" }) == ("自定义峡谷", ""),
            "A custom Summoner's Rift game is named from its mode"
        );
        Check(
            SgpMode(new JObject { ["queueId"] = 99999, ["gameMode"] = "ARAM" }) == ("大乱斗", "q_99999"),
            "An unknown queue falls back to the game mode"
        );
        Check(
            SgpMode(new JObject { ["queueId"] = 99999 }) == ("未知模式(99999)", "q_99999"),
            "A queue and a mode that are both unknown keep the queue number"
        );
    }

    private static (string Mode, string QueueId) SgpMode(JObject game) =>
        ((string, string))SgpExtractGameMode.Invoke(new MatchParserSgp(), [game])!;

    private static JObject Game(int queueId, int championId)
    {
        var participant = new JObject
        {
            ["puuid"] = "self-puuid-0001",
            ["summonerId"] = "12345",
            ["riotIdGameName"] = "测试玩家",
            ["riotIdTagline"] = "0001",
            ["teamId"] = 100,
            ["win"] = true,
            ["kills"] = 10,
            ["deaths"] = 2,
            ["assists"] = 7,
            ["goldEarned"] = 12000,
            ["totalDamageDealtToChampions"] = 20000,
            ["totalDamageTaken"] = 15000,
            ["championId"] = championId,
            ["challenges"] = new JObject { ["killParticipation"] = 0.5 },
        };
        return new JObject
        {
            ["gameId"] = 7412345678L,
            ["gameDuration"] = 1325,
            ["gameStartTimestamp"] = 1767225600000L,
            ["queueId"] = queueId,
            ["gameMode"] = "KIWI",
            ["gameType"] = "MATCHED_GAME",
            ["participants"] = new JArray(participant),
        };
    }

    /// <summary>The same game with a full rune page, two items and an augment.</summary>
    private static JObject RichGame()
    {
        var game = Game(2400, championId: 103);
        var participant = (JObject)game["participants"]![0]!;
        participant["item0"] = 1001;
        participant["item1"] = 1002;
        participant["playerAugment1"] = 1234;
        participant["perks"] = new JObject
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
                    ["selections"] = new JArray(
                        new JObject { ["perk"] = 8005 },
                        new JObject { ["perk"] = 9101 },
                        new JObject { ["perk"] = 9104 },
                        new JObject { ["perk"] = 8014 }
                    ),
                },
                new JObject
                {
                    ["description"] = "subStyle",
                    ["selections"] = new JArray(new JObject { ["perk"] = 8226 }, new JObject { ["perk"] = 8210 }),
                }
            ),
        };
        return game;
    }

    private static JArray History()
    {
        var first = Match(7412345678L);
        var remake = Match(2L, win: false);
        remake["gameEndedInEarlySurrender"] = true;
        var other = Match(3L);
        other["participants"]![0]!["puuid"] = "someone-else";
        return new JArray(first, remake, other);
    }

    private static JObject Match(long gameId, bool win = true) =>
        new()
        {
            ["gameId"] = gameId,
            ["gameDuration"] = 1325,
            ["gameStartTimestamp"] = 1767225600000L,
            ["queueId"] = 420,
            ["participants"] = new JArray(
                new JObject
                {
                    ["puuid"] = "self-puuid-0001",
                    ["teamId"] = 100,
                    ["win"] = win,
                    ["championId"] = 103,
                    ["championName"] = "ParseCheckChampion",
                    ["kills"] = 10,
                    ["deaths"] = 2,
                    ["assists"] = 7,
                    ["enemyMissingPings"] = 4,
                    ["enemyVisionPings"] = 3,
                    ["getBackPings"] = 5,
                    ["dangerPings"] = 1,
                }
            ),
        };

    private static JObject Matchless() =>
        new() { ["gameDuration"] = 600, ["participants"] = new JArray(new JObject { ["puuid"] = "self-puuid-0001" }) };

    private static string ModeFor(int queueId) =>
        (string)ExtractGameMode.Invoke(Parser, [new JObject { ["queueId"] = queueId }])!;

    private static string Tags(JArray? tags) => (string)MapGameTags.Invoke(Parser, [tags])!;

    private static readonly MatchDataParser Parser = new();

    private static readonly MethodInfo ExtractGameMode = typeof(MatchDataParser).GetMethod(
        "ExtractGameMode",
        BindingFlags.NonPublic | BindingFlags.Instance
    )!;

    private static readonly MethodInfo MapGameTags = typeof(MatchDataParser).GetMethod(
        "MapGameTags",
        BindingFlags.NonPublic | BindingFlags.Instance
    )!;

    private static readonly MethodInfo SgpExtractGameMode = typeof(MatchParserSgp).GetMethod(
        "ExtractGameMode",
        BindingFlags.NonPublic | BindingFlags.Instance
    )!;
}
