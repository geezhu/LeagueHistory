using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

/// <summary>
/// Health estimates for players 2999 gives no stats for: Data Dragon base stats grown by level, item stats from the
/// client's item text and Tank Engine stacks; the player's own line uses 2999's real stats.
/// </summary>
internal static class HealthEstimateChecks
{
    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new Exception(message);
        }
    }

    // Maokai from Data Dragon 16.20.1: a reference for the growth curve.
    private static JObject ChampionList() =>
        JObject.Parse(
            """
            {"data":{
              "Maokai":{"key":"57","stats":{"hp":665,"hpperlevel":109,"armor":35,"armorperlevel":5.2,
                "spellblock":32,"spellblockperlevel":2.05}},
              "Broken":{"key":"x","stats":{"hp":1}},
              "Missing":{"key":"58","stats":{"hp":600}}
            }}
            """
        );

    public static void Run()
    {
        var all = ChampionBaseStats.ParseAll(ChampionList());
        Check(all.Keys.SequenceEqual([57]), "Champions with a numeric key and every stat are kept");
        var maokai = all[57];
        Check(
            Math.Abs(ChampionBaseStats.Grown(maokai.Health, maokai.HealthPerLevel, 17) - 2378.48) < 0.01
                && ChampionBaseStats.Grown(maokai.Health, maokai.HealthPerLevel, 1) == 665,
            "Growth follows the game's curve: 665 + 109 × 16 × (0.7025 + 0.0175 × 16)"
        );

        var blade = ItemStats.Parse(
            "<mainText><stats><attention>30</attention>攻击力<br><attention>150</attention>生命值<br>"
                + "<attention>15</attention>技能急速</stats><br><br></mainText>"
        );
        var warmog = ItemStats.Parse(
            "<mainText><stats><attention>400</attention>生命值<br><attention>50</attention>魔法抗性<br>"
                + "<attention>100%</attention>基础生命回复</stats><passive>生命值</passive>"
                + "<attention>300</attention>生命值</mainText>"
        );
        // Compare the fields this rule is about rather than the whole record: ItemStats carries more than the four
        // defensive stats, and a new one must not turn "percents and passive text do not count" red.
        Check(
            blade.Health == 150
                && blade.AbilityHaste == 15
                && blade.Armor == 0
                && blade.MagicResist == 0
                && warmog.Health == 400
                && warmog.MagicResist == 50
                && warmog.AbilityHaste == 0
                && ItemStats.Parse(null) == ItemStats.None,
            "Only flat stats in the stats block count; percents and passive text do not: " + warmog
        );

        var tank = AugmentEffects.From(
            [new(new("p", TrackedAugment.TankEngine, new(0, 2)), new(2, 2, 0, true, ""))],
            0
        );
        var estimate = HealthEstimate.Estimate(maokai, 17, ItemStats.Sum([blade, warmog]), tank);
        Check(
            Math.Abs(estimate.Health - (2378.48 + 550) * 1.1) < 0.01
                && Math.Abs(estimate.PhysicalEffective - estimate.Health * (1 + estimate.Armor / 100)) < 0.01
                && new HealthEstimate(1000, -20, 0).PhysicalEffective < 1000,
            "Tank Engine multiplies the health; resistances turn it into effective health: " + estimate
        );

        var other = PlayerHealthText.Describe(estimate, 17, ItemStats.Sum([blade, warmog]), tank, null);
        var self = PlayerHealthText.Describe(estimate, 17, ItemStats.None, AugmentEffects.None, Stats(4414, 119, 74));
        Check(
            other is { Text: "约 3221 · 物理 6982 · 法术 6901" }
                && other.Details.Contains("已记录的海克斯：坦克引擎（2 层 · 最大生命 +10%）")
                && other.Details.Contains("未含未记录的海克斯")
                && self is { Text: "4414 · 物理 9667 · 法术 7680" }
                && self.Details.Contains("同一公式估算生命 3221，实际高 37%")
                && PlayerHealthText.Describe(null, 17, ItemStats.None, AugmentEffects.None, null) == null,
            "Others get an approximate line, the player the real one with the estimate's gap: "
                + other?.Text
                + " / "
                + self?.Text
                + " / "
                + self?.Details
        );
        Task.Run(CacheAsync).GetAwaiter().GetResult();
        Console.WriteLine(
            "PASS: health estimates from base stats, growth, items and Tank Engine, and the player's real stats."
        );
    }

    private static LiveChampionStats Stats(double health, double armor, double magicResist) =>
        new(health, armor, magicResist, 0, 0, 0, 0, 0);

    // A cached version answers without a request.
    private static async Task CacheAsync()
    {
        string folder = Path.Combine(Path.GetTempPath(), "league-base-stats-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "16.20.1.json"), ChampionList().ToString());
            var stats = await new ChampionBaseStatsData(folder, "16.20.1").LoadAsync(CancellationToken.None);
            Check(stats.Keys.SequenceEqual([57]), "Cached stats are read for the client's version");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
