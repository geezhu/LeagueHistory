namespace League.Models;

/// <summary>
/// What one player's recorded augments add up to: general ability haste and health, resistances for estimates of
/// other players, and the per-ability cooldown rules. 2999's own values already contain all of this for the active
/// player, so only the cooldown rules (which 2999's single haste value cannot express) apply to them.
/// </summary>
public sealed record AugmentEffects(
    double AbilityHaste,
    double Health,
    double HealthMultiplier,
    bool GlassCannon,
    double Armor,
    double MagicResist,
    CooldownOptions Cooldown,
    string[] Parts
)
{
    public const double SoulEaterHealth = 20;
    public const double SoulEaterLater = 300;
    public const double GlassCannonShare = 0.7;
    public const double UltimateAwakeningHaste = 30;

    public static readonly AugmentEffects None = new(0, 0, 1, false, 0, 0, CooldownOptions.None, []);

    public string Summary => string.Join("、", Parts);

    /// <summary>Sums the augments; <paramref name="ultimateHaste"/> is the ultimate haste entered by hand.</summary>
    public static AugmentEffects From(IEnumerable<LiveAugmentStack> augments, double ultimateHaste)
    {
        var records = augments
            .Select(stack =>
                (
                    stack.Selection.Augment,
                    stack.State.Stacks,
                    Choice: AugmentCatalog.Choice(stack.Selection.Augment, stack.Selection.Choice)
                )
            )
            .ToArray();
        var effects = None with { Cooldown = Cooldowns(records, ultimateHaste) };
        foreach (var (augment, stacks, choice) in records)
        {
            effects = Add(effects, augment, stacks, choice);
        }

        var parts = records
            .Select(r => $"{AugmentCatalog.Get(r.Augment).Name}（{Bonus(r.Augment, r.Stacks, r.Choice)}）")
            .ToList();
        if (ultimateHaste > 0)
        {
            parts.Add($"手动大招急速 {ultimateHaste:0}");
        }

        return effects with
        {
            Parts = parts.ToArray(),
        };
    }

    // Health increases each multiply maximum health; flat health and resistances add; haste adds for estimates.
    private static AugmentEffects Add(AugmentEffects effects, TrackedAugment augment, int stacks, string choice) =>
        augment switch
        {
            TrackedAugment.InfiniteRecursion or TrackedAugment.ShrinkEngine => effects with
            {
                AbilityHaste = effects.AbilityHaste + AugmentStacks.AbilityHaste(augment, stacks),
            },
            TrackedAugment.Recursion => effects with { AbilityHaste = effects.AbilityHaste + 60 },
            TrackedAugment.BackToBasics => effects with { AbilityHaste = effects.AbilityHaste + 70 },
            TrackedAugment.MadScientist when choice == "haste" => effects with
            {
                AbilityHaste = effects.AbilityHaste + 70,
            },
            TrackedAugment.MadScientist => effects with { HealthMultiplier = effects.HealthMultiplier * 1.2 },
            TrackedAugment.TankEngine => effects with
            {
                HealthMultiplier = effects.HealthMultiplier * (1 + HealthEstimate.TankEngineHealth * stacks),
            },
            TrackedAugment.Goliath => effects with { HealthMultiplier = effects.HealthMultiplier * 1.35 },
            TrackedAugment.DrawYourSword => effects with { HealthMultiplier = effects.HealthMultiplier * 1.24 },
            TrackedAugment.CelestialBody => effects with { Health = effects.Health + 1500 },
            TrackedAugment.SoulEater => effects with
            {
                Health = effects.Health + SoulEaterHealth * stacks + (choice == "later" ? SoulEaterLater : 0),
            },
            TrackedAugment.GlassCannon => effects with { GlassCannon = true },
            TrackedAugment.StayResolute when choice == "magic" => effects with
            {
                MagicResist = effects.MagicResist + stacks,
            },
            TrackedAugment.StayResolute => effects with { Armor = effects.Armor + stacks },
            _ => effects,
        };

    // The per-ability rules 2999's single haste value cannot express; Ultimate Awakening adds to the hand-entered value.
    private static CooldownOptions Cooldowns(
        (TrackedAugment Augment, int Stacks, string Choice)[] records,
        double ultimateHaste
    )
    {
        bool Has(TrackedAugment augment) => records.Any(r => r.Augment == augment);
        var pursuit = records.Where(r => r.Augment == TrackedAugment.PursuitOfHaste && r.Stacks > 0).ToArray();
        return new(
            [Has(TrackedAugment.BreadAndButter), Has(TrackedAugment.BreadAndJam), Has(TrackedAugment.BreadAndCheese)],
            Has(TrackedAugment.PuristCaster),
            pursuit.Length > 0 ? pursuit[0].Choice : "",
            pursuit.Length > 0 ? Math.Min(2, pursuit[0].Stacks) * 80 : 0,
            Has(TrackedAugment.UltBot),
            Math.Max(0, ultimateHaste) + (Has(TrackedAugment.UltimateAwakening) ? UltimateAwakeningHaste : 0)
        );
    }

    /// <summary>What one augment grants at its stacks and choice, in words.</summary>
    public static string Bonus(TrackedAugment augment, int stacks, string choice) =>
        augment switch
        {
            TrackedAugment.InfiniteRecursion or TrackedAugment.ShrinkEngine =>
                $"{stacks} 层 · 技能急速 +{AugmentStacks.AbilityHaste(augment, stacks)}",
            TrackedAugment.TankEngine => $"{stacks} 层 · 最大生命 +{5 * stacks}%",
            TrackedAugment.SoulEater =>
                $"{stacks} 层 · 生命 +{SoulEaterHealth * stacks + (choice == "later" ? SoulEaterLater : 0):0}",
            TrackedAugment.StayResolute => $"{(choice == "magic" ? "魔抗" : "护甲")} +{stacks}",
            TrackedAugment.PursuitOfHaste => stacks > 0 ? $"{choice} 急速 +{Math.Min(2, stacks) * 80}" : "未完成",
            TrackedAugment.MadScientist => choice == "haste" ? "变小 · 技能急速 +70" : "变大 · 生命 +20%",
            _ => AugmentCatalog.Get(augment).Effect,
        };
}
