using System.Reflection;
using League.Parsers;
using League.Services;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// Custom games report queue -1, so the game mode picks the statistics: the champion select assistant's mode and the
/// player cards' mode win rate. Mayhem's queue 3270 counts as 2400; matchmade queues keep their own number.
/// </summary>
internal static class GameQueueChecks
{
    public static void Run()
    {
        JObject Data(int id, string mode) =>
            new()
            {
                ["queue"] = new JObject { ["id"] = id, ["gameMode"] = mode },
            };

        Check(GameQueues.Effective(Data(-1, "KIWI")) == 2400, "Custom Mayhem uses the Mayhem statistics");
        Check(GameQueues.Effective(Data(-1, "ARAM")) == 450, "Custom ARAM uses the ARAM statistics");
        Check(GameQueues.Effective(Data(3270, "KIWI")) == 2400, "Mayhem's other queue counts as 2400");
        Check(GameQueues.Effective(Data(420, "CLASSIC")) == 420, "Matchmade queues keep their number");
        Check(GameQueues.Effective(null) == 0, "No game data, no queue");

        Check(
            GameQueues.ForHistory(Data(-1, "KIWI")) == "2400"
                && PlayerMatchDataFetcher.GetQueueFilter(GameQueues.ForHistory(Data(-1, "KIWI"))) == "q_2400",
            "A custom Mayhem card filters Mayhem history"
        );
        Check(GameQueues.ForHistory(Data(-1, "ARAM")) == "450", "A custom ARAM card filters ARAM history");
        Check(
            GameQueues.ForHistory(Data(-1, "CLASSIC")) == "-1"
                && PlayerMatchDataFetcher.GetQueueFilter(GameQueues.ForHistory(Data(-1, "CLASSIC"))).Length == 0,
            "A custom Summoner's Rift game keeps all recent games"
        );
        Check(GameQueues.ForHistory(Data(3270, "KIWI")) == "2400", "Mayhem 3270 cards filter Mayhem history");

        var sessionQueue = typeof(League.FormMain)
            .Assembly.GetType("League.ChampionSelectAssistant")!
            .GetMethod("SessionQueue", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check(
            (int)sessionQueue.Invoke(null, [new JObject { ["gameData"] = Data(-1, "KIWI") }])! == 2400,
            "The assistant reads a custom Mayhem game as Mayhem"
        );
        // The profile overview names custom queues instead of showing "模式 3270".
        Check(
            League.Models.CareerStatistics.QueueName(3270) == "自定义海克斯大乱斗"
                && League.Models.CareerStatistics.QueueName(3100) == "自定义峡谷"
                && League.Models.CareerStatistics.QueueName(2400) == "海克斯大乱斗",
            "Custom Mayhem and custom Rift have their own names"
        );
        Check(
            League.Models.CareerStatistics.QueueName(1300) == MatchParserSgp.QueueModeName(1300)
                && League.Models.CareerStatistics.QueueName(987654) == "模式 987654",
            "Other queues use the history list's names; unknown ones keep the number"
        );
        Console.WriteLine("PASS: custom games and queue 3270 map to the queue whose statistics apply, and are named.");
    }
}
