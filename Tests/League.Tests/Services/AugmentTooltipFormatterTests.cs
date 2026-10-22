using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// Filling an augment tooltip from the game data. The spells are two real ARAM: Mayhem augments from
/// TestData/CommunityDragon/Augments: "将他们放倒倒倒" (fixed values, a formula scaled by stats, one formula that is
/// another times a share, a percentage formula and a rules block of match counters) and "泰坦的坚决" (values by level
/// and plain percentages). An in-match tooltip for the first showed 5 seconds, 5 stacks and 67%, which the fixed parts
/// must reproduce; the stat-scaled parts are written as formulas because the champion's stats are not known here.
/// </summary>
public class AugmentTooltipFormatterTests
{
    private static readonly JObject Mode = JObject.Parse(
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestData", "CommunityDragon", "Augments", "kiwi.bin.json")
        )
    );

    private static JObject Spell(string augmentName) =>
        (JObject)
            Mode[
                (string)
                    Mode.Properties()
                        .Select(p => p.Value)
                        .OfType<JObject>()
                        .Single(a => (string?)a["AugmentNameId"] == augmentName)["RootSpell"]!
            ]!;

    private static string TakeEmDown(string template) =>
        AugmentTooltipFormatter.Render(template, Spell("TakeemDownDownDown"));

    private static string Juggernaut(string template) =>
        AugmentTooltipFormatter.Render(template, Spell("ARAM_ImTheJuggernaut"));

    [Theory]
    [InlineData("@Stack_Duration@", "5")]
    [InlineData("@Stacks_Max@", "5")]
    [InlineData("@Stack_Duration*100@", "500")]
    public void Render_AFixedValue_IsWrittenAsTheNumber(string template, string expected)
    {
        Assert.Equal(expected, TakeEmDown(template));
    }

    [Fact]
    public void Render_AFormulaOfStats_IsWrittenOutWithItsRangedShare()
    {
        Assert.Equal("5–30（随等级） + 35% 额外攻击力 + 15% 法术强度（远程英雄 50%）", TakeEmDown("@Calc_Damage_Hit@"));
    }

    [Fact]
    public void Render_AFormulaThatScalesAnother_MultipliesEveryPart()
    {
        // The per-stack damage is the hit damage times 20%, so each part comes out a fifth: 1–6, 7%, 3%.
        Assert.Equal(
            "1–6（随等级） + 7% 额外攻击力 + 3% 法术强度（远程英雄 50%）",
            TakeEmDown("@Calc_Damage_Per_Previous_Stack@")
        );
    }

    [Fact]
    public void Render_APercentageFormula_WritesItsStatTermsPerPoint()
    {
        Assert.Equal("15% + 5%×额外穿甲 + 0.15%×技能急速（远程英雄 50%）", TakeEmDown("@Calc_Damage_Consume@"));
    }

    [Fact]
    public void Render_APlainPercentage_IsThePercentage()
    {
        Assert.Equal("67%（远程英雄 50%）", TakeEmDown("@Calc_Healing@"));
    }

    [Theory]
    [InlineData("@Calc_AF@", "25–75（随等级）")]
    [InlineData("@Calc_Tenacity@", "8%")]
    [InlineData("@StacksPerHit@", "1")]
    public void Render_ValuesByLevelAndPercentages_AreWrittenOut(string template, string expected)
    {
        Assert.Equal(expected, Juggernaut(template));
    }

    [Fact]
    public void Render_TheMatchCounters_AreDroppedWithTheBreaksBeforeThem()
    {
        Assert.Equal(
            "回复生命值。",
            TakeEmDown("回复生命值。<br><br><rules>触发次数：@f1@<br>已造成的伤害：@f2@</rules>")
        );
    }

    [Fact]
    public void Render_ALineHoldingACounter_IsDropped()
    {
        Assert.Equal("持续5秒", TakeEmDown("回复@f1@生命值<br><br>持续@Stack_Duration@秒"));
    }

    [Fact]
    public void Render_ACounterInBrackets_IsDroppedWithTheBrackets()
    {
        Assert.Equal("获得20% 攻击力", TakeEmDown("获得20% (@f1@) 攻击力"));
    }

    [Fact]
    public void Render_QuestProgress_KeepsTheGoal()
    {
        Assert.Equal("攻击25次", TakeEmDown("攻击@f1@/25次"));
    }

    [Fact]
    public void Render_TheBoundSpell_IsCalledTheChosenOne()
    {
        Assert.Equal("使用【指定】技能", TakeEmDown("使用【{{SpellName}}】技能"));
    }

    [Fact]
    public void Render_AQuotedText_IsResolvedAndFilledFromTheSameSpell()
    {
        string text = AugmentTooltipFormatter.Render(
            "{{ Some_Summary }}",
            Spell("TakeemDownDownDown"),
            name => name == "Some_Summary" ? "持续@Stack_Duration@秒" : null
        );

        Assert.Equal("持续5秒", text);
    }

    [Fact]
    public void Render_AnUnknownQuote_IsDropped()
    {
        Assert.Equal("命中时", TakeEmDown("命中时{{ Item_Keyword_OnHit }}"));
    }

    [Fact]
    public void Render_AnIconMarker_IsDropped()
    {
        Assert.Equal("获得60%攻击速度", TakeEmDown("获得%i:scaleAS%60%攻击速度"));
    }

    [Fact]
    public void Render_AStatNamedTwiceInARow_IsNamedOnce()
    {
        Assert.Equal("获得35% 额外攻击力", TakeEmDown("获得35% 额外攻击力攻击力"));
    }

    [Fact]
    public void Render_ARulesBlockWithoutCounters_IsKept()
    {
        Assert.Equal("<rules>持续5秒</rules>", TakeEmDown("<rules>持续@Stack_Duration@秒</rules>"));
    }

    [Fact]
    public void Render_AnUnknownName_IsADash()
    {
        Assert.Equal("造成—伤害", TakeEmDown("造成@Not_A_Value@伤害"));
    }

    [Fact]
    public void Hash_AFormulaName_IsTheKeySpellDataFilesItUnder()
    {
        Assert.Equal("{c75ec8fe}", AugmentTooltipFormatter.Hash("Calc_Damage_Hit"));
    }
}
