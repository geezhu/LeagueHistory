namespace League.Models;

/// <summary>Mayhem augments the augment page can record, because they change health, resistances or cooldowns.</summary>
public enum TrackedAugment
{
    InfiniteRecursion,
    ShrinkEngine,
    TankEngine,
    SoulEater,
    StayResolute,
    Recursion,
    BackToBasics,
    UltBot,
    UltimateAwakening,
    PuristCaster,
    BreadAndButter,
    BreadAndJam,
    BreadAndCheese,
    PursuitOfHaste,
    CelestialBody,
    Goliath,
    DrawYourSword,
    GlassCannon,
    MadScientist,
}

/// <summary>How an augment's stacks are known.</summary>
public enum AugmentKind
{
    /// <summary>Counted from 2999's takedowns and deaths after the anchor.</summary>
    Takedowns,

    /// <summary>Triggered by hits 2999 does not report; the user keeps the count.</summary>
    Manual,

    /// <summary>A fixed bonus; nothing to count.</summary>
    Fixed,
}

/// <summary>A choice an augment needs: its stored key and the label shown for it.</summary>
public sealed record AugmentChoice(string Key, string Label);

/// <summary>An augment's client name, how its stacks are known, what it does here and the choices it needs.</summary>
public sealed record TrackedAugmentInfo(
    TrackedAugment Augment,
    string Name,
    AugmentKind Kind,
    string Effect,
    AugmentChoice[] Choices
);

/// <summary>
/// The recorded augments, with their client names. Effects follow the Wiki's Mayhem data; "% bonus health" is read as
/// the client's HealthAmp, a share of maximum health, like Tank Engine's.
/// </summary>
public static class AugmentCatalog
{
    private static readonly AugmentChoice[] Abilities = [new("Q", "Q"), new("W", "W"), new("E", "E"), new("R", "R")];

    public static readonly TrackedAugmentInfo[] All =
    [
        new(
            TrackedAugment.InfiniteRecursion,
            "无限循环往复",
            AugmentKind.Takedowns,
            "技能急速 60，每次参与击杀 +3，死亡不掉层",
            []
        ),
        new(
            TrackedAugment.ShrinkEngine,
            "缩小引擎",
            AugmentKind.Takedowns,
            "每次参与击杀 +1 层：技能急速 +8、移速 +1%；死亡失去 65% 层数",
            []
        ),
        new(
            TrackedAugment.TankEngine,
            "坦克引擎",
            AugmentKind.Takedowns,
            "每次参与击杀 +1 层：最大生命 +5%；死亡失去 65% 层数",
            []
        ),
        new(
            TrackedAugment.SoulEater,
            "吞噬灵魂",
            AugmentKind.Manual,
            "每次定住敌方英雄生命 +20，无限叠加；不是第一个海克斯时另加 300 生命",
            [new("first", "第一个"), new("later", "非第一个")]
        ),
        new(
            TrackedAugment.StayResolute,
            "保持坚定",
            AugmentKind.Manual,
            "指定技能命中敌方英雄时永久获得 1~3 护甲或魔抗；层数按已获得的抗性总量记录",
            [new("armor", "护甲"), new("magic", "魔抗")]
        ),
        new(TrackedAugment.Recursion, "循环往复", AugmentKind.Fixed, "技能急速 +60", []),
        new(TrackedAugment.BackToBasics, "回归基本功", AugmentKind.Fixed, "技能急速 +70，大招被封印", []),
        new(
            TrackedAugment.UltBot,
            "大招工具人",
            AugmentKind.Fixed,
            "技能急速只作用于大招并 +100；大招冷却按 (技能急速 + 大招急速) × 1.5",
            []
        ),
        new(
            TrackedAugment.UltimateAwakening,
            "终极唤醒",
            AugmentKind.Fixed,
            "大招急速 +30（重置技能为临时效果，不计）",
            []
        ),
        new(TrackedAugment.PuristCaster, "纯粹主义者 - 术师", AugmentKind.Fixed, "技能总冷却 ×0.9", []),
        new(TrackedAugment.BreadAndButter, "面包和黄油", AugmentKind.Fixed, "Q 技能急速 +100", []),
        new(TrackedAugment.BreadAndJam, "面包和果酱", AugmentKind.Fixed, "W 技能急速 +100", []),
        new(TrackedAugment.BreadAndCheese, "面包和奶酪", AugmentKind.Fixed, "E 技能急速 +100", []),
        new(
            TrackedAugment.PursuitOfHaste,
            "急速之追求",
            AugmentKind.Manual,
            "指定技能命中 10 次急速 +80，30 次共 +160；层数记已完成的档（1 或 2）",
            Abilities
        ),
        new(TrackedAugment.CelestialBody, "星界躯体", AugmentKind.Fixed, "生命 +1500，造成的伤害 −10%", []),
        new(TrackedAugment.Goliath, "歌利亚巨人", AugmentKind.Fixed, "生命 +35%，适应之力 +15%", []),
        new(
            TrackedAugment.DrawYourSword,
            "亮出你的剑",
            AugmentKind.Fixed,
            "变为近战，生命 +24% 等（远程英雄可选）",
            []
        ),
        new(TrackedAugment.GlassCannon, "玻璃大炮", AugmentKind.Fixed, "生命上限锁定为最大生命的 70%", []),
        new(
            TrackedAugment.MadScientist,
            "科学狂人",
            AugmentKind.Fixed,
            "每次复活随机：变大（生命 +20%）或变小（技能急速 +70）",
            [new("health", "变大"), new("haste", "变小")]
        ),
    ];

    // Pinyin and initials let the widget's search work without a Chinese input method, which the Game Bar overlay
    // may not switch to; the tags drive its category buttons.
    private static readonly Dictionary<TrackedAugment, (string Pinyin, string[] Tags)> Search = new()
    {
        [TrackedAugment.InfiniteRecursion] = ("wuxianxunhuanwangfu wxxhwf", ["急速"]),
        [TrackedAugment.ShrinkEngine] = ("suoxiaoyinqing sxyq", ["急速"]),
        [TrackedAugment.TankEngine] = ("tankeyinqing tkyq", ["生命"]),
        [TrackedAugment.SoulEater] = ("tunshilinghun tslh", ["生命"]),
        [TrackedAugment.StayResolute] = ("baochijianding bcjd", ["抗性"]),
        [TrackedAugment.Recursion] = ("xunhuanwangfu xhwf", ["急速"]),
        [TrackedAugment.BackToBasics] = ("huiguijibengong hgjbg", ["急速"]),
        [TrackedAugment.UltBot] = ("dazhaogongjuren dzgjr", ["急速", "冷却"]),
        [TrackedAugment.UltimateAwakening] = ("zhongjihuanxing zjhx", ["冷却"]),
        [TrackedAugment.PuristCaster] = ("chuncuizhuyizheshushi cczyzss", ["冷却"]),
        [TrackedAugment.BreadAndButter] = ("mianbaohehuangyou mbhhy", ["冷却"]),
        [TrackedAugment.BreadAndJam] = ("mianbaoheguojiang mbhgj", ["冷却"]),
        [TrackedAugment.BreadAndCheese] = ("mianbaohenailao mbhnl", ["冷却"]),
        [TrackedAugment.PursuitOfHaste] = ("jisuzhizhuiqiu jszzq", ["冷却"]),
        [TrackedAugment.CelestialBody] = ("xingjiequti xjqt", ["生命"]),
        [TrackedAugment.Goliath] = ("geliyajuren glyjr", ["生命"]),
        [TrackedAugment.DrawYourSword] = ("liangchunidejian lcndj", ["生命"]),
        [TrackedAugment.GlassCannon] = ("bolidapao bldp", ["生命"]),
        [TrackedAugment.MadScientist] = ("kexuekuangren kxkr", ["生命", "急速"]),
    };

    public static TrackedAugmentInfo Get(TrackedAugment augment) => All.First(info => info.Augment == augment);

    /// <summary>Full pinyin and initials of the name, for searching without a Chinese input method.</summary>
    public static string Pinyin(TrackedAugment augment) =>
        Search.TryGetValue(augment, out var entry) ? entry.Pinyin : "";

    public static string[] Tags(TrackedAugment augment) => Search.TryGetValue(augment, out var entry) ? entry.Tags : [];

    /// <summary>The choice stored for a selection, or the augment's first choice when none was stored.</summary>
    public static string Choice(TrackedAugment augment, string choice)
    {
        var choices = Get(augment).Choices;
        return choices.Any(c => c.Key == choice) ? choice : choices.FirstOrDefault()?.Key ?? "";
    }
}
