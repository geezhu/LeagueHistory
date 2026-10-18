using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The advice record's two outward shapes: the JSON the Game Bar widget reads, and the signed tooltip lines. The
/// expected values come from the checks in Tests/StatisticsChecks/DeathTimingChecks.cs.
/// </summary>
public class DeathTimingAdviceTests
{
    private static readonly DeathTimingFactor[] Factors =
    [
        new(3, "对面 2 人阵亡（最长还剩 24 秒）"),
        new(2, "身上 2650 金，够买大件"),
        new(-1, "你是我方主要前排"),
        new(0, "阵容数据未加载，只按兵线、复活时间和双方阵亡判断"),
    ];

    [Theory]
    [InlineData(DeathTimingVerdict.Good, "good")]
    [InlineData(DeathTimingVerdict.Okay, "okay")]
    [InlineData(DeathTimingVerdict.Avoid, "avoid")]
    public void ToJson_Verdict_ReturnsTheWidgetCode(DeathTimingVerdict verdict, string expected)
    {
        var advice = new DeathTimingAdvice(verdict, [], Factors);

        var json = advice.ToJson();

        Assert.Equal(expected, (string?)json["verdict"]);
    }

    [Fact]
    public void ToJson_GoodAdvice_CarriesReasonsAndSignedDetails()
    {
        var advice = new DeathTimingAdvice(DeathTimingVerdict.Good, [Factors[0].Text, Factors[1].Text], Factors);

        var json = advice.ToJson();

        Assert.Equal(2, json["reasons"]!.Count());
        Assert.Equal(
            [
                "＋ 对面 2 人阵亡（最长还剩 24 秒）",
                "＋ 身上 2650 金，够买大件",
                "－ 你是我方主要前排",
                "· 阵容数据未加载，只按兵线、复活时间和双方阵亡判断",
            ],
            json["details"]!.Values<string>()
        );
    }

    [Fact]
    public void ToJson_NoReasons_WritesAnEmptyArray()
    {
        var advice = new DeathTimingAdvice(DeathTimingVerdict.Okay, [], []);

        JObject json = advice.ToJson();

        Assert.Empty((JArray)json["reasons"]!);
    }

    [Theory]
    [InlineData(3, "＋ 够买大件")]
    [InlineData(-3, "－ 够买大件")]
    [InlineData(0, "· 够买大件")]
    public void Describe_Weight_ReturnsTheSignAndTheText(int weight, string expected)
    {
        string line = DeathTimingAdvice.Describe(new DeathTimingFactor(weight, "够买大件"));

        Assert.Equal(expected, line);
    }
}
