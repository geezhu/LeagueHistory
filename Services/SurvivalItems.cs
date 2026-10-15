using League.Models;

namespace League.Services;

/// <summary>An item that keeps its owner alive: stasis, revive, cleanse, spell shield or a low-health shield.</summary>
public sealed record SurvivalItem(int Id, string Name, string Effect, string Rule);

/// <summary>
/// Survival items in a player's inventory, as the Tab scoreboard shows them. 2999 reports no item cooldowns, so an item
/// already used this fight is still listed.
/// </summary>
public static class SurvivalItems
{
    // Arena and other modes reuse these items under prefixed IDs (223157, 773157); the last four digits identify them.
    private static readonly Dictionary<int, (string Effect, string Rule)> Items = new()
    {
        [3157] = ("凝滞", "主动：2.5 秒内免疫伤害且不可被选取"),
        [2420] = ("凝滞", "单次使用：进入 2.5 秒凝滞"),
        [3026] = ("复活", "受到致命伤害时凝滞 4 秒后原地复活"),
        [3139] = ("解控", "主动：移除控制效果"),
        [3140] = ("解控", "主动：移除控制效果"),
        [3102] = ("法术护盾", "格挡下一个敌方技能"),
        [3814] = ("法术护盾", "格挡下一个敌方技能"),
        [4632] = ("法术护盾", "格挡下一个敌方技能"),
        [3053] = ("护盾", "生命值将低于 30% 时获得护盾"),
        [6673] = ("护盾", "生命值将低于 30% 时获得护盾"),
        [3156] = ("魔法护盾", "受魔法伤害将低于 30% 生命值时获得魔法护盾"),
        [3155] = ("魔法护盾", "受魔法伤害将低于 30% 生命值时获得魔法护盾"),
        [3193] = ("护盾", "主动：获得衰减护盾"),
    };

    public static SurvivalItem[] Find(IEnumerable<LiveMatchItem>? items) =>
        (items ?? [])
            .Select(item => (Item: item, Known: Items.TryGetValue(BaseId(item.Id), out var known) ? known : default))
            .Where(entry => entry.Known.Effect != null)
            .Select(entry => new SurvivalItem(entry.Item.Id, entry.Item.Name, entry.Known.Effect, entry.Known.Rule))
            .DistinctBy(item => BaseId(item.Id))
            .ToArray();

    private static int BaseId(int id) => id >= 100000 ? id % 10000 : id;
}
