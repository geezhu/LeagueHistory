using League.Models;

namespace League.Tests.Models;

/// <summary>
/// The team chat lines the wave page sends and the window in which the forecast still counts as fresh. The expected
/// lines come from the chat checks in Tests/StatisticsChecks/MinionWaveChecks.cs; the forecast is built by hand, so
/// the age of the data is a parameter and no clock is read.
/// </summary>
public class MinionWaveChatMessageTests
{
    /// <summary>The wall clock of the fixture forecast; every age in these tests is a delta from it.</summary>
    private const long SendClock = 1_700_000_000_000;

    /// <summary>The refusal the widget shows verbatim, so it is pinned word for word.</summary>
    private const string StaleMessage = "未发送：兵线数据未更新，请等待 2999 连接恢复。";

    private static MinionWaveForecast Forecast() =>
        new(
            "召唤师峡谷",
            60,
            SendClock,
            [new WaveSpawn(1, 70, 3, 3, 0), new WaveSpawn(2, 130, 3, 3, 1)],
            ["每 30 秒一波 · 每 3 波 1 个炮车"],
            "普通波 300 金 / 600 经验",
            [
                new LaneWaveForecast(
                    "下路",
                    new WaveArrival("敌方一塔", 75, 79),
                    new WaveArrival("我方一塔", 75, 79),
                    "敌方小水晶被破 · 我方每波 +1 超级兵"
                ),
                new LaneWaveForecast(
                    "上路",
                    new WaveArrival("敌方一塔", 75, 79),
                    new WaveArrival("我方一塔", 75, 79),
                    ""
                ),
            ]
        )
        {
            Deaths =
            [
                new DeathCost(false, "样例英雄 10", 13, 42.4, false, "enemy"),
                new DeathCost(true, "我", 11, 30, true, "self"),
            ],
            DeathFactor = 4.2,
        };

    [Fact]
    public void Fixture_CoversBothSidesOfEveryFilter()
    {
        // Fixture self-check on the rule, not the shape: the death lines split on DeathCost.Ally, the super lines keep
        // only lanes that carry Structures, and the spawn line counts down to a wave still ahead of the game time.
        // Without this a later edit could cut the fixture down to one side and the expected lines would keep passing
        // while pinning far less than they claim to.
        var forecast = Forecast();

        Assert.Contains(forecast.Deaths, death => death.Ally);
        Assert.Contains(forecast.Deaths, death => !death.Ally);
        Assert.Contains(forecast.Lanes, lane => lane.Structures.Length > 0);
        Assert.Contains(forecast.Spawns, wave => wave.Time > forecast.GameTime);
    }

    [Fact]
    public void Constants_ProtocolTargets_MatchTheWidgetStrings()
    {
        Assert.Equal("spawn", MinionWaveChatMessage.Spawn);
        Assert.Equal("super", MinionWaveChatMessage.Super);
        Assert.Equal("deaths", MinionWaveChatMessage.Deaths);
    }

    [Theory]
    [InlineData("spawn")]
    [InlineData("super")]
    [InlineData("deaths")]
    public void IsTarget_WaveTargets_ReturnsTrue(string target)
    {
        Assert.True(MinionWaveChatMessage.IsTarget(target), $"「{target}」是兵线内容");
    }

    [Theory]
    [InlineData("own")]
    [InlineData("")]
    public void IsTarget_OtherTargets_ReturnsFalse(string target)
    {
        Assert.False(MinionWaveChatMessage.IsTarget(target), $"「{target}」不是兵线内容");
    }

    [Fact]
    public void IsTarget_UppercaseSpawn_ReturnsFalse()
    {
        // The pattern match is case-sensitive, so only the exact target strings are accepted.
        Assert.False(MinionWaveChatMessage.IsTarget("SPAWN"), "SPAWN 不是兵线内容");
    }

    // Age zero: the fixture is fresh at its own clock, which is the moment the forecast was measured.
    [Theory]
    [InlineData("spawn", "兵线：0:10 后出第 1 波；下个炮车波 2:10；每 30 秒一波 · 每 3 波 1 个炮车")]
    [InlineData("super", "超级兵：下路 敌方小水晶被破 · 我方每波 +1 超级兵")]
    public void Build_SingleLineTarget_ReturnsOneExactLine(string target, string expected)
    {
        var forecast = Forecast();

        var lines = MinionWaveChatMessage.Build(forecast, target, forecast.Clock);

        Assert.Equal(expected, Assert.Single(lines));
    }

    [Fact]
    public void Build_Deaths_ReturnsTheEnemyLineThenTheAllyLine()
    {
        var forecast = Forecast();

        var lines = MinionWaveChatMessage.Build(forecast, "deaths", forecast.Clock);

        Assert.Equal(2, lines.Count);
        Assert.Equal("现在阵亡需等待（时间加成 +4%） 敌方：样例英雄 10 13级42秒", lines[0]);
        Assert.Equal("我方：我 11级30秒", lines[1]);
    }

    // The line is built at send time, so the countdown shrinks with the age of the data; at fourteen seconds wave 1 has
    // spawned and wave 2 is next, which is the cannon wave itself and takes the "（炮车波）" marker instead of the
    // "下个炮车波" suffix.
    [Theory]
    [InlineData(1_000, "兵线：0:09 后出第 1 波；下个炮车波 2:10；每 30 秒一波 · 每 3 波 1 个炮车")]
    [InlineData(14_000, "兵线：0:56 后出第 2 波（炮车波）；每 30 秒一波 · 每 3 波 1 个炮车")]
    public void Build_LaterSendTime_CountsDownFromTheSendMoment(long age, string expected)
    {
        // The precondition the class demands of fresh data: at most fifteen seconds old. A row past it would fail with
        // the refusal exception, which looks exactly like a bug in Build instead of a broken fixture.
        Assert.True(age <= 15_000, $"age {age} 超出 15000 毫秒的新鲜度前提");

        var forecast = Forecast();

        var lines = MinionWaveChatMessage.Build(forecast, "spawn", forecast.Clock + age);

        Assert.Equal(expected, Assert.Single(lines));
    }

    // The near side of the fifteen second window: zero age, just under the edge and exactly at it. At the edge the
    // schedule has already moved on to wave 2, so these rows pin the window itself and not just "does not throw".
    [Theory]
    [InlineData(0, "兵线：0:10 后出第 1 波；下个炮车波 2:10；每 30 秒一波 · 每 3 波 1 个炮车")]
    [InlineData(14_999, "兵线：0:55 后出第 2 波（炮车波）；每 30 秒一波 · 每 3 波 1 个炮车")]
    [InlineData(15_000, "兵线：0:55 后出第 2 波（炮车波）；每 30 秒一波 · 每 3 波 1 个炮车")]
    public void Build_WithinTheStaleWindow_StillBuildsTheLine(long age, string expected)
    {
        // Same precondition as above: these rows expect a line, so the data has to be inside the window.
        Assert.True(age <= 15_000, $"age {age} 超出 15000 毫秒的新鲜度前提");

        var forecast = Forecast();

        var lines = MinionWaveChatMessage.Build(forecast, "spawn", forecast.Clock + age);

        Assert.Equal(expected, Assert.Single(lines));
    }

    // The far side of the same window: one millisecond past the edge, and a minute past it.
    [Theory]
    [InlineData(15_001)]
    [InlineData(60_000)]
    public void Build_PastTheStaleWindow_IsRefused(long age)
    {
        var forecast = Forecast();

        var error = Assert.Throws<InvalidOperationException>(() =>
            MinionWaveChatMessage.Build(forecast, "spawn", forecast.Clock + age)
        );

        Assert.Equal(StaleMessage, error.Message);
    }

    [Fact]
    public void Build_ClockAheadOfNow_IsRefusedWithTheSameMessage()
    {
        // A clock that runs backwards is a different condition from staleness, and it is pinned on its own.
        var forecast = Forecast();

        var error = Assert.Throws<InvalidOperationException>(() =>
            MinionWaveChatMessage.Build(forecast, "spawn", forecast.Clock - 1)
        );

        Assert.Equal(StaleMessage, error.Message);
    }

    [Fact]
    public void Build_WithoutAForecast_IsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            MinionWaveChatMessage.Build(null, "spawn", SendClock)
        );

        Assert.Equal(StaleMessage, error.Message);
    }

    [Theory]
    [InlineData("own")]
    [InlineData("")]
    [InlineData("SPAWN")]
    public void Build_TargetThatIsNotAWaveContent_IsRefused(string target)
    {
        var forecast = Forecast();

        var error = Assert.Throws<InvalidOperationException>(() =>
            MinionWaveChatMessage.Build(forecast, target, forecast.Clock)
        );

        Assert.Equal("未发送：未知的兵线内容。", error.Message);
    }

    [Fact]
    public void Build_SuperWithoutAFallenInhibitor_IsRefused()
    {
        // Only the lane without structures survives the copy, so there is nothing to name.
        var forecast = Forecast() with
        {
            Lanes = [Forecast().Lanes[1]],
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            MinionWaveChatMessage.Build(forecast, "super", forecast.Clock)
        );

        Assert.Equal("未发送：目前没有被破的小水晶。", error.Message);
    }

    [Fact]
    public void Build_DeathsWithoutDeathTimes_IsRefused()
    {
        var forecast = Forecast() with { Deaths = [] };

        var error = Assert.Throws<InvalidOperationException>(() =>
            MinionWaveChatMessage.Build(forecast, "deaths", forecast.Clock)
        );

        Assert.Equal("未发送：当前模式没有复活时间数据。", error.Message);
    }

    [Fact]
    public void Build_SuperOnASingleLane_LeavesTheLaneNameOut()
    {
        // The lane name is only there to tell several lanes apart, so a single-lane map drops it.
        var forecast = Forecast() with
        {
            Lanes = [Forecast().Lanes[0]],
        };

        var lines = MinionWaveChatMessage.Build(forecast, "super", forecast.Clock);

        Assert.Equal("超级兵：敌方小水晶被破 · 我方每波 +1 超级兵", Assert.Single(lines));
    }

    [Theory]
    [InlineData(0.4, "现在阵亡需等待 敌方：样例英雄 10 13级42秒")]
    [InlineData(0.6, "现在阵亡需等待（时间加成 +1%） 敌方：样例英雄 10 13级42秒")]
    public void Build_DeathsTimeFactor_ShowsTheBonusFromHalfAPercent(double factor, string expected)
    {
        var forecast = Forecast() with { DeathFactor = factor };

        var lines = MinionWaveChatMessage.Build(forecast, "deaths", forecast.Clock);

        Assert.Equal(expected, lines[0]);
    }

    [Fact]
    public void Build_DeathsExactlyAtHalfAPercent_KeepsTheBonus()
    {
        // The threshold is inclusive; the number itself rounds away under one percent, so only the section is pinned.
        var forecast = Forecast() with
        {
            DeathFactor = 0.5,
        };

        var lines = MinionWaveChatMessage.Build(forecast, "deaths", forecast.Clock);

        Assert.StartsWith("现在阵亡需等待（时间加成 +", lines[0]);
    }
}
