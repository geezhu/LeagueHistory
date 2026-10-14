using League.Models;
using Newtonsoft.Json.Linq;

internal static class ModelChecks
{
    public static Task RunAsync()
    {
        AramTeamScoreChecks.Run();
        ChampionTierChatChecks.Run();
        LiveChampionChecks.Run();
        LiveDeathStreakChecks.Run();
        OverlayTierChatChecks.Run();
        OverlayRecordChecks.Run();
        OverlayVoiceChatChecks.Run();
        Timeline();
        return Task.CompletedTask;
    }

    private static void Timeline()
    {
        var players = new List<JObject>
        {
            new()
            {
                ["participantId"] = 1,
                ["teamId"] = 200,
                ["riotIdGameName"] = "红队玩家",
            },
            new()
            {
                ["participantId"] = 7,
                ["teamId"] = 100,
                ["riotIdGameName"] = "蓝队玩家",
            },
        };
        var data = JObject.Parse(
            """
            {"info":{"frameInterval":60000,"frames":[
              {"timestamp":120000,"participantFrames":{"7":{"totalGold":2500}},"events":[{"type":"ELITE_MONSTER_KILL","timestamp":90000,"killerId":0,"monsterType":"DRAGON"}]},
              {"timestamp":-1,"events":[{"type":"CHAMPION_KILL","killerId":7}]},
              {"timestamp":0,"participantFrames":{"1":{"totalGold":500},"7":{"totalGold":600}},"events":[]},
              {"timestamp":60000,"participantFrames":{"1":{"totalGold":1300},"7":{"totalGold":1500}},"events":[{"type":"CHAMPION_KILL","timestamp":40000,"killerId":7,"victimId":1,"position":{"x":100,"y":200}}]}
            ]}}
            """
        );
        var timeline = MatchTimeline.Parse(new JObject { ["json"] = data }, players);
        Check(timeline.FrameInterval == 60000 && timeline.Frames.Count == 3, "Wrappers and negative frame filtering");
        Check(
            timeline.Frames.Select(frame => frame.Timestamp).SequenceEqual(new long[] { 0, 60000, 120000 }),
            "Frame order"
        );
        Check(
            timeline.Frames[0].Blue == 600 && timeline.Frames[0].Red == 500,
            "Teams use participant mapping, not ID ranges"
        );
        Check(
            timeline.GoldDifference(timeline.Frames[0]) == 100 && timeline.Frames[2].Red == null,
            "Gold advantage and missing data"
        );
        Check(
            timeline.Events.Count == 2 && timeline.Events[0].Actor == "蓝队玩家" && timeline.Events[0].Team == 100,
            "Events remain sorted and assigned to the mapped killer"
        );
        Check(
            timeline.Events[0].X == 100 && timeline.Events[0].Y == 200 && timeline.Events[1].Actor == "环境",
            "Event positions and environmental kills"
        );
        Check(
            MatchTimeline
                .Parse(data, [players[0], players[0]])
                .Frames.All(frame => frame.Blue == null && frame.Red == null),
            "Ambiguous player mapping leaves gold unconfirmed"
        );
        Check(MatchTimeline.Parse(new JArray(), players).Frames.Count == 0, "Unexpected timeline shape");
        Console.WriteLine(
            "PASS: isolated model suite, timeline wrappers, ordering, team mapping, missing data and events."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
