using System.Reflection;
using System.Runtime.CompilerServices;
using League.Controls;
using League.Managers;
using League.Models;
using League.Parsers;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// The processor that turns a player card into a player's history: the hidden-player rule, the history size, the
/// cached copy handed back for a new champion, the behaviour summary derived from recent games and the rank text.
/// The card manager is stood in for, so nothing reaches the client.
/// </summary>
internal static class MatchQueryProcessorChecks
{
    public static async Task RunAsync()
    {
        await GuardChecks();
        HiddenChecks();
        await CacheHitChecks();
        BehaviourChecks();
        RankTextChecks();
        Console.WriteLine(
            "PASS: the history size, the missing card manager guard, the hidden-player rule, the cached copy, "
                + "the behaviour averages and the rank text."
        );
    }

    private static async Task GuardChecks()
    {
        var processor = new MatchQueryProcessor();
        processor.SetMatchHistoryCount(50);
        Check(HistoryCount(processor) == 50, "A supported history size is kept");
        processor.SetMatchHistoryCount(7);
        Check(HistoryCount(processor) == 20, "An unsupported history size falls back to twenty");
        processor.SetMatchHistoryCount(50);
        Check(HistoryCount(processor) == 50, "The size can be set again");

        Check(
            Fails(processor, "EnsureFactoryInitialized", typeof(InvalidOperationException)),
            "A processor without a card manager refuses to fetch anything"
        );

        SetManager.Invoke(processor, [RuntimeHelpers.GetUninitializedObject(typeof(PlayerCardManager))]);
        EnsureFactory.Invoke(processor, null);
        Check(true, "A processor with a card manager is ready");

        Check(await Rejects(() => processor.FetchPlayerMatchInfoAsync(null!)), "A missing player card is refused");

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Check(
            await Rejects(() => processor.FetchPlayerMatchInfoAsync(new JObject(), cancelled.Token)),
            "A cancelled history read is refused"
        );
    }

    private static void HiddenChecks()
    {
        Check(IsHidden(new JObject(), 0, "a-real-puuid"), "A card without a summoner is hidden");
        Check(IsHidden(new JObject(), 7, ""), "A card without a puuid is hidden");
        Check(
            IsHidden(new JObject { ["nameVisibilityType"] = "HIDDEN" }, 7, "a-real-puuid"),
            "A card the client marks hidden is hidden"
        );
        Check(IsHidden(new JObject(), 7, "OBFUSCATED_abc"), "An obfuscated puuid is hidden");
        Check(IsHidden(new JObject(), 7, "HIDDEN_abc"), "A placeholder puuid is hidden");
        Check(!IsHidden(new JObject(), 7, "a-real-puuid"), "An ordinary player card is not hidden");
    }

    private static async Task CacheHitChecks()
    {
        var processor = new MatchQueryProcessor();
        SetManager.Invoke(processor, [RuntimeHelpers.GetUninitializedObject(typeof(PlayerCardManager))]);
        var cache = (PlayerMatchCacheManager)CacheField.GetValue(processor)!;
        var cached = new PlayerMatchInfo
        {
            Player = new PlayerInfo
            {
                SummonerId = 7,
                Puuid = "p-0000000001",
                GameName = "甲",
                ChampionId = 1,
            },
            WinHistory = [true, false],
        };
        cache.CachePlayerMatch(7, ":20", cached);

        var stages = new List<string>();
        var hit = await processor.FetchPlayerMatchInfoAsync(
            new JObject
            {
                ["summonerId"] = 7,
                ["puuid"] = "p-0000000001",
                ["championId"] = 103,
            },
            default,
            "",
            stages.Add
        );

        Check(!ReferenceEquals(hit, cached), "A cached player is handed back as a copy");
        Check(hit.Player.ChampionId == 103, "The copy carries the champion the card asked about");
        Check(hit.Player.Puuid == "p-0000000001", "The copy keeps the player's identity");
        Check(hit.Player.ChampionName == "Unknown", "A champion the resource table does not know is named Unknown");
        Check(hit.Player.Avatar == null, "A queue-filtered copy carries no avatar");
        Check(stages.SequenceEqual(["queue", "cache-hit"]), "The stages report the cache hit");

        processor.ClearPlayerMatchCache();
        Check(!cache.TryGetPlayerMatch(7, ":20", out _), "Clearing the cache drops the players that were read");
    }

    private static void BehaviourChecks()
    {
        var processor = new MatchQueryProcessor();
        var empty = new PlayerMatchInfo();
        Behaviour.Invoke(processor, [empty]);
        Check(empty.Behavior.AvgMissingPings == 0, "A player without games has no behaviour averages");

        var played = new PlayerMatchInfo
        {
            RecentMatches =
            [
                Stat(10, surrender: true, missing: 3, vision: 3, getBack: 4, danger: 3),
                Stat(20, surrender: true, missing: 3, vision: 3, getBack: 4, danger: 3),
                Stat(25, early: true, missing: 3, vision: 3, getBack: 4, danger: 2),
                Stat(30, missing: 3, vision: 2, getBack: 4, danger: 2),
            ],
        };
        Behaviour.Invoke(processor, [played]);

        Check(played.Behavior.AvgMissingPings == 3, "The average of the missing pings is reported");
        Check(played.Behavior.AvgVisionPings == 2.75, "The average of the vision pings is reported");
        Check(played.Behavior.AvgGetBackPings == 4, "The average of the retreat pings is reported");
        Check(played.Behavior.AvgDangerPings == 2.5, "The average of the danger pings is reported");
        Check(played.Behavior.SurrenderRate == 75, "Surrenders and early surrenders are counted together");
        Check(played.Behavior.ShortGameCount == 1, "Only games under eighteen minutes count as short");
    }

    private static void RankTextChecks()
    {
        var processor = new MatchQueryProcessor();
        var ranks = RankedStats.FromJson(
            JObject.Parse("""{"queueMap":{"RANKED_SOLO_5x5":{"tier":"GOLD","division":"II","leaguePoints":45}}}""")
        );

        string solo = (string)FormatRank.Invoke(processor, [ranks, "单双排"])!;
        Check(solo.Contains("45", StringComparison.Ordinal), "A ranked queue reports its league points");
        Check((string)FormatRank.Invoke(processor, [ranks, "其他"])! == "未知", "A queue without a rank is unknown");
    }

    private static MatchStat Stat(
        int duration,
        bool surrender = false,
        bool early = false,
        int missing = 0,
        int vision = 0,
        int getBack = 0,
        int danger = 0
    ) =>
        new()
        {
            GameDurationMinutes = duration,
            GameEndedInSurrender = surrender,
            GameEndedInEarlySurrender = early,
            EnemyMissingPings = missing,
            EnemyVisionPings = vision,
            GetBackPings = getBack,
            DangerPings = danger,
        };

    private static bool IsHidden(JObject card, long summonerId, string puuid) =>
        (bool)Hidden.Invoke(null, [card, summonerId, puuid])!;

    private static int HistoryCount(MatchQueryProcessor processor) => (int)HistoryCountField.GetValue(processor)!;

    private static bool Fails(MatchQueryProcessor processor, string method, Type expected)
    {
        try
        {
            Method(method).Invoke(processor, null);
            return false;
        }
        catch (TargetInvocationException ex)
        {
            return ex.InnerException?.GetType() == expected;
        }
    }

    private static async Task<bool> Rejects(Func<Task> action)
    {
        try
        {
            await action();
            return false;
        }
        catch (Exception ex) when (ex is ArgumentNullException or OperationCanceledException)
        {
            return true;
        }
    }

    private static MethodInfo Method(string name) =>
        typeof(MatchQueryProcessor).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo EnsureFactory = Method("EnsureFactoryInitialized");

    private static readonly MethodInfo Behaviour = Method("CalculatePlayerBehavior");

    private static readonly MethodInfo FormatRank = Method("GetFormattedRank");

    private static readonly MethodInfo SetManager = typeof(MatchQueryProcessor).GetMethod(
        "SetPlayerCardManager",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
    )!;

    private static readonly MethodInfo Hidden = typeof(MatchQueryProcessor).GetMethod(
        "IsHiddenPlayer",
        BindingFlags.Static | BindingFlags.NonPublic
    )!;

    private static readonly FieldInfo HistoryCountField = typeof(MatchQueryProcessor).GetField(
        "_matchHistoryCount",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    private static readonly FieldInfo CacheField = typeof(MatchQueryProcessor).GetField(
        "_cacheManager",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;
}
