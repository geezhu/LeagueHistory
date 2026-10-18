using League.Models;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The game data's GameCalculation formulas. The shapes are the ones the extraction writes; Annie Q's is copied from
/// TestData/CommunityDragon/annie.bin.json.
/// </summary>
public class SpellCalculatorTests
{
    private static readonly ChampionStatSheet Stats = new(11, true)
    {
        AbilityPower = 120,
        AttackDamage = new(90, 150),
        Health = new(1500, 2000),
        BonusAttackSpeed = 0.4,
        AbilityHaste = 25,
    };

    [Fact]
    public void Calculate_AnnieQ_AddsBaseDamageAndEightyPercentAbilityPower()
    {
        var spell = new SpellBuilder()
            .WithValue("BaseDamage", 35, 80, 125, 170, 215, 260, 305)
            .WithValue("APRatio", 0.8, 0.8, 0.8, 0.8, 0.8, 0.8, 0.8)
            .WithCalculation("TotalDamage", Calc.Formula(Calc.Value("BaseDamage"), Calc.AbilityPowerBy("APRatio")))
            .Build();

        var number = SpellCalculator.Calculate(SpellBuilder.Context(spell, Stats, rank: 2), "TotalDamage");

        Assert.Equal(125 + 0.8 * 120, number!.Value.Value, 6);
    }

    [Theory]
    [InlineData(2, 0, 150)]
    [InlineData(2, 1, 90)]
    [InlineData(2, 2, 60)]
    [InlineData(12, 2, 500)]
    public void Calculate_StatFormula_UsesTotalBaseOrBonus(int stat, int formula, double expected)
    {
        var spell = new SpellBuilder().WithCalculation("Ratio", Calc.Formula(Calc.Stat(stat, 1, formula))).Build();

        var number = SpellCalculator.Calculate(SpellBuilder.Context(spell, Stats), "Ratio");

        Assert.Equal(expected, number!.Value.Value, 6);
    }

    [Fact]
    public void Calculate_StatTheSheetDoesNotKnow_ReturnsNull()
    {
        var spell = new SpellBuilder().WithCalculation("Odd", Calc.Formula(Calc.Stat(16, 1))).Build();

        var number = SpellCalculator.Calculate(SpellBuilder.Context(spell, Stats), "Odd");

        Assert.Null(number);
    }

    [Fact]
    public void Calculate_EffectIndexOne_ReadsTheFirstEffectAmount()
    {
        var spell = new SpellBuilder()
            .WithEffect(0, 10, 25, 40, 55, 70)
            .WithCalculation(
                "QDamage",
                Calc.Formula(Calc.Part("EffectValueCalculationPart", new JObject { ["mEffectIndex"] = 1 }))
            )
            .Build();

        var number = SpellCalculator.Calculate(SpellBuilder.Context(spell, Stats, rank: 3), "QDamage");

        Assert.Equal(40, number!.Value.Value);
    }

    [Fact]
    public void Calculate_LevelBreakpoints_AddEachBonusReachedSoFar()
    {
        var breakpoints = Calc.Part(
            "ByCharLevelBreakpointsCalculationPart",
            new JObject
            {
                ["mLevel1Value"] = 0.55,
                ["mBreakpoints"] = new JArray(
                    new JObject { ["mLevel"] = 6, ["mAdditionalBonusAtThisLevel"] = 0.05 },
                    new JObject { ["mLevel"] = 11, ["mAdditionalBonusAtThisLevel"] = 0.05 },
                    new JObject { ["mLevel"] = 16, ["mAdditionalBonusAtThisLevel"] = 0.05 }
                ),
            }
        );
        var spell = new SpellBuilder().WithCalculation("Ratio", Calc.Formula(breakpoints)).Build();

        var number = SpellCalculator.Calculate(SpellBuilder.Context(spell, Stats, level: 11), "Ratio");

        Assert.Equal(0.65, number!.Value.Value, 6);
    }

    [Fact]
    public void Calculate_PerLevelBreakpoint_ChangesTheRateFromItsLevelOn()
    {
        var breakpoints = Calc.Part(
            "ByCharLevelBreakpointsCalculationPart",
            new JObject
            {
                ["mLevel1Value"] = 10,
                ["mInitialBonusPerLevel"] = 1,
                ["mBreakpoints"] = new JArray(new JObject { ["mLevel"] = 4, ["mBonusPerLevelAtAndAfter"] = 3 }),
            }
        );
        var spell = new SpellBuilder().WithCalculation("Value", Calc.Formula(breakpoints)).Build();

        var number = SpellCalculator.Calculate(SpellBuilder.Context(spell, Stats, level: 5), "Value");

        Assert.Equal(10 + 1 + 1 + 3 + 3, number!.Value.Value);
    }

    [Theory]
    [InlineData(1, 22)]
    [InlineData(18, 10)]
    public void Calculate_LevelInterpolation_RunsFromStartToEnd(int level, double expected)
    {
        var line = Calc.Part(
            "ByCharLevelInterpolationCalculationPart",
            new JObject { ["mStartValue"] = 22, ["mEndValue"] = 10 }
        );
        var spell = new SpellBuilder().WithCalculation("Cooldown", Calc.Formula(line)).Build();

        var number = SpellCalculator.Calculate(SpellBuilder.Context(spell, Stats, level: level), "Cooldown");

        Assert.Equal(expected, number!.Value.Value, 6);
    }

    [Fact]
    public void Calculate_ModifiedCalculation_MultipliesTheNamedOne()
    {
        var spell = new SpellBuilder()
            .WithValue("RepeatDamageMod", 0.4, 0.4)
            .WithCalculation("SingleFireDamage", Calc.Formula(Calc.Number(100)))
            .WithCalculation(
                "RepeatDamage",
                new JObject
                {
                    ["__type"] = "GameCalculationModified",
                    ["mModifiedGameCalculation"] = "SingleFireDamage",
                    ["mMultiplier"] = Calc.Value("RepeatDamageMod"),
                }
            )
            .Build();

        var number = SpellCalculator.Calculate(SpellBuilder.Context(spell, Stats), "RepeatDamage");

        Assert.Equal(40, number!.Value.Value, 6);
    }

    [Fact]
    public void Calculate_Conditional_ShowsTheDefaultCase()
    {
        var spell = new SpellBuilder()
            .WithCalculation("Usual", Calc.Formula(Calc.Number(7)))
            .WithCalculation(
                "Damage",
                new JObject
                {
                    ["__type"] = "GameCalculationConditional",
                    ["mDefaultGameCalculation"] = "Usual",
                    ["mConditionalGameCalculation"] = "{60195651}",
                }
            )
            .Build();

        var number = SpellCalculator.Calculate(SpellBuilder.Context(spell, Stats), "Damage");

        Assert.Equal(7, number!.Value.Value);
    }

    [Fact]
    public void Calculate_BuffCounter_CountsNoStacksAndMarksThePartial()
    {
        var counter = Calc.Part(
            "BuffCounterByCoefficientCalculationPart",
            new JObject { ["mBuffName"] = "{7f3c01cf}", ["mCoefficient"] = 1 }
        );
        var spell = new SpellBuilder().WithCalculation("Stacked", Calc.Formula(Calc.Number(5), counter)).Build();

        var number = SpellCalculator.Calculate(SpellBuilder.Context(spell, Stats), "Stacked");

        Assert.Equal(new SpellNumber(5, Partial: true), number);
    }

    [Fact]
    public void Calculate_UnnamedPartType_ReturnsNull()
    {
        var spell = new SpellBuilder()
            .WithCalculation("Elapsed", Calc.Formula(Calc.Part("PercentageOfBuffNameElapsed", new JObject())))
            .Build();

        var number = SpellCalculator.Calculate(SpellBuilder.Context(spell, Stats), "Elapsed");

        Assert.Null(number);
    }

    [Fact]
    public void Calculate_DisplayAsPercentAndPrecision_AreKept()
    {
        var calculation = Calc.Formula(Calc.Number(0.25));
        calculation["mDisplayAsPercent"] = true;
        calculation["mPrecision"] = 1;
        var spell = new SpellBuilder().WithCalculation("Share", calculation).Build();

        var number = SpellCalculator.Calculate(SpellBuilder.Context(spell, Stats), "Share");

        Assert.Equal(new SpellNumber(0.25, Percent: true, Precision: 1), number);
    }

    [Fact]
    public void Calculate_ModeOverride_ReplacesTheValueInThatMode()
    {
        var spell = new SpellBuilder()
            .WithValue("BaseDamage", 0, 100)
            .WithModeValue("aram", "BaseDamage", 0, 80)
            .WithCalculation("TotalDamage", Calc.Formula(Calc.Value("BaseDamage")))
            .Build();

        var number = SpellCalculator.Calculate(SpellBuilder.Context(spell, Stats, mode: "aram"), "TotalDamage");

        Assert.Equal(80, number!.Value.Value);
    }

    [Fact]
    public void Calculate_CooldownMultiplier_IsTheHasteShare()
    {
        var spell = new SpellBuilder()
            .WithCalculation("Scaled", Calc.Formula(Calc.Part("CooldownMultiplierCalculationPart", new JObject())))
            .Build();

        var number = SpellCalculator.Calculate(SpellBuilder.Context(spell, Stats), "Scaled");

        Assert.Equal(100.0 / 125, number!.Value.Value, 6);
    }

    [Fact]
    public void Calculate_SelfReference_StopsInsteadOfRecursingForever()
    {
        var spell = new SpellBuilder()
            .WithCalculation(
                "Loop",
                new JObject { ["__type"] = "GameCalculationModified", ["mModifiedGameCalculation"] = "Loop" }
            )
            .Build();

        var number = SpellCalculator.Calculate(SpellBuilder.Context(spell, Stats), "Loop");

        Assert.Null(number);
    }
}
