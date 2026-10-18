using League.Models;
using League.Services;
using League.Tests.Support;

namespace League.Tests.Services;

/// <summary>String table tooltips with their tokens filled in, in the forms the game's tooltips use.</summary>
public class SpellTooltipTests
{
    private static readonly SpellScaling AnnieQ = new SpellBuilder("annieq")
        .WithValue("BaseDamage", 0, 80, 125, 170, 215, 260)
        .WithValue("SlowAmount", -0.3, -0.3)
        .WithCalculation("TotalDamage", Calc.Formula(Calc.Value("BaseDamage")))
        .Build();

    [Fact]
    public void Render_CalculationToken_GivesOneValuePerRank()
    {
        var segments = SpellTooltip.Render(
            "造成<magicDamage>@TotalDamage@魔法伤害</magicDamage>。",
            SpellBuilder.Context(AnnieQ),
            5
        );

        Assert.Equal(["80", "125", "170", "215", "260"], segments[1].Values);
    }

    [Fact]
    public void Render_StyleTag_MarksTheTextAndNumbersInside()
    {
        var segments = SpellTooltip.Render(
            "造成<magicDamage>@TotalDamage@魔法伤害</magicDamage>。",
            SpellBuilder.Context(AnnieQ),
            5
        );

        Assert.Equal(["", "magic", "magic", ""], segments.Select(s => s.Style));
    }

    [Fact]
    public void Render_SameValueAtEveryRank_KeepsOneValue()
    {
        var segments = SpellTooltip.Render("减速@SlowAmount*-100@%", SpellBuilder.Context(AnnieQ), 5);

        Assert.Equal(["30"], segments[1].Values);
    }

    [Fact]
    public void Render_LineBreakAndIcon_BecomeNewlineAndNothing()
    {
        var segments = SpellTooltip.Render("第一行<br>%i:cooldown%第二行", SpellBuilder.Context(AnnieQ), 5);

        Assert.Equal("第一行\n第二行", Assert.Single(segments).Text);
    }

    [Fact]
    public void Render_OtherSpellToken_ReadsThatSpell()
    {
        var passive = new SpellBuilder("anniepassive").WithValue("StunDuration", 1.75, 1.75).Build();

        var segments = SpellTooltip.Render(
            "@spell.AnniePassive:StunDuration@",
            SpellBuilder.Context(AnnieQ, others: passive),
            1
        );

        Assert.Equal(["1.75"], Assert.Single(segments).Values);
    }

    [Fact]
    public void Render_ClientVariable_IsUnknown()
    {
        var segments = SpellTooltip.Render("当前：@f1@", SpellBuilder.Context(AnnieQ), 5);

        Assert.True(segments[1].Unknown);
    }

    [Fact]
    public void Render_ValueOfOnlyStacks_ReadsAsByStacks()
    {
        var counter = Calc.Part(
            "BuffCounterByNamedDataValueCalculationPart",
            new() { ["mBuffName"] = "{f6907f1b}", ["mDataValue"] = "PerStack" }
        );
        var spell = new SpellBuilder().WithCalculation("Bonus", Calc.Formula(counter)).Build();

        var segments = SpellTooltip.Render("获得@Bonus@护甲", SpellBuilder.Context(spell), 5);

        Assert.Equal(("按层数", true), (segments[1].Text, segments[1].Unknown));
    }

    [Fact]
    public void Render_SilentToken_IsDropped()
    {
        var segments = SpellTooltip.Render("说明。@SpellModifierDescriptionAppend@", SpellBuilder.Context(AnnieQ), 5);

        Assert.Equal("说明。", Assert.Single(segments).Text);
    }

    [Theory]
    [InlineData(176.4, false, null, null, 1, "176")]
    [InlineData(1.754, false, null, null, 1, "1.75")]
    [InlineData(0.256, true, null, null, 1, "25.6%")]
    [InlineData(0.256, true, 0, null, 1, "26%")]
    [InlineData(0.3, false, null, 1, 100, "30")]
    [InlineData(-0.3, false, null, null, -100, "30")]
    public void Format_Number_FollowsPercentScaleAndPrecision(
        double value,
        bool percent,
        int? calculationPrecision,
        int? tokenPrecision,
        double scale,
        string expected
    )
    {
        var text = SpellTooltip.Format(new SpellNumber(value, percent, calculationPrecision), scale, tokenPrecision);

        Assert.Equal(expected, text);
    }
}
