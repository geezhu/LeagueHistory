using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// Howling Abyss turret stats over time, fortification after the outer turret, super minion waves and the player's
/// basic attacks against the next turret.
/// </summary>
internal static class TurretChecks
{
    public static void Run()
    {
        CheckStats();
        CheckFortification();
        CheckSuperWaves();
        CheckSiege();
        Console.WriteLine(
            "PASS: turret stats and growth, Mayhem nexus health, fortification timers, super waves and siege arithmetic."
        );
    }

    private static void CheckStats()
    {
        var rules = TurretRules.HowlingAbyss;
        Check(rules.At(LaneStructureKind.Outer, 0, false) == new TurretStats(1300, 185, 60), "Outer turret at start");
        Check(rules.At(LaneStructureKind.Outer, 600, false) == new TurretStats(1300, 275, 81), "Outer turret grows");
        Check(
            rules.At(LaneStructureKind.Outer, 720, false) == new TurretStats(1300, 293, 40),
            "Outer resistances fall at 12:00 as damage stops growing"
        );
        Check(rules.At(LaneStructureKind.Inner, 1200, false) == new TurretStats(3900, 385, 84), "Inner turret caps");
        Check(rules.At(LaneStructureKind.Nexus, 0, true)?.Health == 1200, "Mayhem nexus turrets have less health");
        Check(rules.At(LaneStructureKind.Inhibitor, 0, false) == null, "Inhibitors are not turrets");
        Check(new TurretStats(1300, 185, 60).EffectiveHealth == 2080, "Effective health counts resistances");
    }

    private static void CheckFortification()
    {
        LiveMatchEvent Outer(double time) =>
            new(1, "TurretKilled", time, "", "") { Structure = "Turret_TChaos_L1_P3_671196429_0" };
        LaneMarker EnemyInner(LiveMatchSnapshot game) =>
            MinionWaveForecaster
                .Calculate(game, 0)!
                .Lanes[0]
                .Progress!.Markers.Single(m => !m.Ally && m.Kind == LaneStructureKind.Inner);

        var aram = EnemyInner(Game("ARAM", 530, Outer(500)));
        Check(aram.FortifiedUntil == 560 && aram.Fortification == 0.3, "The inner turret is fortified for 60 seconds");
        var mayhem = EnemyInner(Game("KIWI", 530, Outer(500)));
        Check(mayhem.FortifiedUntil == 570 && mayhem.Fortification == 0.4, "Mayhem fortifies longer and harder");
        Check(EnemyInner(Game("ARAM", 561, Outer(500))).FortifiedUntil == null, "Fortification ends");
        Check(EnemyInner(Game("ARAM", 530)).FortifiedUntil == null, "No fortification while the outer turret stands");

        var forecast = MinionWaveForecaster.Calculate(Game("ARAM", 530, Outer(500)), 0)!;
        var markers = forecast.Lanes[0].Progress!.Markers;
        Check(
            markers
                .Where(m => m.Kind is LaneStructureKind.Outer or LaneStructureKind.Inner or LaneStructureKind.Nexus)
                .All(m => m.Details.Length > 0)
                && markers
                    .Where(m => m.Kind is LaneStructureKind.Inhibitor or LaneStructureKind.Base)
                    .All(m => m.Details.Length == 0),
            "Turrets carry their stats; inhibitors and the nexus do not"
        );
        var json = (JObject)
            forecast.ToJson()["lanes"]![0]!["progress"]!["markers"]!.First(m =>
                (string?)m["kind"] == "turret" && (bool?)m["ally"] == false && (double?)m["fortifiedUntil"] == 560
            );
        Check(((string?)json["details"])!.Contains("满血 3900"), "Turret details travel to the widget");
        Check(
            forecast.Lanes[0].Progress!.Markers.Single(m => !m.Ally && m.Kind == LaneStructureKind.Outer)
                is { Alive: false, FortifiedUntil: null },
            "A fallen turret has no fortification"
        );
    }

    private static void CheckSuperWaves()
    {
        var inhibitor = new LiveMatchEvent(1, "InhibKilled", 500, "", "")
        {
            Structure = "Inhib_TChaos_L1_P1_2432143522_0",
        };
        // Waves spawn every 25 seconds from 0:50, so 525, 550, 575 and 600 follow the 500 second fall.
        var text = MinionWaveForecaster.Calculate(Game("ARAM", 600, inhibitor), 0)!.Lanes[0].Structures;
        Check(text.Contains("超级兵约还剩 6 波"), "Super minion waves count down: " + text);
        var later = MinionWaveForecaster.Calculate(Game("ARAM", 800, inhibitor), 0)!.Lanes[0].Structures;
        Check(later.Contains("超级兵约已出完"), "Super minion waves run out: " + later);
    }

    private static void CheckSiege()
    {
        Check(TurretRules.HowlingAbyss.StructureDamage(1440) == 1.125, "Structure damage rises from 18:00 to 30:00");

        // 5:00 outer turret: 1300 health, 75 armor, 230 damage. 200 attack damage hits for 200 × 100 / 175.
        var ranged = Siege(Game("ARAM", 300) with { Active = Player(200, 0, 550) })!;
        Check(
            ranged is { Target: "敌方一塔", Hits: 12, Seconds: 11, HitsAlone: 57, Magic: false }
                && Math.Abs(ranged.HitDamage - 200 / 1.75) < 1e-9,
            "Ranged attacks against the outer turret"
        );
        // 35 effective armor after the turret's 30% penetration: 230, 345, 460 and 575 damage before mitigation.
        Check(
            ranged.Shots.Select(s => Math.Round(s)).SequenceEqual([170, 256, 341, 426]) && ranged.ShotsToDown == 4,
            "The turret warms up against the player: " + string.Join("/", ranged.Shots)
        );
        Check(Siege(Game("ARAM", 300) with { Active = Player(200, 0, 125) })!.Hits == 10, "Melee champions hit harder");
        var mage = Siege(Game("ARAM", 300) with { Active = Player(60, 400, 550) })!;
        Check(mage is { Magic: true, Hits: 8 }, "Ability power makes the hit magic");

        LiveMatchEvent Fell(string structure) => new(1, "TurretKilled", 500, "", "") { Structure = structure };
        var fortified = Siege(
            Game("ARAM", 530, Fell("Turret_TChaos_L1_P3_671196429_0")) with
            {
                Active = Player(200, 0, 550),
            }
        )!;
        var open = Siege(
            Game("ARAM", 565, Fell("Turret_TChaos_L1_P3_671196429_0")) with
            {
                Active = Player(200, 0, 550),
            }
        )!;
        Check(
            fortified is { Target: "敌方二塔", Fortification: 0.3 }
                && Math.Abs(fortified.HitDamage - open.HitDamage * 0.7) < 1e-9,
            "Fortification reduces the hits on the inner turret"
        );
        var inhibitorNext = Game(
            "ARAM",
            700,
            Fell("Turret_TChaos_L1_P3_671196429_0"),
            Fell("Turret_TChaos_L1_P2_620863572_0")
        ) with
        {
            Active = Player(200, 0, 550),
        };
        Check(Siege(inhibitorNext) == null, "An inhibitor without reinforced armor gets no turret numbers");
        Check(Siege(Game("ARAM", 300)) == null, "No numbers without the player's stats");

        var json = MinionWaveForecaster.Calculate(Game("ARAM", 300) with { Active = Player(200, 0, 550) }, 0)!.ToJson();
        Check((int?)json["lanes"]?[0]?["siege"]?["hitsAlone"] == 57, "Siege numbers travel to the widget");
    }

    private static TurretSiege? Siege(LiveMatchSnapshot game) =>
        MinionWaveForecaster.Calculate(game, 0)!.Lanes[0].Siege;

    private static LiveActivePlayer Player(double attack, double power, double range) =>
        new(
            new LiveChampionStats(2000, 50, 40, 0, 0, 0, 0, 0)
            {
                CurrentHealth = 1000,
                AttackDamage = attack,
                AbilityPower = power,
                AttackSpeed = 1,
                AttackRange = range,
            },
            []
        );

    private static LiveMatchSnapshot Game(string mode, double time, params LiveMatchEvent[] events) =>
        new(mode, 12, time, [new("我|0", "我", "Lux", "拉克丝", "ORDER", true, 8, null)]) { Events = events };
}
