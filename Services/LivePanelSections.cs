using System.Globalization;
using League.Models;
using Newtonsoft.Json.Linq;
using static League.Services.DetailCards;
using static League.Services.OverlayChampionDetailService;

namespace League.Services;

public sealed record LivePanelInput(
    LiveMatchInsight Insight,
    LivePlayerInsight? Self,
    LiveChampionStats? Stats,
    LiveDefenseAdvice? Defense,
    LiveBuildAdvice? Build,
    int[] OwnedItems,
    int[] Runes,
    string RuneSource
)
{
    /// <summary>The active player's ability cooldowns now; empty until the champion's data has loaded.</summary>
    public AbilityCooldown[] Cooldowns { get; init; } = [];

    /// <summary>The cooldown page's options for the player, already applied to <see cref="Cooldowns"/>.</summary>
    public CooldownOptions CooldownOptions { get; init; } = CooldownOptions.None;
}

/// <summary>
/// The "本局" detail tab as cards: the ending, the battle against the enemy team, the active player's state and
/// attributes, the armor or magic resist choice, the next item and the rune page.
/// </summary>
public static class LivePanelSections
{
    public const string Tab = "live";

    public static JArray Build(LivePanelInput input, Func<int, string> itemName)
    {
        var sections = new JArray();
        if (input.Insight.Result.Length > 0)
        {
            sections.Add(Ending(input));
        }

        sections.Add(Battle(input.Insight));
        if (input.Self != null || input.Stats != null)
        {
            sections.Add(State(input));
        }

        if (input.Cooldowns.Length > 0)
        {
            sections.Add(Cooldowns(input.Cooldowns, input.Stats?.AbilityHaste ?? 0, input.CooldownOptions.Summary()));
        }

        if (input.Defense?.PhysicalShare is double share)
        {
            sections.Add(DefenseChoice(input.Defense, share));
        }

        if (input.Build != null)
        {
            sections.Add(Equipment(input.Build, input.OwnedItems, itemName));
        }

        if (input.Runes.Length > 0)
        {
            var runes = Section(Tab, "符文页", Tiles, [], input.RuneSource);
            runes["icons"] = new JArray(input.Runes.Select(id => Icon("rune", id)));
            sections.Add(runes);
        }

        return sections;
    }

    public static string Gold(int value) =>
        value >= 1000 ? (value / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k" : value.ToString();

    public static string Lead(int lead) =>
        lead == 0 ? "经济持平"
        : lead > 0 ? "领先 " + Gold(lead)
        : "落后 " + Gold(-lead);

    public static string Percent(double value) => (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string Clock(double seconds) => $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";

    private static string Versus(int own, int enemy) => $"{own} : {enemy}";

    private static JObject Ending(LivePanelInput input)
    {
        var insight = input.Insight;
        string result = insight.Result switch
        {
            "Win" => "胜利",
            "Lose" => "失败",
            _ => insight.Result,
        };
        return Section(
            Tab,
            "本局结束",
            Tiles,
            [
                Tile(
                    "结果",
                    result,
                    input.Self?.Score is { Length: > 0 } score ? "你 " + score : "",
                    insight.Result switch
                    {
                        "Win" => "good",
                        "Lose" => "bad",
                        _ => "",
                    }
                ),
                Tile("击杀", Versus(insight.Own.Kills, insight.Enemy.Kills), "我方 : 敌方"),
                Tile("团战", $"{insight.FightsWon} 胜 {insight.FightsLost} 负", ""),
                Tile("经济", Lead(insight.GoldLead), "", DetailCards.Compare(insight.GoldLead, 0)),
            ],
            "完整数据在结算后进入战绩，可在主程序战绩页查看。"
        );
    }

    private static JObject Battle(LiveMatchInsight insight)
    {
        var last = insight.Fights.LastOrDefault();
        return Section(
            Tab,
            "战况",
            Tiles,
            [
                Tile(
                    "击杀",
                    Versus(insight.Own.Kills, insight.Enemy.Kills),
                    "我方 : 敌方",
                    DetailCards.Compare(insight.Own.Kills, insight.Enemy.Kills)
                ),
                Tile(
                    "经济",
                    Lead(insight.GoldLead),
                    $"装备 {Gold(insight.Own.ItemGold)} : {Gold(insight.Enemy.ItemGold)}",
                    DetailCards.Compare(insight.GoldLead, 0),
                    "按双方装备总价计算：2999 不提供他人金币。"
                ),
                Tile(
                    "团战",
                    insight.Fights.Length == 0 ? "暂无" : $"{insight.FightsWon} 胜 {insight.FightsLost} 负",
                    last == null ? "" : $"{Clock(last.Start)} 杀 {last.OwnKills} 亡 {last.EnemyKills}",
                    DetailCards.Compare(insight.FightsWon, insight.FightsLost),
                    "三次以上击杀间隔不超过 12 秒记为一次团战；小字为最近一次的时间和我方击杀、阵亡。"
                ),
                Tile(
                    "团灭",
                    Versus(insight.Own.Aces, insight.Enemy.Aces),
                    "我方 : 敌方",
                    DetailCards.Compare(insight.Own.Aces, insight.Enemy.Aces)
                ),
                Tile(
                    "多杀",
                    Versus(insight.Own.Multikills, insight.Enemy.Multikills),
                    "我方 : 敌方",
                    DetailCards.Compare(insight.Own.Multikills, insight.Enemy.Multikills)
                ),
            ]
        );
    }

    // One tile per ability: the cooldown after haste, with the rank and the base cooldown under it.
    private static JObject Cooldowns(AbilityCooldown[] cooldowns, double haste, string options)
    {
        var tiles = cooldowns.Select(cooldown =>
        {
            string title = $"{cooldown.Key} {cooldown.Name}";
            string rank = $"{cooldown.Rank}/{cooldown.Ranks} 级";
            if (cooldown.Sealed)
            {
                return Tile(title, "已封印", "回归基本功");
            }

            if (cooldown.Rank == 0)
            {
                return Tile(title, "未学", "");
            }

            if (cooldown.Base <= 0)
            {
                return Tile(title, "—", rank + " · 数据里没有冷却");
            }

            string charges = cooldown.Charges > 0 ? $" · {cooldown.Charges} 层充能" : "";
            return Tile(
                title,
                (cooldown.Charges > 0 ? "充能 " : "") + Seconds(cooldown.Current),
                $"{rank}{charges} · 基础 {Seconds(cooldown.Base)}",
                "info"
            );
        });
        return Section(
            Tab,
            "技能冷却",
            Tiles,
            tiles,
            $"按 2999 的技能急速 {haste:0}（与游戏属性面板相同）计算：冷却 = 基础冷却 × 100 / (100 + 急速)。"
                + "充能技能显示每层的充能时间。"
                + (
                    options.Length > 0
                        ? $"已计入「海克斯」页的记录：{options}。"
                        : "大招急速和只对部分技能生效的海克斯（大招工具人、面包系列等）可在「海克斯」页记录。"
                )
                + "技能命中后的冷却返还未计入。"
        );
    }

    private static string Seconds(double seconds) =>
        seconds.ToString(seconds < 10 ? "0.#" : "0", CultureInfo.InvariantCulture) + " 秒";

    // The player's score and survivability as tiles; attributes as smaller tiles under them.
    private static JObject State(LivePanelInput input)
    {
        var tiles = new List<JObject>();
        if (input.Self is { } self)
        {
            tiles.Add(Tile("KDA", self.Score.Length > 0 ? self.Score : "—", ""));
            tiles.Add(Tile("参团", self.KillParticipation is double kp ? Percent(kp) : "—", ""));
            tiles.Add(Tile("装备", Gold(self.ItemGold), "装备总价"));
        }

        if (input.Defense is { } defense)
        {
            tiles.Add(Tile("物理有效生命", $"{defense.PhysicalHealth:0}", "抵挡物理伤害"));
            tiles.Add(Tile("魔法有效生命", $"{defense.MagicHealth:0}", "抵挡魔法伤害"));
        }
        else if (input.Stats is { } health)
        {
            tiles.Add(Tile("生命", $"{health.MaxHealth:0}", ""));
        }

        var section = Section(Tab, "你的状态", Tiles, tiles);
        if (input.Stats is { } stats)
        {
            double reduction = stats.AbilityHaste > 0 ? stats.AbilityHaste / (100 + stats.AbilityHaste) : 0;
            section["signals"] = new JArray(
                Tile("护甲", $"{stats.Armor:0}", ""),
                Tile("魔抗", $"{stats.MagicResist:0}", ""),
                Tile("技能急速", $"{stats.AbilityHaste:0}", reduction > 0 ? "冷却 −" + Percent(reduction) : ""),
                Tile(
                    "物理穿透",
                    $"{stats.PhysicalLethality:0} + {Percent(stats.ArmorPenetrationPercent)}",
                    "固定 + 百分比"
                ),
                Tile(
                    "法术穿透",
                    $"{stats.MagicPenetrationFlat:0} + {Percent(stats.MagicPenetrationPercent)}",
                    "固定 + 百分比"
                )
            );
        }

        if (input.Defense?.PhysicalShare == null)
        {
            section["tip"] = "敌方伤害构成暂不可用，仅显示当前有效生命。";
        }

        return section;
    }

    private static JObject DefenseChoice(LiveDefenseAdvice defense, double share)
    {
        string choice = defense.Priority switch
        {
            LiveDefensePriority.Armor => "优先护甲",
            LiveDefensePriority.MagicResist => "优先魔抗",
            _ => "收益接近",
        };
        return Section(
            Tab,
            "防装建议",
            Tiles,
            [
                Tile("敌方物理伤害", Percent(share), "魔法 " + Percent(1 - share)),
                Tile($"+{LiveDefenseAdvice.Step:0} 护甲", $"多抵 {defense.ArmorGain:0}", "点伤害"),
                Tile($"+{LiveDefenseAdvice.Step:0} 魔抗", $"多抵 {defense.MagicGain:0}", "点伤害"),
                Tile("下一件防装", choice, "", "info"),
            ],
            "按 arammeta 英雄伤害构成估算，不含真实伤害和穿透。"
        );
    }

    private static JObject Equipment(LiveBuildAdvice advice, int[] owned, Func<int, string> itemName)
    {
        var build = advice.Build;
        var section = Section(
            Tab,
            "出装对照",
            Tiles,
            [
                Tile(
                    "下一件",
                    advice.Next is int next ? itemName(next) : "已全部完成",
                    "",
                    advice.Next == null ? "good" : "info"
                ),
                Tile(
                    "参照组合",
                    build.WinRate is double win ? "胜率 " + Percent(win) : "胜率 —",
                    build.PickRate is double pick ? "使用率 " + Percent(pick) : ""
                ),
                Tile("进度", $"{advice.Owned}/{build.Ids.Length}", "已有装备"),
            ],
            "按与已有装备重合最多的推荐组合对照；已拥有的装备显示为暗色。"
        );
        section["icons"] = new JArray(
            build.Ids.Select(id =>
            {
                var icon = Icon("item", id);
                icon["owned"] = owned.Contains(id);
                return icon;
            })
        );
        return section;
    }
}
