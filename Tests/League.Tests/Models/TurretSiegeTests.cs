using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The active player against the next enemy turret, as the Game Bar widget reads it. The expected values come from
/// the checks in Tests/StatisticsChecks/TurretChecks.cs.
/// </summary>
public class TurretSiegeTests
{
    private static TurretSiege Siege() =>
        new("敌方二塔", 97.6, false, 40, 23.4, 199, 117.7, [262.4, 392.5, 524.4, 655.6], 3) { Fortification = 0.3 };

    [Fact]
    public void ToJson_Siege_CarriesEveryField()
    {
        var json = Siege().ToJson();

        Assert.Equal("敌方二塔", (string?)json["target"]);
        Assert.Equal(98.0, (double?)json["hit"]);
        Assert.False((bool?)json["magic"]);
        Assert.Equal(40, (int?)json["hits"]);
        Assert.Equal(23.4, (double?)json["seconds"]);
        Assert.Equal(199, (int?)json["hitsAlone"]);
        Assert.Equal(117.7, (double?)json["secondsAlone"]);
        Assert.Equal([262.0, 392.0, 524.0, 656.0], json["shots"]!.Values<double>());
        Assert.Equal(3, (int?)json["shotsToDown"]);
        Assert.Equal(0.3, (double?)json["fortification"]);
    }

    [Theory]
    [InlineData(23.44, 23.4)]
    [InlineData(23.46, 23.5)]
    public void ToJson_Seconds_RoundsToOneDecimal(double seconds, double expected)
    {
        var siege = Siege() with { Seconds = seconds };

        var json = siege.ToJson();

        Assert.Equal(expected, (double?)json["seconds"]);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.304, 0.3)]
    [InlineData(0.306, 0.31)]
    public void ToJson_Fortification_RoundsToTwoDecimals(double fortification, double expected)
    {
        var siege = Siege() with { Fortification = fortification };

        JToken? written = siege.ToJson()["fortification"];

        Assert.Equal(expected, (double?)written);
    }
}
