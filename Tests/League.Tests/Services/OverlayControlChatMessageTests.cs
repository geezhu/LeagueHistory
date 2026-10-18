using League.Models;
using League.Services;

namespace League.Tests.Services;

/// <summary>One side's control list for game chat; the roster and abilities follow Tests/StatisticsChecks/ControlChecks.cs.</summary>
public class OverlayControlChatMessageTests
{
    private static readonly LoadingRoster Roster = new(
        "1",
        450,
        "ARAM",
        [
            new("self", "自己#1", "", 1, 143, 100, true),
            new("enemy", "对手#3", "", 3, 99, 200, false),
            new("enemy2", "对手#4", "", 4, 7, 200, false),
        ]
    );

    // Q roots; E only slows, ranged and strong enough for the control page.
    private static readonly Dictionary<int, ControlSkill[]> Controls = new()
    {
        [99] = [new("Q", "光之束缚", ["禁锢"], ""), new("E", "透光奇点", [], "") { Slow = 45, Range = 1100 }],
        [7] = [new("E", "减速", [], "") { Slow = 40, Range = 900 }],
        [143] = [new("Q", "缠绕", ["禁锢"], "")],
    };

    private static string Hero(int id) => "英雄" + id;

    private static OverlayChatRequest Request(string kind, string target = "enemy") =>
        new("id", "1", target, -1, 0, 0, false, false, kind);

    [Theory]
    [InlineData("controls", "【控制·对方】英雄99 Q禁锢 E减速；英雄7 E减速")]
    [InlineData("hardControls", "【硬控·对方】英雄99 Q禁锢；英雄7 无硬控")]
    public void Build_Kind_IncludesSlowsOnlyForTheFullList(string kind, string expected)
    {
        var messages = OverlayControlChatMessage.Build(Roster, Request(kind), Controls, null, Hero);

        Assert.Equal(expected, Assert.Single(messages));
    }

    [Fact]
    public void Build_HardControlsForOneHero_IsRefused()
    {
        var build = () =>
            OverlayControlChatMessage.Build(Roster, Request("hardControls", "single"), Controls, null, Hero);

        Assert.Throws<InvalidOperationException>(build);
    }
}
