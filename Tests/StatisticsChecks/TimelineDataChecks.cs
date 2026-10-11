using League.Models;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;
using static StatisticsFixtures;

internal static class TimelineDataChecks
{
    public static void Run()
    {
        var players = Players();
        var parsed = MatchTimeline.Parse(Timeline(), players);
        Check(
            parsed.Frames[0].Blue == 600 && parsed.Frames[0].Red == 500,
            "Teams must use participant mapping, not ID ranges"
        );
        Check(parsed.Frames[2].Red == null, "Missing gold must leave a gap");
        Check(
            parsed.GoldDifference(parsed.Frames[0]) == 100 && parsed.GoldDifference(parsed.Frames[2]) == null,
            "Average gold difference and missing intervals"
        );
        Check(
            parsed.GoldDifference(new GoldFrame(0, 300, 500)) == -200 && parsed.Events[0].Team == 100,
            "Negative gold advantage and killer team mapping"
        );
        Check(
            parsed.Events[0].Actor == "蓝队玩家" && parsed.Events[0].X == 100 && parsed.Events[1].Actor == "环境",
            "Event identities/positions"
        );
        Check(MatchTimeline.Parse(new JArray(), players).Frames.Count == 0, "Unexpected timeline shape");
    }
}
