using League.Models;
using League.Tests.Support;

namespace League.Tests.Models;

public class SpellScalingTests
{
    [Fact]
    public void Value_RankPastTheList_UsesTheLastValue()
    {
        var spell = new SpellBuilder().WithValue("Damage", 0, 10, 20).Build();

        var value = spell.Value("Damage", 5, "");

        Assert.Equal(20, value);
    }

    [Fact]
    public void Value_NameInOtherCase_StillMatches()
    {
        var spell = new SpellBuilder().WithValue("StunDuration", 0, 1.5).Build();

        var value = spell.Value("Stunduration", 1, "");

        Assert.Equal(1.5, value);
    }

    [Fact]
    public void Value_ModeWithOverride_UsesTheModeValue()
    {
        var spell = new SpellBuilder().WithValue("Slow", 0, 0.4).WithModeValue("aram", "Slow", 0, 0.2).Build();

        var value = spell.Value("Slow", 1, "ARAM");

        Assert.Equal(0.2, value);
    }

    [Fact]
    public void Value_UnknownName_ReturnsNull()
    {
        var spell = new SpellBuilder().Build();

        var value = spell.Value("Missing", 1, "");

        Assert.Null(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Effect_IndexOutsideTheList_ReturnsNull(int index)
    {
        var spell = new SpellBuilder().WithEffect(0, 10).Build();

        var value = spell.Effect(index, 1);

        Assert.Null(value);
    }
}
