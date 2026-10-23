using League.Models;

namespace League.Tests.Models;

/// <summary>
/// The behaviour tags of upstream b1d561c. Each case sets one signal at its threshold or just below it and leaves every
/// other signal at zero, so the tag under test is the only one its rule can add.
/// </summary>
public class BehaviorTagsTests
{
    [Theory]
    [InlineData("投降", 35, "爱投降")]
    [InlineData("投降", 22, "容易投降")]
    [InlineData("问号", 2.6, "爱发问号")]
    [InlineData("提醒", 2.2, "爱发提醒")]
    [InlineData("撤退", 3.0, "爱发撤退")]
    [InlineData("危险", 2.2, "爱发危险")]
    public void Summary_OneSignalAtItsThreshold_AddsItsTag(string signal, double value, string tag)
    {
        string summary = BehaviorTags.Summary(Behavior(signal, value));

        Assert.Equal(tag, summary);
    }

    [Theory]
    [InlineData("投降", 21.9)]
    [InlineData("问号", 2.5)]
    [InlineData("提醒", 2.1)]
    [InlineData("撤退", 2.9)]
    [InlineData("危险", 2.1)]
    public void Summary_OneSignalJustBelowItsThreshold_AddsNothing(string signal, double value)
    {
        string summary = BehaviorTags.Summary(Behavior(signal, value));

        Assert.Equal("", summary);
    }

    [Fact]
    public void Summary_LovesToSurrender_LeavesOutTheMilderSurrenderTag()
    {
        string summary = BehaviorTags.Summary(Behavior("投降", 40));

        Assert.Equal("爱投降", summary);
    }

    // Every signal just below its own tag: 2.5 + 2.1 + 2.9 + 2.1 = 9.6, the most the total reaches without one.
    [Fact]
    public void Summary_ATotalOfEightWithoutSingleTags_AddsOnlyTheUnsteadyTag()
    {
        string summary = BehaviorTags.Summary(Pings(2.5, 2.1, 2.9, 2.1));

        Assert.Equal("心态容易崩", summary);
    }

    [Fact]
    public void Summary_ATotalJustBelowEight_AddsNothing()
    {
        string summary = BehaviorTags.Summary(Pings(2, 2, 2, 1.9));

        Assert.Equal("", summary);
    }

    [Theory]
    [InlineData(2.625, "玻璃心")]
    [InlineData(3.125, "压力怪")]
    [InlineData(3.625, "急性子")]
    public void Summary_EqualPingsAddingUpToATotalThreshold_AddsThatTag(double each, string tag)
    {
        string summary = BehaviorTags.Summary(Pings(each, each, each, each));

        Assert.Contains(tag, summary.Split('、'));
    }

    // Upstream made these independent: 「心态容易崩」 no longer gives way to 「玻璃心」.
    [Fact]
    public void Summary_AFragileTotal_KeepsTheUnsteadyTagAlongside()
    {
        string[] tags = BehaviorTags.Summary(Pings(2.625, 2.625, 2.625, 2.625)).Split('、');

        Assert.Contains("心态容易崩", tags);
        Assert.Contains("玻璃心", tags);
    }

    [Fact]
    public void Summary_RetreatsAloneAtTheFragileThreshold_AddsTheFragileTag()
    {
        string summary = BehaviorTags.Summary(Pings(0, 0, 3.8, 0));

        Assert.Equal("爱发撤退、玻璃心", summary);
    }

    [Fact]
    public void Summary_ManyRetreatsAndAHighTotal_AddsTheComplainingTag()
    {
        string summary = BehaviorTags.Summary(Pings(3, 0, 4.2, 3));

        Assert.Contains("爱抱怨", summary.Split('、'));
    }

    [Theory]
    [InlineData(4.2, 0)]
    [InlineData(0, 4.8)]
    public void Summary_OneExtremeSignal_AddsTheImpatientTag(double missing, double getBack)
    {
        string summary = BehaviorTags.Summary(Pings(missing, 0, getBack, 0));

        Assert.Contains("急性子", summary.Split('、'));
    }

    [Fact]
    public void Summary_SeveralTags_JoinsThemInRuleOrderWithoutBrackets()
    {
        var behavior = Pings(2.6, 0, 3.0, 0);
        behavior.SurrenderRate = 35;

        string summary = BehaviorTags.Summary(behavior);

        Assert.Equal("爱投降、爱发问号、爱发撤退", summary);
    }

    [Fact]
    public void Rules_EveryTag_HasADescriptionThatNamesAThreshold()
    {
        Assert.All(BehaviorTags.Rules, rule => Assert.Contains("≥", rule.Description));
    }

    [Theory]
    [InlineData("压力怪", BehaviorTagSeverity.Bad)]
    [InlineData("容易投降", BehaviorTagSeverity.Info)]
    public void Find_AKnownTag_ReturnsItsSeverity(string tag, BehaviorTagSeverity severity)
    {
        BehaviorTagSeverity? found = BehaviorTags.Find(tag)?.Severity;

        Assert.Equal(severity, found);
    }

    [Fact]
    public void Find_AnUnknownTag_ReturnsNull()
    {
        Assert.Null(BehaviorTags.Find("稳健"));
    }

    [Fact]
    public void SignalCounts_OnlyPositiveCounts_AreListedInOrder()
    {
        var behavior = new PlayerBehavior
        {
            TotalMissingPings = 12,
            TotalGetBackPings = 0,
            TotalDangerPings = 3,
        };

        IReadOnlyList<string> counts = behavior.SignalCounts();

        Assert.Equal(["发问号 12次", "发危险 3次"], counts);
    }

    private static PlayerBehavior Behavior(string signal, double value) =>
        signal switch
        {
            "投降" => new PlayerBehavior { SurrenderRate = value },
            "问号" => Pings(value, 0, 0, 0),
            "提醒" => Pings(0, value, 0, 0),
            "撤退" => Pings(0, 0, value, 0),
            "危险" => Pings(0, 0, 0, value),
            _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, "Unknown signal"),
        };

    private static PlayerBehavior Pings(double missing, double vision, double getBack, double danger) =>
        new()
        {
            AvgMissingPings = missing,
            AvgVisionPings = vision,
            AvgGetBackPings = getBack,
            AvgDangerPings = danger,
        };
}
