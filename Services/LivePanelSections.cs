using System.Globalization;
using League.Models;
using Newtonsoft.Json.Linq;
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
);

/// <summary>The "本局" detail tab: battle summary, survivability, next item and rune page, as detail rows.</summary>
public static class LivePanelSections
{
    public const string Tab = "live";

    public static JArray Build(LivePanelInput input, Func<int, string> itemName)
    {
        var sections = new JArray();
        if (input.Insight.Result.Length > 0)
        {
            sections.Add(Section(Tab, "本局结束", [Ending(input)]));
        }

        sections.Add(Section(Tab, "战况", Battle(input)));
        if (input.Stats != null)
        {
            sections.Add(Section(Tab, "生存与属性", [Defense(input.Stats, input.Defense)]));
        }

        if (input.Build != null)
        {
            sections.Add(Section(Tab, "出装对照", [Equipment(input.Build, input.OwnedItems, itemName)]));
        }

        if (input.Runes.Length > 0)
        {
            sections.Add(
                Section(Tab, "符文页", [Row("本局符文", input.RuneSource, input.Runes.Select(id => Icon("rune", id)))])
            );
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

    private static string SelfLine(LivePlayerInsight self) =>
        "你 "
        + (self.Score.Length > 0 ? self.Score : "—")
        + (self.KillParticipation is double kp ? " · 参团 " + Percent(kp) : "")
        + " · 装备 "
        + Gold(self.ItemGold);

    private static JObject Ending(LivePanelInput input)
    {
        var insight = input.Insight;
        string result = insight.Result switch
        {
            "Win" => "胜利",
            "Lose" => "失败",
            _ => insight.Result,
        };
        return Row(
            result + (input.Self != null ? " · " + SelfLine(input.Self) : ""),
            "完整数据在结算后进入战绩，可在主程序战绩页查看。",
            stats: $"击杀 {insight.Own.Kills} : {insight.Enemy.Kills} · 团战 {insight.FightsWon} 胜 {insight.FightsLost} 负 · {Lead(insight.GoldLead)}"
        );
    }

    private static IEnumerable<JObject> Battle(LivePanelInput input)
    {
        var insight = input.Insight;
        string fights =
            insight.Fights.Length == 0 ? "暂无团战" : $"团战 {insight.FightsWon} 胜 {insight.FightsLost} 负";
        var last = insight.Fights.LastOrDefault();
        yield return Row(
            $"装备总价 {Gold(insight.Own.ItemGold)} : {Gold(insight.Enemy.ItemGold)} · {Lead(insight.GoldLead)}",
            last == null
                ? "三次以上击杀间隔不超过 12 秒记为一次团战。"
                : $"最近团战 {Clock(last.Start)} · 我方击杀 {last.OwnKills}、阵亡 {last.EnemyKills}",
            stats: $"击杀 {insight.Own.Kills} : {insight.Enemy.Kills} · {fights} · 团灭 {insight.Own.Aces} : {insight.Enemy.Aces} · 多杀 {insight.Own.Multikills} : {insight.Enemy.Multikills}"
        );
        if (input.Self != null)
        {
            yield return Row(SelfLine(input.Self));
        }
    }

    private static JObject Defense(LiveChampionStats stats, LiveDefenseAdvice? defense)
    {
        string title =
            defense == null
                ? $"生命 {stats.MaxHealth:0}"
                : $"物理有效生命 {defense.PhysicalHealth:0} · 魔法有效生命 {defense.MagicHealth:0}";
        double reduction = stats.AbilityHaste > 0 ? stats.AbilityHaste / (100 + stats.AbilityHaste) : 0;
        string detail =
            $"护甲 {stats.Armor:0} · 魔抗 {stats.MagicResist:0} · 技能急速 {stats.AbilityHaste:0}"
            + (reduction > 0 ? $"（冷却缩减 {Percent(reduction)}）" : "")
            + $" · 物穿 {stats.PhysicalLethality:0} + {Percent(stats.ArmorPenetrationPercent)}"
            + $" · 法穿 {stats.MagicPenetrationFlat:0} + {Percent(stats.MagicPenetrationPercent)}";
        return Row(title, Advice(defense), stats: detail);
    }

    private static string Advice(LiveDefenseAdvice? defense)
    {
        if (defense?.PhysicalShare is not double share)
        {
            return "敌方伤害构成暂不可用，仅显示当前有效生命。";
        }

        string choice = defense.Priority switch
        {
            LiveDefensePriority.Armor => "下一件防装优先护甲",
            LiveDefensePriority.MagicResist => "下一件防装优先魔抗",
            _ => "护甲和魔抗收益接近",
        };
        return $"敌方物理伤害约 {Percent(share)}：+{LiveDefenseAdvice.Step:0} 护甲多抵 {defense.ArmorGain:0} 点、"
            + $"+{LiveDefenseAdvice.Step:0} 魔抗多抵 {defense.MagicGain:0} 点 → {choice}。"
            + "按 arammeta 英雄伤害构成估算，不含真实伤害和穿透。";
    }

    private static JObject Equipment(LiveBuildAdvice advice, int[] owned, Func<int, string> itemName)
    {
        var build = advice.Build;
        var icons = build
            .Ids.Select(id =>
            {
                var icon = Icon("item", id);
                icon["owned"] = owned.Contains(id);
                return icon;
            })
            .ToArray();
        string rate =
            (build.WinRate is double win ? "胜率 " + Percent(win) : "胜率 —")
            + (build.PickRate is double pick ? " · 使用率 " + Percent(pick) : "");
        return Row(
            advice.Next is int next ? "下一件：" + itemName(next) : "推荐组合已全部完成",
            "按与已有装备重合最多的推荐组合对照；已拥有的装备显示为暗色。",
            icons,
            $"参照组合 {rate} · 已有 {advice.Owned}/{build.Ids.Length}"
        );
    }
}
