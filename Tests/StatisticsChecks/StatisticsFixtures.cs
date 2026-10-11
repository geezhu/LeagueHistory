using System.Net;
using Newtonsoft.Json.Linq;

internal static class StatisticsFixtures
{
    public static JObject Game()
    {
        return JObject.Parse(
            """
            {"gameId":1,"queueId":420,"participants":[{"participantId":1,"puuid":"self","championId":1,"stats":{"win":true,"kills":10,"deaths":0,"assists":5,"totalDamageDealtToChampions":20000,"goldEarned":12000,"pentaKills":0}}]}
            """
        );
    }

    public static JArray Records()
    {
        var game = Game();
        var remake = (JObject)game.DeepClone();
        remake["gameId"] = 2;
        remake["participants"]![0]!["stats"]!["gameEndedInEarlySurrender"] = true;
        var wrong = (JObject)game.DeepClone();
        wrong["gameId"] = 3;
        wrong["participants"]![0]!["puuid"] = "other";
        return new JArray(game, game.DeepClone(), remake, wrong);
    }

    public static List<JObject> Players()
    {
        return new List<JObject>
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
    }

    public static JObject Timeline()
    {
        return JObject.Parse(
            """
            {"frames":[
              {"timestamp":0,"participantFrames":{"1":{"totalGold":500},"7":{"totalGold":600}},"events":[]},
              {"timestamp":60000,"participantFrames":{"1":{"totalGold":1300},"7":{"totalGold":1500}},"events":[{"type":"CHAMPION_KILL","timestamp":40000,"killerId":7,"victimId":1,"position":{"x":100,"y":200}}]},
              {"timestamp":120000,"participantFrames":{"7":{"totalGold":2500}},"events":[{"type":"ELITE_MONSTER_KILL","timestamp":90000,"killerId":0,"monsterType":"DRAGON"}]},
              {"timestamp":180000,"participantFrames":{"1":{"totalGold":3200},"7":{"totalGold":3400}},"events":[]}
            ]}
            """
        );
    }

    public static JArray Masteries()
    {
        return JArray.Parse(
            """
            [{"championId":1,"championLevel":10,"championPoints":12000},{"championId":2,"championLevel":20,"championPoints":80000},{"championId":3,"championLevel":8,"championPoints":9000},{"championId":1,"championPoints":1000},{"championId":4}]
            """
        );
    }

    public sealed record Reply(HttpStatusCode Status, string Body);

    public sealed class Handler(Func<string, Reply> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var result = reply(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(result.Status) { Content = new StringContent(result.Body) });
        }
    }
}
