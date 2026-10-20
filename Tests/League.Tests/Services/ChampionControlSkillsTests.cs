using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// Recognising hard crowd control in the client's champion data, which has no structured control field: the Chinese
/// descriptions name the effects in prose, so the rules are keywords read clause by clause.
///
/// Four things decide whether a clause counts. A clause about removing or resisting control applies none itself; a
/// passive's trigger clause ("……时", "被……") names when it happens, not what it does; "变形" only counts when the
/// sentence polymorphs an enemy rather than transforming the champion; and "拉拽" only counts when the champion is not
/// pulling itself.
/// </summary>
public class ChampionControlSkillsTests
{
    // ---- each effect is recognised by its own words ----

    [Theory]
    [InlineData("将敌人击飞", "击飞")]
    [InlineData("将其浮空", "击飞")]
    [InlineData("将目标抛向空中", "击飞")]
    [InlineData("将敌人击退", "击退")]
    [InlineData("将目标推挤开", "击退")]
    [InlineData("压制目标", "压制")]
    [InlineData("使敌人眩晕", "晕眩")]
    [InlineData("将其击晕", "晕眩")]
    [InlineData("魅惑目标", "魅惑")]
    [InlineData("迫使敌人嘲讽自己", "嘲讽")]
    [InlineData("使敌人恐惧", "恐惧")]
    [InlineData("使目标恐吓逃跑", "恐惧")]
    [InlineData("使敌人沉睡", "沉睡")]
    [InlineData("将其禁锢", "禁锢")]
    [InlineData("使目标无法移动", "禁锢")]
    [InlineData("使敌人沉默", "沉默")]
    public void Effects_AClauseNamingAnEffect_RecognisesIt(string text, string expected)
    {
        Assert.Equal([expected], ChampionControlSkills.Effects(text, passive: false));
    }

    [Theory]
    [InlineData("减速目标")]
    [InlineData("致盲目标")]
    [InlineData("造成伤害")]
    [InlineData("")]
    public void Effects_ASlowOrBlindOrNothingAtAll_IsNotHardControl(string text)
    {
        // Slows and blinds are deliberately left out; the widget calls what it finds "recognized", not complete.
        Assert.Empty(ChampionControlSkills.Effects(text, passive: false));
    }

    // ---- the order and the duplicates ----

    [Fact]
    public void Effects_SeveralEffects_AreReturnedInTheRuleOrder()
    {
        // The order is the table's, not the sentence's, so the same text always reads the same.
        Assert.Equal(["击飞", "沉默"], ChampionControlSkills.Effects("将敌人击飞并使其沉默", passive: false));
    }

    [Fact]
    public void Effects_TheSameEffectTwice_IsReturnedOnce()
    {
        Assert.Equal(["晕眩"], ChampionControlSkills.Effects("使敌人眩晕", passive: false));
        Assert.Equal(["晕眩"], ChampionControlSkills.Effects("使敌人眩晕，再次眩晕", passive: false));
    }

    [Fact]
    public void Effects_EffectsInSeparateClauses_AllCount()
    {
        Assert.Equal(["晕眩", "禁锢"], ChampionControlSkills.Effects("使敌人眩晕，并将其禁锢", passive: false));
    }

    // ---- clauses that apply nothing ----

    [Theory]
    [InlineData("免疫眩晕")]
    [InlineData("移除眩晕")]
    [InlineData("解除击飞")]
    [InlineData("不受击退影响")]
    [InlineData("无视嘲讽")]
    [InlineData("减少禁锢时间")]
    [InlineData("缩短沉默")]
    [InlineData("抵抗恐惧")]
    [InlineData("友军的眩晕")]
    [InlineData("降低冷却")]
    public void Effects_AClauseAboutRemovingOrResistingControl_AppliesNone(string text)
    {
        // "Immune to", "removes", "shortens" and "an ally's" all describe control without applying it.
        Assert.Empty(ChampionControlSkills.Effects(text, passive: false));
    }

    [Fact]
    public void Effects_ANegativeClauseDoesNotSpoilTheRest()
    {
        // The exclusion is per clause, so a sentence can both mention immunity and do something.
        Assert.Equal(["晕眩"], ChampionControlSkills.Effects("免疫减速，并使敌人眩晕", passive: false));
    }

    // ---- a passive's trigger clause ----

    [Fact]
    public void Effects_APassivesTriggerClause_IsNotAnEffectItApplies()
    {
        // "When it is stunned" says when the passive fires, not that it stuns.
        Assert.Empty(ChampionControlSkills.Effects("被敌人眩晕时获得护盾", passive: true));
    }

    [Fact]
    public void Effects_TheSameWordsOnAnAbility_DoCount()
    {
        // Only a passive reads its "……时" clause as a trigger; an ability describing one does apply it.
        Assert.Equal(["晕眩"], ChampionControlSkills.Effects("被敌人眩晕时获得护盾", passive: false));
    }

    [Fact]
    public void Effects_APassivesOwnEffectClause_StillCounts()
    {
        Assert.Equal(["击飞"], ChampionControlSkills.Effects("将敌人击飞", passive: true));
    }

    // ---- the two exceptions ----

    [Fact]
    public void Effects_PolymorphingAnEnemy_Counts()
    {
        Assert.Equal(["变形"], ChampionControlSkills.Effects("将其变形", passive: false));
        Assert.Equal(["变形"], ChampionControlSkills.Effects("使一名敌人变形", passive: false));
    }

    [Theory]
    [InlineData("变形成魔龙")]
    [InlineData("将他的锤子变形")]
    [InlineData("自己变形")]
    public void Effects_TransformingSomethingElse_DoesNotCount(string text)
    {
        // Turning into a dragon or reshaping a hammer is not crowd control.
        Assert.Empty(ChampionControlSkills.Effects(text, passive: false));
    }

    [Theory]
    [InlineData("自己拉向目标")]
    [InlineData("自身拽向队友")]
    public void Effects_PullingItself_DoesNotCount(string text)
    {
        // Moving yourself is not pulling an enemy.
        Assert.Empty(ChampionControlSkills.Effects(text, passive: false));
    }

    [Theory]
    [InlineData("拽向目标")]
    [InlineData("将敌人拉到身前")]
    public void Effects_PullingAnEnemy_DoesCount(string text)
    {
        Assert.Equal(["拉拽"], ChampionControlSkills.Effects(text, passive: false));
    }

    // ---- the summary ----

    [Fact]
    public void Summary_MarkupAndPlaceholders_AreRemoved()
    {
        // The summary is the description without the client's markup, for a tooltip.
        JObject spell = new() { ["description"] = "造成<scaleAP>魔法伤害</scaleAP>，持续@Duration@秒" };

        Assert.Equal("造成魔法伤害，持续秒", ChampionControlSkills.Summary(spell));
    }

    [Fact]
    public void Summary_NoSpellAtAll_IsEmpty()
    {
        Assert.Equal("", ChampionControlSkills.Summary(null));
    }

    [Fact]
    public void Summary_ASpellWithoutADescription_IsEmpty()
    {
        Assert.Equal("", ChampionControlSkills.Summary(new JObject()));
    }

    // ---- reading a champion ----

    private static JObject Champion(params (string Key, string Name, string Description)[] spells) =>
        new()
        {
            ["spells"] = new JArray(
                spells.Select(s => new JObject
                {
                    ["spellKey"] = s.Key,
                    ["name"] = s.Name,
                    ["description"] = s.Description,
                    ["dynamicDescription"] = "",
                })
            ),
        };

    [Fact]
    public void Parse_ASpellThatAppliesControl_IsListedByItsKey()
    {
        ControlSkill skill = Assert.Single(ChampionControlSkills.Parse(Champion(("Q", "秘术射击", "将敌人击飞"))));

        Assert.Equal("Q", skill.Key);
        Assert.Equal("秘术射击", skill.Name);
        Assert.Equal(["击飞"], skill.Effects);
    }

    [Fact]
    public void Parse_ASpellThatAppliesNothing_IsLeftOut()
    {
        Assert.Empty(ChampionControlSkills.Parse(Champion(("Q", "秘术射击", "造成伤害"))));
    }

    [Fact]
    public void Parse_ThePassive_IsListedFirstUnderItsOwnKey()
    {
        // The passive has no spell key, so it is named after what it is.
        JObject champion = Champion(("Q", "秘术射击", "将敌人击飞"));
        champion["passive"] = new JObject { ["name"] = "被动", ["description"] = "使敌人沉默" };

        ControlSkill[] skills = ChampionControlSkills.Parse(champion);

        Assert.Equal(["被动", "Q"], skills.Select(s => s.Key));
    }

    [Fact]
    public void Parse_AnAbilityNameInsideADescription_IsNotAnEffect()
    {
        // "缠绕之根" is the ability's own name; leaving it in would read as the 禁锢 effect.
        var champion = new JObject
        {
            ["spells"] = new JArray(
                new JObject
                {
                    ["spellKey"] = "E",
                    ["name"] = "缠绕之根",
                    ["description"] = "施放缠绕之根，造成伤害",
                    ["dynamicDescription"] = "",
                }
            ),
        };

        Assert.Empty(ChampionControlSkills.Parse(champion));
    }

    [Fact]
    public void Parse_NoSpellsAtAll_IsEmpty()
    {
        Assert.Empty(ChampionControlSkills.Parse(new JObject()));
    }
}
