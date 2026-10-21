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

    [Fact]
    public void Build_ASideNobodyCanBeFoundFor_IsRefusedBeforeTheAbilitiesAreConsulted()
    {
        // The roster has no enemies, so there is no side to describe - a different problem from missing abilities.
        LoadingRoster selfOnly = new("1", 450, "ARAM", [new("self", "自己#1", "", 1, 143, 100, true)]);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            OverlayControlChatMessage.Build(selfOnly, Request("controls"), Controls, null, Hero)
        );

        Assert.Equal("未发送：无法确定所选阵营，请等待名单更新。", error.Message);
    }

    [Fact]
    public void Build_ASideWhoseAbilitiesAreNotLoadedYet_IsRefusedWithItsOwnReason()
    {
        // The champions are known but none of them has abilities in hand; the user is told to wait, not that the
        // side is unknown.
        Dictionary<int, ControlSkill[]> onlySelf = new() { [143] = [new("Q", "缠绕", ["禁锢"], "")] };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            OverlayControlChatMessage.Build(Roster, Request("controls"), onlySelf, null, Hero)
        );

        Assert.Equal("未发送：英雄技能还在读取，请稍后重试。", error.Message);
    }

    [Fact]
    public void Build_ASideThatDoesNotFitOneMessage_ContinuesInTheNext()
    {
        // The limit is one chat message; the tail must not be dropped, and the header is repeated so the reader knows
        // which side the continuation belongs to. Five enemies with long names cannot fit in 180 characters.
        LoadingRoster wide = new(
            "1",
            450,
            "ARAM",
            [
                new("self", "自己#1", "", 1, 143, 100, true),
                new("e1", "敌1#2", "", 2, 21, 200, false),
                new("e2", "敌2#3", "", 3, 22, 200, false),
                new("e3", "敌3#4", "", 4, 23, 200, false),
                new("e4", "敌4#5", "", 5, 24, 200, false),
                new("e5", "敌5#6", "", 6, 25, 200, false),
            ]
        );
        Dictionary<int, ControlSkill[]> abilities = new()
        {
            [21] = [new("Q", "缠绕", ["禁锢"], "")],
            [22] = [new("Q", "缠绕", ["禁锢"], "")],
            [23] = [new("Q", "缠绕", ["禁锢"], "")],
            [24] = [new("Q", "缠绕", ["禁锢"], "")],
            [25] = [new("Q", "缠绕", ["禁锢"], "")],
        };
        static string LongName(int id) => "名字很长的英雄" + id + new string('·', 80);

        var messages = OverlayControlChatMessage.Build(wide, Request("hardControls"), abilities, null, LongName);

        Assert.True(messages.Count > 1, $"Expected a continuation line, got {messages.Count}.");
        Assert.All(messages, line => Assert.True(line.Length <= 180, $"'{line}' is longer than one message."));
        Assert.All(messages, line => Assert.StartsWith("【硬控·对方】", line, StringComparison.Ordinal));
    }
}
