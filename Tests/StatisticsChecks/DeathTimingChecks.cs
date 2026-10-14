using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>Advice on dying to shop: blocking factors, enemies dead, resources, team pressure and its JSON.</summary>
internal static class DeathTimingChecks
{
    public static void Run()
    {
        CheckScope();
        CheckBlocking();
        CheckTrade();
        CheckPressure();
        CheckActivePlayer();
        CheckAugments();
        Console.WriteLine(
            "PASS: death timing scope, inhibitor and streak blocks, enemies dead, gold and health, team pressure and JSON."
        );
    }

    private static void CheckScope()
    {
        var rift = Game(300, 1500, 1000) with { Mode = "CLASSIC", Map = 11 };
        Check(Advise(rift) == null, "Summoner's Rift recalls to shop, so it gets no advice");
        var dead = Game(300, 1500, 1000);
        dead = dead with
        {
            Players = dead.Players.Select(p => p.Self ? p with { IsDead = true, RespawnTimer = 20 } : p).ToArray(),
        };
        Check(Advise(dead) == null, "No advice while already dead");
    }

    private static void CheckBlocking()
    {
        var inhibitor = Game(300, 3000, 200) with
        {
            Events = [new(1, "InhibKilled", 250, "", "") { Structure = "Barracks_T1_C1" }],
        };
        var fallen = Advise(inhibitor)!;
        Check(
            fallen.Verdict == DeathTimingVerdict.Avoid && fallen.Reasons[0].Contains("小水晶"),
            "A fallen own inhibitor blocks dying"
        );

        LiveMatchEvent Kill(int id, double time) => new(id, "ChampionKill", time, "我", "敌方1");
        var streak = Advise(Game(300, 3000, 200) with { Events = [Kill(1, 100), Kill(2, 150), Kill(3, 200)] })!;
        Check(
            streak.Verdict == DeathTimingVerdict.Avoid && streak.Reasons[0].Contains("终结赏金"),
            "A kill streak warns of a shutdown bounty"
        );
        var reset = Advise(
            Game(300, 3000, 200) with
            {
                Events = [Kill(1, 100), Kill(2, 150), new(3, "ChampionKill", 180, "敌方1", "我"), Kill(4, 200)],
            }
        )!;
        Check(reset.Factors.All(f => !f.Text.Contains("终结赏金")), "A death resets the streak");
    }

    private static void CheckTrade()
    {
        var game = Game(300, 3000, 200);
        game = game with
        {
            Players = game
                .Players.Select((p, i) => p.Team == "CHAOS" && i < 7 ? p with { IsDead = true, RespawnTimer = 25 } : p)
                .ToArray(),
        };
        var good = Advise(game)!;
        Check(
            good.Verdict == DeathTimingVerdict.Good
                && good.Reasons[0].Contains("对面 2 人阵亡")
                && good.Factors.Any(f => f.Text.Contains("够买大件"))
                && good.Factors.Any(f => f.Text.Contains("血量只剩 20%")),
            "Two enemies dead, a full item of gold and low health make a good trade"
        );

        var poor = Game(300, 300, 1000);
        poor = poor with
        {
            Players = poor
                .Players.Select((p, i) => i is 1 or 2 ? p with { IsDead = true, RespawnTimer = 20 } : p)
                .ToArray(),
        };
        var avoid = Advise(poor)!;
        Check(
            avoid.Verdict == DeathTimingVerdict.Avoid
                && avoid.Factors.Any(f => f.Text.Contains("只剩 2 人守"))
                && avoid.Factors.Any(f => f.Text == "身上只有 300 金"),
            "Two allies dead and no gold argue against dying"
        );
        Check(
            avoid.Factors.Any(f => f.Weight == 0 && f.Text.Contains("阵容数据未加载")),
            "Without statistics the advice says what it left out"
        );

        var json = good.ToJson();
        Check(
            (string?)json["verdict"] == "good"
                && json["reasons"]!.Count() == 2
                && json["details"]!.Values<string>().Any(d => d!.StartsWith("＋ 对面 2 人阵亡")),
            "Advice travels to the widget"
        );
        var forecast = MinionWaveForecaster.Calculate(game, 0)! with { DeathAdvice = good };
        Check((string?)forecast.ToJson()["deathAdvice"]?["verdict"] == "good", "The wave snapshot carries the advice");
    }

    private static void CheckPressure()
    {
        var game = Game(300, 1500, 1000);
        var plain = Advise(game)!;
        var risky = Advise(
            game,
            new(PressureLevel.Medium, PressureLevel.High, PressureLevel.Low) { SelfRoles = ["前排"] }
        )!;
        Check(
            risky.Factors.Sum(f => f.Weight) == plain.Factors.Sum(f => f.Weight) - 2
                && risky.Factors.Any(f => f.Text == "你是我方主要前排"),
            "A diving enemy and being the main front line each cost a point"
        );

        var fixture = JObject.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/AramMeta/team-score.json"))
        );
        var index = AramMetaIndex.Parse((JObject)fixture["index"]!);
        var pressure = TeamPressure.From(index, [4, 35, 37, 63, 102], [112, 134, 157, 202, 876], 4);
        string[] names = ["前排", "开团", "清线", "续航"];
        Check(pressure != null && pressure.SelfRoles.All(names.Contains), "Pressure comes from full teams");
        Check(TeamPressure.From(index, [102], [112, 134, 157, 202, 876], 102) == null, "Too few champions to judge");
    }

    private static void CheckAugments()
    {
        var mayhem = Game(300, 1500, 1000) with { Mode = "KIWI" };
        static DeathTimingFactor? Augment(LiveMatchSnapshot game, int? openedAt) =>
            DeathTimingAdvisor
                .Evaluate(game, MinionWaveForecaster.Calculate(game, 0)!, null, openedAt)!
                .Factors.SingleOrDefault(f => f.Text.Contains("强化"));
        Check(
            Augment(mayhem, 3) is { Weight: 2, Text: "7 级的强化还没选，死后才能选" },
            "A Mayhem augment reached since the shop was open favours dying"
        );
        Check(Augment(mayhem, 8) == null, "Augments already offered are not counted again");
        Check(Augment(mayhem, null) == null, "Unknown history after a mid-game start makes no claim");
        Check(Augment(Game(300, 1500, 1000), 3) == null, "Plain ARAM has no augments");
    }

    private static void CheckActivePlayer()
    {
        var active = LiveActivePlayer.Parse(
            new JObject
            {
                ["currentGold"] = 1234.5,
                ["championStats"] = new JObject { ["maxHealth"] = 1000, ["currentHealth"] = 250 },
            }
        )!;
        Check(active.CurrentGold == 1234.5 && active.Stats?.CurrentHealth == 250, "Gold and health are read");
    }

    private static DeathTimingAdvice? Advise(LiveMatchSnapshot snapshot, TeamPressure? pressure = null) =>
        MinionWaveForecaster.Calculate(snapshot, 0) is { } waves
            ? DeathTimingAdvisor.Evaluate(snapshot, waves, pressure)
            : null;

    // Self is the first ORDER player at level 8; players 5–9 are the enemy team.
    private static LiveMatchSnapshot Game(double time, double gold, double health)
    {
        var players = Enumerable
            .Range(0, 10)
            .Select(i =>
            {
                string name =
                    i == 0 ? "我"
                    : i < 5 ? $"队友{i}"
                    : $"敌方{i - 4}";
                return new LiveMatchPlayer(
                    $"{name}|{i}",
                    name,
                    "Lux",
                    "拉克丝",
                    i < 5 ? "ORDER" : "CHAOS",
                    i == 0,
                    8,
                    null
                )
                {
                    EventAliases = [name],
                };
            })
            .ToArray();
        var stats = new LiveChampionStats(1000, 0, 0, 0, 0, 0, 0, 0) { CurrentHealth = health };
        return new("ARAM", 12, time, players)
        {
            Active = new(stats, []) { CurrentGold = gold },
            Events = [],
        };
    }
}
