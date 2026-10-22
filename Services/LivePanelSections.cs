using System.Globalization;
using League.Models;
using Newtonsoft.Json.Linq;
using static League.Services.DetailCards;
using static League.Services.OverlayChampionDetailService;

namespace League.Services;

public sealed record LivePanelInput(
    LiveMatchInsight Insight,
    LivePlayerInsight? Self,
    LiveDefenseAdvice? Defense,
    LiveBuildAdvice? Build,
    int[] OwnedItems
);

/// <summary>
/// The "本局" detail tab: what a player decides during a game, in as little space as the widget has. The battle and
/// the player's own score in two lines, the defense choice as the enemy's damage mix with its verdict, and the next
/// item against the reference build; the ending once the game is over. Attributes, cooldowns and the rune page are on
/// the skills tab or known to the player, so they are not repeated here.
/// </summary>
public static class LivePanelSections
{
    public const string Tab = "live";

    /// <summary>Items on one or two lines: label, value in its tone, a small note, the explanation on hover.</summary>
    public const string Strip = "strip";

    /// <summary>The enemy's physical share as a bar with the verdict beside it, the strip's items under it.</summary>
    public const string Split = "split";

    /// <summary>The next item's icon and name, the reference build's icons with the owned ones dimmed.</summary>
    public const string BuildLayout = "build";

    public static JArray Build(LivePanelInput input, Func<int, string> itemName)
    {
        var sections = new JArray();
        if (input.Insight.Result.Length > 0)
        {
            sections.Add(Ending(input));
        }

        sections.Add(Battle(input.Insight, input.Self));
        if (input.Defense != null)
        {
            sections.Add(Defense(input.Defense));
        }

        if (input.Build != null)
        {
            sections.Add(Equipment(input.Build, input.OwnedItems, itemName));
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

    // An item of a strip; line 1 is the second line.
    private static JObject Item(
        int line,
        string title,
        string value,
        string stats = "",
        string tone = "",
        string tip = ""
    )
    {
        var item = Tile(title, value, stats, tone, tip);
        item["line"] = line;
        return item;
    }

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

    // The teams on the first line, the player on the second.
    private static JObject Battle(LiveMatchInsight insight, LivePlayerInsight? self)
    {
        var last = insight.Fights.LastOrDefault();
        var items = new List<JObject>
        {
            Item(
                0,
                "击杀",
                Versus(insight.Own.Kills, insight.Enemy.Kills),
                tone: DetailCards.Compare(insight.Own.Kills, insight.Enemy.Kills),
                tip: "我方 : 敌方"
            ),
            Item(
                0,
                "经济",
                Lead(insight.GoldLead),
                tone: DetailCards.Compare(insight.GoldLead, 0),
                tip: $"装备总价 {Gold(insight.Own.ItemGold)} : {Gold(insight.Enemy.ItemGold)}。2999 不提供他人金币，按双方装备总价计算。"
            ),
            Item(
                0,
                "团战",
                insight.Fights.Length == 0 ? "暂无" : $"{insight.FightsWon} 胜 {insight.FightsLost} 负",
                last == null ? "" : $"最近 {Clock(last.Start)} 杀 {last.OwnKills} 亡 {last.EnemyKills}",
                DetailCards.Compare(insight.FightsWon, insight.FightsLost),
                "三次以上击杀间隔不超过 12 秒记为一次团战。"
            ),
        };
        if (self != null)
        {
            items.Add(Item(1, "你", self.Score.Length > 0 ? self.Score : "—"));
            items.Add(Item(1, "参团", self.KillParticipation is double kp ? Percent(kp) : "—"));
            items.Add(Item(1, "装备", Gold(self.ItemGold), tip: "装备总价"));
        }

        return Section(Tab, "战况", Strip, items);
    }

    // The verdict first: the enemy's damage mix and which resistance to buy, the effective healths and what 10 more
    // of each would hold under it. Without the mix only the effective healths are known.
    private static JObject Defense(LiveDefenseAdvice defense)
    {
        var health = Item(0, "有效生命", $"物理 {defense.PhysicalHealth:0} · 魔法 {defense.MagicHealth:0}");
        if (defense.PhysicalShare is not double share)
        {
            return Section(Tab, "防装", Strip, [health], "敌方伤害构成暂不可用，仅显示当前有效生命。");
        }

        var section = Section(
            Tab,
            "防装",
            Split,
            [
                health,
                Item(0, $"+{LiveDefenseAdvice.Step:0} 护甲", $"多抵 {defense.ArmorGain:0}"),
                Item(0, $"+{LiveDefenseAdvice.Step:0} 魔抗", $"多抵 {defense.MagicGain:0}"),
            ],
            "按 arammeta 英雄伤害构成估算，不含真实伤害和穿透；「多抵」是多加这些抗性后多抵挡的伤害。"
        );
        section["share"] = share;
        section["left"] = "物理 " + Percent(share);
        section["right"] = "魔法 " + Percent(1 - share);
        section["verdict"] = defense.Priority switch
        {
            LiveDefensePriority.Armor => "下一件优先护甲",
            LiveDefensePriority.MagicResist => "下一件优先魔抗",
            _ => "护甲魔抗收益接近",
        };
        return section;
    }

    private static JObject Equipment(LiveBuildAdvice advice, int[] owned, Func<int, string> itemName)
    {
        var build = advice.Build;
        var section = Section(
            Tab,
            "出装",
            BuildLayout,
            [
                Item(0, "进度", $"{advice.Owned}/{build.Ids.Length}"),
                Item(
                    0,
                    "参照组合",
                    build.WinRate is double win ? "胜率 " + Percent(win) : "胜率 —",
                    build.PickRate is double pick ? "使用率 " + Percent(pick) : ""
                ),
            ],
            "按与已有装备重合最多的推荐组合对照；已拥有的装备显示为暗色。"
        );
        section["nextName"] = advice.Next is int next ? itemName(next) : "已全部完成";
        if (advice.Next is int id)
        {
            section["next"] = Icon("item", id);
        }

        section["icons"] = new JArray(
            build.Ids.Select(item =>
            {
                var icon = Icon("item", item);
                icon["owned"] = owned.Contains(item);
                return icon;
            })
        );
        return section;
    }
}
