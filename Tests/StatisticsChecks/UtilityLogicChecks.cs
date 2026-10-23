using System.Reflection;
using League.Managers;
using League.Models;
using League.Networking;
using League.Parsers;
using League.PrimaryElection;
using League.Services;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// Utilities and models that had no test at all: the pinyin initials behind the Game Bar search, the champion
/// position table, the player card cache and its snapshot, premade grouping, and the queue-to-mode names the
/// history cards and the summary parser show.
/// </summary>
internal static class UtilityLogicChecks
{
    public static void Run()
    {
        PinyinInitials();
        ChampionPositions();
        PlayerCardCaches();
        PremadeGroups();
        QueueModeNames();
        Console.WriteLine(
            "PASS: pinyin initials, champion positions, player card cache and snapshot, premade grouping, "
                + "and the queue-to-mode names of both mapping tables."
        );
    }

    private static void PinyinInitials()
    {
        Check(ChineseInitials.Of("坦克引擎") == "tkyq", "Level-1 characters give their pinyin initials");
        Check(
            ChineseInitials.Of("亚索") == "ys" && ChineseInitials.Of("盖伦") == "gl",
            "Champion names give the initials the search expects"
        );
        Check(
            ChineseInitials.Of("A1 b2-+") == "a1b2",
            "Letters and digits are kept in lower case and other ASCII is dropped"
        );
        Check(
            ChineseInitials.Of(null) == "" && ChineseInitials.Of("") == "" && ChineseInitials.Of("  ") == "",
            "Text without usable characters has no initials"
        );
        // Level-2 characters are ordered by radical, so they cannot be given an initial.
        Check(
            ChineseInitials.Of("亍") == "" && ChineseInitials.Of("龘") == "",
            "Level-2 and non-GB2312 characters are left out"
        );
    }

    private static void ChampionPositions()
    {
        Check(
            ChampionPositionMapper
                .GetPositionsByChampionName("武器大师")
                .SequenceEqual([ChampionPosition.Top, ChampionPosition.Jungle]),
            "A champion played in two lanes lists both in table order"
        );
        Check(
            ChampionPositionMapper
                .GetPositionsByChampionName("暗夜猎手")
                .SequenceEqual([ChampionPosition.Top, ChampionPosition.ADC]),
            "A champion played as a marksman above and below lists both"
        );
        Check(
            ChampionPositionMapper.GetPositionsByChampionName("解脱者").Count == 4,
            "A champion in four lanes lists all four"
        );
        Check(
            ChampionPositionMapper.GetPositionsByChampionName("不存在的英雄").Count == 0,
            "A champion outside the table has no position"
        );
    }

    private static void PlayerCardCaches()
    {
        var cache = new PlayerCardCache();
        Check(!cache.TryGetCard(7, out var missing) && missing == null, "An unregistered summoner has no card");
        cache.RegisterCard(0, null!); // A hidden player is never registered.
        Check(!cache.TryGetCard(0, out _), "Registering a card without a summoner is ignored");

        var first = Info(11, "玩家甲");
        cache.AddOrUpdateCache(0, first);
        Check(cache.GetAllCachedPlayerInfos().Count == 0, "Caching a player without a summoner is ignored");
        cache.AddOrUpdateCache(11, first);
        Check(cache.GetAllCachedPlayerInfos()[11] == first, "A cached player is readable by summoner id");

        var snapshot = cache.GetAllCachedPlayerInfos();
        snapshot.Clear();
        Check(cache.GetAllCachedPlayerInfos().Count == 1, "The reader gets a copy, so it cannot clear the cache");

        cache.Replace([Info(21, "玩家乙")]);
        var replaced = cache.GetAllCachedPlayerInfos();
        Check(
            replaced.Count == 1 && replaced.ContainsKey(21) && !replaced.ContainsKey(11),
            "Replace swaps the whole cache for the given players"
        );

        cache.UpdateCurrentChampion(21, 99, 3);
        Check(cache.GetCurrentChampion(21) == 99, "The current champion is remembered per summoner");
        Check(cache.GetCurrentChampion(999) == 0, "An unknown summoner has no champion");
        cache.UpdateCurrentChampion(0, 55, 1); // Hidden players still report a champion for the picks.
        Check(cache.GetCurrentChampion(0) == 55, "A hidden player still records the champion");

        cache.ClearGameState();
        Check(
            cache.GetAllCachedPlayerInfos().Count == 0 && cache.GetCurrentChampion(21) == 0,
            "ClearGameState drops the game's players and champions"
        );

        cache.AddOrUpdateCache(31, Info(31, "玩家丙"));
        cache.UpdateCurrentChampion(31, 7, 2);
        cache.ClearAll();
        Check(
            cache.GetAllCachedPlayerInfos().Count == 0 && cache.GetCurrentChampion(31) == 0,
            "ClearAll drops everything"
        );
    }

    private static void PremadeGroups()
    {
        var first = Info(1, "甲");
        first.MatchKeys.AddRange(["m1", "m2", "m3"]);
        var second = Info(2, "乙");
        second.MatchKeys.AddRange(["m1", "m2"]);
        var third = Info(3, "丙");
        third.MatchKeys.AddRange(["m1"]);
        var alone = Info(4, "丁");
        alone.MatchKeys.AddRange(["z1"]);

        var detector = new PartyDetector();
        detector.Detect([first, second, third, alone]);
        Check(first.PartyId != null && first.PartyId == second.PartyId, "Two shared games make a premade");
        Check(
            first.Player.GroupKey != null
                && first.Player.GroupKey.Contains('甲')
                && first.Player.GroupKey.Contains('乙'),
            "The group key names every member"
        );
        Check(
            first.Player.NameColor == second.Player.NameColor && first.Player.NameColor != Color.Black,
            "A premade shares one colour"
        );
        Check(
            third.PartyId == null && third.Player.GroupKey == null && third.Player.NameColor == Color.Black,
            "One shared game alone is below the threshold"
        );
        Check(
            alone.PartyId == null && alone.Player.NameColor == Color.Black,
            "A player who shares nothing stays alone"
        );

        var otherPair = Info(5, "戊");
        otherPair.MatchKeys.AddRange(["y1", "y2"]);
        var otherMate = Info(6, "己");
        otherMate.MatchKeys.AddRange(["y1", "y2"]);
        detector.Detect([first, second, otherPair, otherMate]);
        Check(first.Player.NameColor != otherPair.Player.NameColor, "Two premades are told apart by colour");

        detector.Detect([first, alone]);
        Check(
            first.PartyId == null && first.Player.GroupKey == null && first.Player.NameColor == Color.Black,
            "A scan that no longer groups a player clears the mark it left before"
        );

        var anyShared = new PartyDetector(1);
        anyShared.Detect([first, third]);
        Check(
            first.PartyId != null && first.PartyId == third.PartyId,
            "A threshold of one groups players who shared a single game"
        );

        var anonymous = Info(7, "无名");
        anonymous.MatchKeys.AddRange(["m1", "m2", "m3"]);
        anonymous.Player.Puuid = "";
        new PartyDetector().Detect([first, anonymous]);
        Check(first.PartyId == null, "A player without a puuid cannot be grouped");

        var empty = new PartyDetector();
        empty.Detect([]);
        empty.Detect([first, second]);
        Check(first.PartyId != null, "An empty roster is accepted, and a later scan still groups");
    }

    private static void QueueModeNames()
    {
        var parser = new MatchDataParser();
        string Mode(string json) => (string)ExtractGameMode.Invoke(parser, [JObject.Parse(json)])!;

        Check(Mode("""{"queueId":420}""") == "单双排", "A ranked queue id names its mode");
        Check(Mode("""{"queueId":3270}""") == "自定义海克斯", "The custom Mayhem queue has a name");
        Check(Mode("""{"queueId":450}""") == "大乱斗", "The ARAM queue has a name");
        Check(Mode("""{"metadata":{"tags":["q_450"]}}""") == "大乱斗", "A queue tag names the mode");
        Check(
            Mode("""{"metadata":{"tags":["q_450","q_420"]}}""") == "单双排",
            "The first matching tag in the table wins"
        );
        Check(Mode("""{"metadata":{"tags":["q_420"]},"queueId":450}""") == "单双排", "Tags win over the queue id");
        Check(
            Mode("""{"metadata":{"tags":["未知标签"]},"queueId":450}""") == "大乱斗",
            "An unknown tag falls back to the queue id"
        );
        Check(
            Mode("""{"metadata":{"tags":["mode_practicetool"]}}""") == "训练模式",
            "A mode tag without a queue id still names the mode"
        );
        Check(Mode("""{"queueId":0,"gameMode":"CLASSIC"}""") == "召唤师峡谷", "Queue zero falls back to the game mode");
        Check(Mode("""{}""") == "未知(queueId=-1)", "Without data the mode reports the missing queue");
        Check(Mode("""{"queueId":99999}""") == "未知(queueId=99999)", "An unmapped queue keeps its number");
        Check(
            (string)ExtractGameMode.Invoke(parser, [null])! == "未知模式",
            "Missing game data has a mode name of its own"
        );

        var gameMod = typeof(MatchMap).Assembly.GetType("League.Models.GameMod")!;
        var getModeName = gameMod.GetMethod("GetModeName", BindingFlags.Public | BindingFlags.Static)!;
        string Native(int? queue, string? mode) => (string)getModeName.Invoke(null, [queue, mode])!;

        Check(Native(440, null) == "灵活排位", "The queue id is mapped first");
        Check(Native(440, "ARAM") == "灵活排位", "The queue id wins over the game mode");
        Check(Native(null, "ARAM") == "大乱斗", "The game mode is matched in upper case");
        Check(Native(null, "aram") == "大乱斗", "A lower-case game mode still matches");
        Check(Native(99999, "CLASSIC") == "召唤师峡谷", "An unmapped queue falls back to the game mode");
        Check(Native(null, null) == "未知()", "Nothing known reports no queue at all");
    }

    private static readonly MethodInfo ExtractGameMode = typeof(MatchDataParser).GetMethod(
        "ExtractGameMode",
        BindingFlags.NonPublic | BindingFlags.Instance
    )!;

    private static PlayerMatchInfo Info(long summonerId, string name) =>
        new()
        {
            Player = new PlayerInfo
            {
                SummonerId = summonerId,
                GameName = name,
                Puuid = $"puuid-{summonerId}",
            },
        };
}
