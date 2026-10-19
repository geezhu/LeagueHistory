using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The gold curve and the kill feed of a match timeline, read from hand-written payloads. The shapes and the expected
/// values come from the timeline checks in Tests/StatisticsChecks/ModelChecks.cs and TimelineDataChecks.cs.
/// </summary>
public class MatchTimelineTests
{
    // ModelChecks.Timeline(): wrapped in {"json": ...} with the interval in the info block, frames out of order and one
    // of them carrying a negative timestamp.
    private const string WrappedJson = """
        {"json":{"info":{"frameInterval":60000,"frames":[
          {"timestamp":120000,"participantFrames":{"7":{"totalGold":2500}},"events":[{"type":"ELITE_MONSTER_KILL","timestamp":90000,"killerId":0,"monsterType":"DRAGON"}]},
          {"timestamp":-1,"events":[{"type":"CHAMPION_KILL","killerId":7}]},
          {"timestamp":0,"participantFrames":{"1":{"totalGold":500},"7":{"totalGold":600}},"events":[]},
          {"timestamp":60000,"participantFrames":{"1":{"totalGold":1300},"7":{"totalGold":1500}},"events":[{"type":"CHAMPION_KILL","timestamp":40000,"killerId":7,"victimId":1,"position":{"x":100,"y":200}}]}
        ]}}}
        """;

    // StatisticsFixtures.Timeline(): no wrapper and no interval, so the frames array sits at the root.
    private const string UnwrappedJson = """
        {"frames":[
          {"timestamp":0,"participantFrames":{"1":{"totalGold":500},"7":{"totalGold":600}},"events":[]},
          {"timestamp":60000,"participantFrames":{"1":{"totalGold":1300},"7":{"totalGold":1500}},"events":[{"type":"CHAMPION_KILL","timestamp":40000,"killerId":7,"victimId":1,"position":{"x":100,"y":200}}]},
          {"timestamp":120000,"participantFrames":{"7":{"totalGold":2500}},"events":[{"type":"ELITE_MONSTER_KILL","timestamp":90000,"killerId":0,"monsterType":"DRAGON"}]},
          {"timestamp":180000,"participantFrames":{"1":{"totalGold":3200},"7":{"totalGold":3400}},"events":[]}
        ]}
        """;

    // Two blue players and one red player, plus a fourth participant of a team that is neither blue nor red.
    private const string SplitJson = """
        {"frames":[{"timestamp":0,"participantFrames":{"1":{"totalGold":100},"2":{"totalGold":200},"7":{"totalGold":900}}}]}
        """;

    /// <summary>Red player 1 and blue player 7: the teams are read from teamId, not from the participant ids.</summary>
    private static List<JObject> Players() =>
        [
            new JObject
            {
                ["participantId"] = 1,
                ["teamId"] = 200,
                ["riotIdGameName"] = "红队玩家",
            },
            new JObject
            {
                ["participantId"] = 7,
                ["teamId"] = 100,
                ["riotIdGameName"] = "蓝队玩家",
            },
        ];

    private static List<JObject> SplitPlayers() =>
        [
            new JObject
            {
                ["participantId"] = 1,
                ["teamId"] = 100,
                ["riotIdGameName"] = "蓝队一号",
            },
            new JObject
            {
                ["participantId"] = 2,
                ["teamId"] = 100,
                ["riotIdGameName"] = "蓝队二号",
            },
            new JObject
            {
                ["participantId"] = 7,
                ["teamId"] = 200,
                ["riotIdGameName"] = "红队玩家",
            },
            new JObject
            {
                ["participantId"] = 9,
                ["teamId"] = 300,
                ["riotIdGameName"] = "旁观者",
            },
        ];

    private static MatchTimeline Parse(JToken data, IReadOnlyList<JObject> participants) =>
        MatchTimeline.Parse(data, participants);

    private static MatchTimeline Parse(string json, IReadOnlyList<JObject> participants) =>
        Parse(JToken.Parse(json), participants);

    private static MatchTimeline Wrapped() => Parse(WrappedJson, Players());

    private static MatchTimeline Unwrapped() => Parse(UnwrappedJson, Players());

    [Theory]
    [InlineData(600L, 500L, 100.0)]
    [InlineData(300L, 500L, -200.0)]
    public void GoldDifference_BothSidesKnown_ReturnsTheAverageGap(long blue, long red, double expected)
    {
        var timeline = Unwrapped();

        var difference = timeline.GoldDifference(new GoldFrame(0, blue, red));

        Assert.Equal(expected, difference);
    }

    [Fact]
    public void GoldDifference_EqualSides_ReturnsZeroRatherThanMissing()
    {
        var timeline = Unwrapped();

        var difference = timeline.GoldDifference(new GoldFrame(0, 500, 500));

        Assert.Equal(0.0, difference);
    }

    [Theory]
    [InlineData(600L, null)]
    [InlineData(null, 500L)]
    [InlineData(null, null)]
    public void GoldDifference_OneSideMissing_ReturnsNull(long? blue, long? red)
    {
        var timeline = Unwrapped();

        var difference = timeline.GoldDifference(new GoldFrame(0, blue, red));

        Assert.Null(difference);
    }

    [Fact]
    public void GoldDifference_NoPlayerCounts_ReturnsNull()
    {
        var timeline = Parse(UnwrappedJson, []);

        var difference = timeline.GoldDifference(new GoldFrame(0, 600, 500));

        Assert.Null(difference);
    }

    [Fact]
    public void GoldDifference_UnevenTeamSizes_AverageEachSideSeparately()
    {
        var timeline = Parse(SplitJson, SplitPlayers());

        var difference = timeline.GoldDifference(new GoldFrame(0, 300, 900));

        Assert.Equal(-750.0, difference);
    }

    [Fact]
    public void GoldDifference_ParsedFrames_ReportTheGapAndTheMissingInterval()
    {
        var timeline = Unwrapped();

        Assert.Equal(100.0, timeline.GoldDifference(timeline.Frames[0]));
        Assert.Null(timeline.GoldDifference(timeline.Frames[2]));
    }

    [Theory]
    [InlineData("""{"frames":[{"timestamp":0}]}""")]
    [InlineData("""{"info":{"frames":[{"timestamp":0}]}}""")]
    [InlineData("""{"json":{"frames":[{"timestamp":0}]}}""")]
    [InlineData("""{"json":{"info":{"frames":[{"timestamp":0}]}}}""")]
    public void Parse_TimelineWrappers_UnwrapToTheFramesArray(string json)
    {
        var timeline = Parse(json, Players());

        var frame = Assert.Single(timeline.Frames);

        Assert.Equal(0L, frame.Timestamp);
    }

    [Theory]
    [InlineData("""{"frameInterval":30000,"frames":[]}""", 30000L)]
    [InlineData("""{"info":{"frameInterval":60000,"frames":[]}}""", 60000L)]
    [InlineData("""{"json":{"info":{"frameInterval":60000,"frames":[]}}}""", 60000L)]
    public void Parse_IntegerFrameInterval_IsKept(string json, long expected)
    {
        var timeline = Parse(json, Players());

        Assert.Equal(expected, timeline.FrameInterval);
    }

    [Theory]
    [InlineData("""{"frames":[]}""")]
    [InlineData("""{"frameInterval":null,"frames":[]}""")]
    [InlineData("""{"frameInterval":"60000","frames":[]}""")]
    [InlineData("""{"frameInterval":60000.5,"frames":[]}""")]
    public void Parse_FrameIntervalThatIsNotAnInteger_StaysUnknown(string json)
    {
        var timeline = Parse(json, Players());

        Assert.Null(timeline.FrameInterval);
    }

    [Fact]
    public void Parse_RootLevelFrames_ReadAllFourFrames()
    {
        var timeline = Unwrapped();

        Assert.Collection(
            timeline.Frames,
            frame => Assert.Equal(0L, frame.Timestamp),
            frame => Assert.Equal(60000L, frame.Timestamp),
            frame => Assert.Equal(120000L, frame.Timestamp),
            frame => Assert.Equal(180000L, frame.Timestamp)
        );
    }

    [Fact]
    public void Parse_FramesArrivingOutOfOrder_AreSortedByTimestamp()
    {
        var timeline = Wrapped();

        Assert.Collection(
            timeline.Frames,
            frame => Assert.Equal(0L, frame.Timestamp),
            frame => Assert.Equal(60000L, frame.Timestamp),
            frame => Assert.Equal(120000L, frame.Timestamp)
        );
    }

    [Fact]
    public void Parse_NegativeTimestampFrame_DropsItsEventsToo()
    {
        var timeline = Wrapped();

        Assert.Collection(
            timeline.Events,
            kill => Assert.Equal(40000L, kill.Timestamp),
            kill => Assert.Equal(90000L, kill.Timestamp)
        );
    }

    [Theory]
    [InlineData("42")]
    [InlineData("\"120000\"")]
    [InlineData("null")]
    [InlineData("""{"events":[]}""")]
    [InlineData("""{"timestamp":"0"}""")]
    [InlineData("""{"timestamp":null}""")]
    [InlineData("""{"timestamp":0.5}""")]
    public void Parse_FramesThatAreNotTimestampedObjects_AreSkipped(string entry)
    {
        var timeline = Parse($$"""{"frames":[{{entry}},{"timestamp":0}]}""", Players());

        var frame = Assert.Single(timeline.Frames);

        Assert.Equal(0L, frame.Timestamp);
    }

    [Theory]
    [InlineData("""{"info":{"frameInterval":60000}}""")]
    [InlineData("""{"info":{"frameInterval":60000,"frames":{}}}""")]
    [InlineData("""{"info":{"frameInterval":60000,"frames":null}}""")]
    public void Parse_PayloadWithoutFramesArray_ReturnsOnlyTheInterval(string json)
    {
        var timeline = Parse(json, Players());

        Assert.Equal(60000L, timeline.FrameInterval);
        Assert.Empty(timeline.Frames);
        Assert.Empty(timeline.Events);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"x\"")]
    [InlineData("null")]
    public void Parse_PayloadThatIsNotAnObject_ReturnsAnEmptyTimeline(string json)
    {
        var timeline = Parse(json, Players());

        Assert.Empty(timeline.Frames);
        Assert.Empty(timeline.Events);
        Assert.Null(timeline.FrameInterval);
    }

    [Fact]
    public void Parse_Gold_UsesTheParticipantTeamMapping()
    {
        var timeline = Unwrapped();

        var frame = timeline.Frames[0];

        Assert.Equal(600L, frame.Blue);
        Assert.Equal(500L, frame.Red);
    }

    [Fact]
    public void Parse_Gold_IsSummedOverTheTeamPlayers()
    {
        var timeline = Parse(SplitJson, SplitPlayers());

        var frame = Assert.Single(timeline.Frames);

        Assert.Equal(300L, frame.Blue);
        Assert.Equal(900L, frame.Red);
    }

    [Fact]
    public void Parse_ParticipantCounts_SplitByTeamId()
    {
        var timeline = Parse(SplitJson, SplitPlayers());

        Assert.Equal(2, timeline.BluePlayers);
        Assert.Equal(1, timeline.RedPlayers);
    }

    [Fact]
    public void Parse_TotalGoldMissingForOneTeam_LeavesThatSideNull()
    {
        var timeline = Wrapped();

        var frame = timeline.Frames[2];

        Assert.Equal(120000L, frame.Timestamp);
        Assert.Equal(2500L, frame.Blue);
        Assert.Null(frame.Red);
    }

    [Fact]
    public void Parse_FrameWithoutParticipantFrames_LeavesBothSidesNull()
    {
        var timeline = Parse("""{"frames":[{"timestamp":60000,"events":[]}]}""", Players());

        var frame = Assert.Single(timeline.Frames);

        Assert.Null(frame.Blue);
        Assert.Null(frame.Red);
    }

    [Theory]
    [InlineData("""{"timestamp":0,"participantFrames":{"1":{"totalGold":500},"7":{"totalGold":600.5}}}""")]
    [InlineData("""{"timestamp":0,"participantFrames":{"1":{"totalGold":500},"7":{"totalGold":"600"}}}""")]
    [InlineData("""{"timestamp":0,"participantFrames":{"1":{"totalGold":500},"7":{"totalGold":null}}}""")]
    [InlineData("""{"timestamp":0,"participantFrames":{"1":{"totalGold":500},"7":{"totalGold":true}}}""")]
    public void Parse_TotalGoldThatIsNotAnInteger_LeavesThatSideNull(string frameJson)
    {
        var timeline = Parse($$"""{"frames":[{{frameJson}}]}""", Players());

        var frame = Assert.Single(timeline.Frames);

        Assert.Null(frame.Blue);
        Assert.Equal(500L, frame.Red);
    }

    [Fact]
    public void Parse_DuplicateParticipantIds_LeaveGoldUnconfirmed()
    {
        var players = Players();

        var timeline = Parse(WrappedJson, [players[0], players[0]]);

        Assert.All(timeline.Frames, frame => Assert.Null(frame.Blue));
        Assert.All(timeline.Frames, frame => Assert.Null(frame.Red));
    }

    [Theory]
    [InlineData("""{"participantId":9}""")]
    [InlineData("""{"participantId":9,"teamId":"100"}""")]
    [InlineData("""{"participantId":"9","teamId":100}""")]
    public void Parse_IncompleteParticipantMapping_LeavesGoldUnconfirmed(string extra)
    {
        var players = Players();
        players.Add(JObject.Parse(extra));

        var timeline = Parse(WrappedJson, players);

        Assert.All(timeline.Frames, frame => Assert.Null(frame.Blue));
        Assert.All(timeline.Frames, frame => Assert.Null(frame.Red));
    }

    [Fact]
    public void Parse_NoParticipants_LeavesGoldUnconfirmed()
    {
        var timeline = Parse(WrappedJson, []);

        Assert.All(timeline.Frames, frame => Assert.Null(frame.Blue));
        Assert.All(timeline.Frames, frame => Assert.Null(frame.Red));
    }

    [Theory]
    [InlineData("CHAMPION_KILL")]
    [InlineData("ELITE_MONSTER_KILL")]
    [InlineData("BUILDING_KILL")]
    public void Parse_KnownEventKinds_AreKept(string kind)
    {
        var timeline = Parse(
            $$"""{"frames":[{"timestamp":60000,"events":[{"type":"{{kind}}","killerId":7}]}]}""",
            Players()
        );

        var timelineEvent = Assert.Single(timeline.Events);

        Assert.Equal(kind, timelineEvent.Kind);
    }

    [Theory]
    [InlineData("""{"type":"WARD_PLACED","killerId":7}""")]
    [InlineData("""{"type":"SKILL_LEVEL_UP","killerId":7}""")]
    [InlineData("""{"type":"GAME_END"}""")]
    [InlineData("""{"killerId":7}""")]
    [InlineData("""{"type":null,"killerId":7}""")]
    [InlineData("""{"type":"","eventType":"CHAMPION_KILL","killerId":7}""")]
    public void Parse_OtherEventKinds_AreDropped(string timelineEvent)
    {
        var timeline = Parse($$"""{"frames":[{"timestamp":60000,"events":[{{timelineEvent}}]}]}""", Players());

        Assert.Empty(timeline.Events);
    }

    [Fact]
    public void Parse_EventWithoutTypeKey_ReadsTheEventTypeKey()
    {
        var timeline = Parse(
            """{"frames":[{"timestamp":60000,"events":[{"eventType":"BUILDING_KILL","buildingType":"INHIBITOR_BUILDING","killerId":7}]}]}""",
            Players()
        );

        var timelineEvent = Assert.Single(timeline.Events);

        Assert.Equal("BUILDING_KILL", timelineEvent.Kind);
        Assert.Equal("摧毁水晶", timelineEvent.Description);
    }

    [Fact]
    public void Parse_ChampionKill_NamesKillerVictimAndPosition()
    {
        var timeline = Wrapped();

        var kill = timeline.Events[0];

        Assert.Equal("CHAMPION_KILL", kill.Kind);
        Assert.Equal(40000L, kill.Timestamp);
        Assert.Equal("蓝队玩家", kill.Actor);
        Assert.Equal(100, kill.Team);
        Assert.Equal("击杀 红队玩家", kill.Description);
        Assert.Equal(100, kill.X);
        Assert.Equal(200, kill.Y);
    }

    [Fact]
    public void Parse_MonsterKillByTheEnvironment_HasNoTeamAndNoPosition()
    {
        var timeline = Wrapped();

        var kill = timeline.Events[1];

        Assert.Equal("ELITE_MONSTER_KILL", kill.Kind);
        Assert.Equal(90000L, kill.Timestamp);
        Assert.Equal("环境", kill.Actor);
        Assert.Equal("击杀 巨龙", kill.Description);
        Assert.Null(kill.Team);
        Assert.Null(kill.X);
        Assert.Null(kill.Y);
    }

    [Fact]
    public void Parse_KillerOutsideTheRoster_FallsBackToThePlayerNumber()
    {
        var timeline = Parse(
            """{"frames":[{"timestamp":60000,"events":[{"type":"CHAMPION_KILL","killerId":99,"victimId":42}]}]}""",
            Players()
        );

        var kill = Assert.Single(timeline.Events);

        Assert.Equal("玩家 99", kill.Actor);
        Assert.Equal("击杀 玩家 42", kill.Description);
        Assert.Null(kill.Team);
    }

    [Fact]
    public void Parse_ChampionKillWithoutVictim_NamesTheEnvironment()
    {
        var timeline = Parse(
            """{"frames":[{"timestamp":60000,"events":[{"type":"CHAMPION_KILL","killerId":7}]}]}""",
            Players()
        );

        Assert.Equal("击杀 环境", Assert.Single(timeline.Events).Description);
    }

    [Theory]
    [InlineData("""{"participantId":7,"teamId":100,"riotIdGameName":"优先名","summonerName":"备用名"}""", "优先名")]
    [InlineData("""{"participantId":7,"teamId":100,"summonerName":"备用名"}""", "备用名")]
    [InlineData("""{"participantId":7,"teamId":100}""", "玩家 7")]
    public void Parse_KillerName_PrefersRiotIdThenSummonerNameThenNumber(string player, string expected)
    {
        var timeline = Parse(
            """{"frames":[{"timestamp":60000,"events":[{"type":"CHAMPION_KILL","killerId":7}]}]}""",
            [JObject.Parse(player)]
        );

        Assert.Equal(expected, Assert.Single(timeline.Events).Actor);
    }

    [Theory]
    [InlineData("""{"type":"ELITE_MONSTER_KILL","killerId":0,"monsterType":"DRAGON"}""", "击杀 巨龙")]
    [InlineData("""{"type":"ELITE_MONSTER_KILL","killerId":0,"monsterType":"BARON_NASHOR"}""", "击杀 纳什男爵")]
    [InlineData("""{"type":"ELITE_MONSTER_KILL","killerId":0,"monsterType":"RIFTHERALD"}""", "击杀 峡谷先锋")]
    [InlineData("""{"type":"ELITE_MONSTER_KILL","killerId":0,"monsterType":"HORDE"}""", "击杀 虚空巢虫")]
    [InlineData("""{"type":"ELITE_MONSTER_KILL","killerId":0,"monsterType":"UNKNOWN"}""", "击杀 史诗野怪")]
    [InlineData("""{"type":"ELITE_MONSTER_KILL","killerId":0}""", "击杀 史诗野怪")]
    public void Parse_MonsterKill_NamesTheMonster(string timelineEvent, string expected)
    {
        var timeline = Parse($$"""{"frames":[{"timestamp":60000,"events":[{{timelineEvent}}]}]}""", Players());

        Assert.Equal(expected, Assert.Single(timeline.Events).Description);
    }

    [Theory]
    [InlineData("""{"type":"BUILDING_KILL","killerId":7,"buildingType":"INHIBITOR_BUILDING"}""", "摧毁水晶")]
    [InlineData("""{"type":"BUILDING_KILL","killerId":7,"buildingType":"TURRET_BUILDING"}""", "摧毁防御塔")]
    [InlineData("""{"type":"BUILDING_KILL","killerId":7}""", "摧毁防御塔")]
    public void Parse_BuildingKill_NamesInhibitorsAndOtherBuildings(string timelineEvent, string expected)
    {
        var timeline = Parse($$"""{"frames":[{"timestamp":60000,"events":[{{timelineEvent}}]}]}""", Players());

        Assert.Equal(expected, Assert.Single(timeline.Events).Description);
    }

    [Fact]
    public void Parse_EventWithoutTimestamp_TakesTheFrameTimestamp()
    {
        var timeline = Parse(
            """{"frames":[{"timestamp":60000,"events":[{"type":"CHAMPION_KILL","killerId":7}]}]}""",
            Players()
        );

        Assert.Equal(60000L, Assert.Single(timeline.Events).Timestamp);
    }

    [Fact]
    public void Parse_EventsArrivingOutOfOrder_AreSortedByTimestamp()
    {
        var timeline = Parse(
            """
            {"frames":[
              {"timestamp":60000,"events":[{"type":"CHAMPION_KILL","timestamp":50000,"killerId":7}]},
              {"timestamp":120000,"events":[{"type":"CHAMPION_KILL","timestamp":10000,"killerId":1}]}
            ]}
            """,
            Players()
        );

        Assert.Collection(
            timeline.Events,
            kill => Assert.Equal(10000L, kill.Timestamp),
            kill => Assert.Equal(50000L, kill.Timestamp)
        );
    }

    [Fact]
    public void Parse_FrameWithoutEventsKey_ReadsNoEvents()
    {
        var timeline = Parse(
            """{"frames":[{"timestamp":0,"participantFrames":{"1":{"totalGold":500},"7":{"totalGold":600}}}]}""",
            Players()
        );

        Assert.Empty(timeline.Events);
    }
}
