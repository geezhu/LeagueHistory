using League.Models;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The Game Bar composition page: only ARAM and ARAM: Mayhem have one, both sides carry the seven capability grades
/// and a damage mix, Mayhem alone adds the estimate and the strongest pairs, and the chat line names the side it
/// describes. The rosters and the champion-only statistics index are built here, so no loading path, client or window
/// is involved. This class replaces the checks in Tests/StatisticsChecks/GameBarCompositionChecks.cs.
/// </summary>
public class GameBarCompositionTests
{
    private const string StubPatch = "9.99";
    private const string KiwiMode = "KIWI";
    private const string AramMode = "ARAM";

    // One chat line per side and mode, spelled exactly as TeamAnalysisChatMessage.Grades writes it. The stub teams
    // below hold no capability totals, so the front line is their only gap: it grades D, the other six grade C.
    private const string MayhemOwnLine =
        "【海克斯大乱斗·我方阵容能力】前排 D · 控制 C · 开团 C · 伤害 C · 消耗 C · 清线 C · 续航 C；物理伤害 50% · 前排 0 人；等级按同人数随机阵容排名，D 为短板";

    private const string MayhemEnemyLine =
        "【海克斯大乱斗·对方阵容能力】前排 D · 控制 C · 开团 C · 伤害 C · 消耗 C · 清线 C · 续航 C；物理伤害 0% · 前排 0 人；等级按同人数随机阵容排名，D 为短板";

    private const string AramOwnLine =
        "【大乱斗·我方阵容能力】前排 D · 控制 C · 开团 C · 伤害 C · 消耗 C · 清线 C · 续航 C；物理伤害 50% · 前排 0 人；等级按同人数随机阵容排名，D 为短板";

    private const string AramEnemyLine =
        "【大乱斗·对方阵容能力】前排 D · 控制 C · 开团 C · 伤害 C · 消耗 C · 清线 C · 续航 C；物理伤害 0% · 前排 0 人；等级按同人数随机阵容排名，D 为短板";

    // The old check's fixture: arammeta's ten champions with the comp data its own site.js was run against. The first
    // five and the last five are one side each, and the self player is player 0.
    private static readonly AramMetaIndex Index = AramTeamScoreFixture.Index;

    private static readonly AramCapabilityGrades Grades = AramCapabilityGrades.Build(Index);

    private static readonly int[] FirstTeam = Index.Champions.Select(champion => champion.Id).Take(5).ToArray();

    private static readonly int[] SecondTeam = Index
        .Champions.Select(champion => champion.Id)
        .Skip(5)
        .Take(5)
        .ToArray();

    // Eleven synthetic champions with no capability totals, so every grade comes from a total of zero. Only the
    // damage mix and the front line carry data, which keeps each expected string readable from this table: the five
    // ad shares 0, ¼, ½, ¾ and 1, plus one champion that counts towards the front line.
    private static readonly (int Id, double Phys, double Magic, double Front)[] StubChampions =
    [
        (1, 0, 100, 0),
        (2, 25, 75, 0),
        (3, 50, 50, 0),
        (4, 75, 25, 0),
        (5, 100, 0, 0),
        (6, 0, 100, 0),
        (7, 0, 100, 0),
        (8, 0, 100, 0),
        (9, 0, 100, 0),
        (10, 0, 100, 0),
        (11, 0, 100, 2),
    ];

    private static readonly AramMetaIndex StubIndex = BuildStubIndex();

    private static readonly AramCapabilityGrades StubGrades = AramCapabilityGrades.Build(StubIndex);

    [Theory]
    [InlineData(450, AramMode, true)]
    [InlineData(2400, "", true)]
    [InlineData(3270, AramMode, true)]
    [InlineData(0, KiwiMode, true)]
    [InlineData(0, "kiwi", true)]
    [InlineData(420, "CLASSIC", false)]
    [InlineData(900, "URF", false)]
    [InlineData(0, "", false)]
    public void Supports_QueueAndMode_DecideTheCompositionPage(int queue, string mode, bool expected)
    {
        var roster = new LoadingRoster("1", queue, mode, []);

        bool supported = GameBarComposition.Supports(roster);

        Assert.Equal(expected, supported);
    }

    [Theory]
    [InlineData(2400, "", true)]
    [InlineData(3270, AramMode, true)]
    [InlineData(0, KiwiMode, true)]
    [InlineData(450, AramMode, false)]
    [InlineData(0, "", false)]
    [InlineData(420, "CLASSIC", false)]
    public void IsMayhem_QueueAndMode_ReportAramMayhemAlone(int queue, string mode, bool expected)
    {
        var roster = new LoadingRoster("1", queue, mode, []);

        bool mayhem = GameBarComposition.IsMayhem(roster);

        Assert.Equal(expected, mayhem);
    }

    [Fact]
    public void Build_RosterWithoutASelfPlayer_ReturnsNull()
    {
        // Both sides are picked; only the missing self player leaves nothing to compare.
        var roster = Roster(2400, KiwiMode, FirstTeam, SecondTeam, self: false);

        var composition = GameBarComposition.Build(Index, Grades, roster, Name);

        Assert.Null(composition);
    }

    [Fact]
    public void Build_SelfPlayerWithoutASide_ReturnsNull()
    {
        // Both sides hold champions, so the self player's missing team number is the only reason left.
        var roster = new LoadingRoster(
            "1",
            2400,
            KiwiMode,
            [
                new LoadingPlayer("self", "", "", 1, FirstTeam[0], 0, true),
                new LoadingPlayer("ally", "", "", 2, FirstTeam[1], 0, false),
                new LoadingPlayer("enemy", "", "", 3, SecondTeam[0], 100, false),
            ]
        );

        var composition = GameBarComposition.Build(Index, Grades, roster, Name);

        Assert.Null(composition);
    }

    [Fact]
    public void Build_SelfSideWithoutChampions_ReturnsNull()
    {
        var roster = new LoadingRoster(
            "1",
            2400,
            KiwiMode,
            [
                new LoadingPlayer("self", "", "", 1, 0, 200, true),
                new LoadingPlayer("enemy", "", "", 2, SecondTeam[0], 100, false),
            ]
        );

        var composition = GameBarComposition.Build(Index, Grades, roster, Name);

        Assert.Null(composition);
    }

    [Fact]
    public void Build_WithoutAnOpponent_ReturnsNull()
    {
        var roster = new LoadingRoster(
            "1",
            2400,
            KiwiMode,
            [
                new LoadingPlayer("self", "", "", 1, FirstTeam[0], 200, true),
                new LoadingPlayer("ally", "", "", 2, FirstTeam[1], 200, false),
            ]
        );

        var composition = GameBarComposition.Build(Index, Grades, roster, Name);

        Assert.Null(composition);
    }

    [Fact]
    public void Build_SelfPlayerOnTheSecondTeam_OwnIsTheSelfPlayersSide()
    {
        // The self player sits on team 100 here, the side the roster lists second for the other tests.
        var roster = new LoadingRoster(
            "1",
            2400,
            KiwiMode,
            [
                .. SecondTeam.Select((id, index) => new LoadingPlayer($"e{index}", "", "", index, id, 200, false)),
                .. FirstTeam.Select((id, index) => new LoadingPlayer($"p{index}", "", "", index, id, 100, index == 0)),
            ]
        );

        var composition = Composition(roster);

        Assert.Equal(FirstTeam, composition.Own.Team);
        Assert.Equal(SecondTeam, composition.Enemy.Team);
    }

    [Fact]
    public void Build_EntriesWithoutAChampion_AreLeftOutOfTheirSide()
    {
        var roster = new LoadingRoster(
            "1",
            2400,
            KiwiMode,
            [
                new LoadingPlayer("p0", "", "", 0, FirstTeam[0], 200, true),
                new LoadingPlayer("p1", "", "", 1, 0, 200, false),
                new LoadingPlayer("p2", "", "", 2, FirstTeam[2], 200, false),
                new LoadingPlayer("e0", "", "", 3, SecondTeam[0], 100, false),
            ]
        );

        var composition = Composition(roster);

        Assert.Equal([FirstTeam[0], FirstTeam[2]], composition.Own.Team);
        Assert.Equal([SecondTeam[0]], composition.Enemy.Team);
    }

    [Fact]
    public void Build_Page_DescribesBothSidesWithSevenGradedCapabilities()
    {
        var composition = Composition(Roster(2400, KiwiMode, FirstTeam, SecondTeam));
        var teams = (JArray)composition.Json["teams"]!;

        Assert.Equal(2, teams.Count);
        Assert.Equal("我方", (string?)teams[0]!["title"]);
        Assert.Equal("敌方", (string?)teams[1]!["title"]);
        Assert.All(teams, team => Assert.Contains("前排", (string)team!["damage"]!));
        Assert.All(teams, team => Assert.Equal(7, ((JArray)team["capabilities"]!).Count));
        Assert.All(
            teams,
            team =>
                Assert.All(
                    (JArray)team["capabilities"]!,
                    capability => Assert.InRange((double)capability["percentile"]!, 0, 1)
                )
        );
    }

    [Fact]
    public void Build_TeamBelowAGapThreshold_NamesTheGapAndGradesItD()
    {
        // arammeta's own reference evaluation of these three champions lacks damage.
        var composition = Composition(
            Roster(2400, KiwiMode, [FirstTeam[0], FirstTeam[1], FirstTeam[2]], [SecondTeam[3]])
        );
        var own = (JObject)composition.Json["teams"]![0]!;
        var damage = ((JArray)own["capabilities"]!).Single(row => (string?)row["name"] == "伤害");

        Assert.Equal("伤害", (string?)own["lacks"]);
        Assert.Equal(true, (bool?)damage["lack"]);
        Assert.Equal("D", (string?)damage["grade"]);
    }

    [Fact]
    public void Build_ChampionsTheIndexDoesNotKnow_AreNamedByTheChampionFunction()
    {
        var roster = Roster(2400, KiwiMode, [99999, 99998, FirstTeam[0]], [SecondTeam[0]]);

        var composition = Composition(roster, id => "英雄 " + id);
        var own = (JObject)composition.Json["teams"]![0]!;

        Assert.Equal("英雄 99999、英雄 99998", (string?)own["missing"]);
    }

    [Fact]
    public void Build_Mayhem_AddsTheEstimateAndTheStrongestPairs()
    {
        var composition = Composition(Roster(2400, KiwiMode, FirstTeam, SecondTeam));
        var own = (JObject)composition.Json["teams"]![0]!;
        var pairs = (JArray)own["pairs"]!;
        var strongest = composition.Own.Pairs.OrderByDescending(pair => Math.Abs(pair.Lift)).First();

        Assert.Equal(true, (bool?)composition.Json["mayhem"]);
        Assert.StartsWith("估计胜率 ", (string)own["winRate"]!);
        Assert.Contains(" · 可信度 ", (string)own["winDetail"]!);
        Assert.Equal(3, pairs.Count);
        Assert.StartsWith($"{Name(strongest.First)} + {Name(strongest.Second)} ", (string)pairs[0]!);
        Assert.All(pairs, pair => Assert.Contains("（同队 ", (string)pair!));
    }

    [Fact]
    public void Build_NormalAram_LeavesOutTheMayhemTrainedEstimate()
    {
        var composition = Composition(Roster(450, AramMode, FirstTeam, SecondTeam));
        var teams = (JArray)composition.Json["teams"]!;

        Assert.Equal(false, (bool?)composition.Json["mayhem"]);
        Assert.All(teams, team => Assert.Null(team["winRate"]));
        Assert.All(teams, team => Assert.Null(team["winDetail"]));
        Assert.All(teams, team => Assert.Null(team["pairs"]));
    }

    [Fact]
    public void Build_Composition_KeepsTheGradesAndTheEvaluatedSides()
    {
        var composition = StubGame([1, 2, 3, 4, 5]);

        Assert.Same(StubGrades, composition.Grades);
        Assert.Equal([1, 2, 3, 4, 5], composition.Own.Team);
        Assert.Equal([10], composition.Enemy.Team);
    }

    [Fact]
    public void Build_Mayhem_ReportsTheExactEstimateAndAnEmptyPairList()
    {
        var own = (JObject)StubGame([1, 2, 3, 4, 5]).Json["teams"]![0]!;

        Assert.Equal("估计胜率 50.0%", (string?)own["winRate"]);
        Assert.Equal("英雄 50.0% · 搭配 ±0.0% · 阵容 ±0.0% · 可信度 低", (string?)own["winDetail"]);
        Assert.Empty((JArray)own["pairs"]!);
    }

    [Fact]
    public void Build_Page_CarriesTheProtocolAndTheRevisionInOrder()
    {
        var composition = StubGame([1, 2, 3, 4, 5], [6, 7, 8, 9, 10]);

        Assert.Equal(1, (int?)composition.Json["protocol"]);
        Assert.Equal("9.99|1,2,3,4,5|6,7,8,9,10", (string?)composition.Json["revision"]);
    }

    [Theory]
    [InlineData(
        true,
        "arammeta 海克斯大乱斗 9.99 统计模型。等级按同人数随机阵容排名，D 为短板；估计胜率不保证单场胜负。"
    )]
    [InlineData(false, "arammeta 大乱斗 9.99 统计模型。等级按同人数随机阵容排名，D 为短板；普通大乱斗不显示估计胜率。")]
    public void Build_Note_StatesTheModeThePatchAndTheGapLetter(bool mayhem, string expected)
    {
        var composition = StubGame([1, 2, 3, 4, 5], mayhem);

        Assert.Equal(expected, (string?)composition.Json["note"]);
    }

    [Fact]
    public void Build_Capabilities_GradeSevenAndMarkTheGapWithD()
    {
        var own = (JObject)StubGame([1, 2, 3, 4, 5]).Json["teams"]![0]!;
        var capabilities = (JArray)own["capabilities"]!;

        Assert.Equal(
            ["前排", "控制", "开团", "伤害", "消耗", "清线", "续航"],
            capabilities.Select(row => (string?)row["name"])
        );
        Assert.Equal(["D", "C", "C", "C", "C", "C", "C"], capabilities.Select(row => (string?)row["grade"]));
        Assert.Equal([true, false, false, false, false, false, false], capabilities.Select(row => (bool?)row["lack"]));
        Assert.All(capabilities, row => Assert.Equal(0.0, (double?)row["percentile"]));
        Assert.Equal("前排：全队合计 0，短板阈值 1；高于 0% 的同人数随机阵容。", (string?)capabilities[0]!["tip"]);
        Assert.Equal("控制：全队合计 0，短板阈值 0；高于 0% 的同人数随机阵容。", (string?)capabilities[1]!["tip"]);
        Assert.Equal("前排", (string?)own["lacks"]);
        Assert.Equal("", (string?)own["missing"]);
    }

    [Theory]
    [InlineData(1, "物理伤害 0%（魔法为主） · 前排 0 人")]
    [InlineData(2, "物理伤害 25% · 前排 0 人")]
    [InlineData(3, "物理伤害 50% · 前排 0 人")]
    [InlineData(4, "物理伤害 75% · 前排 0 人")]
    [InlineData(5, "物理伤害 100%（物理为主） · 前排 0 人")]
    [InlineData(11, "物理伤害 0%（魔法为主） · 前排 1 人")]
    public void Build_DamageLine_DescribesTheAdShareAndTheFrontLine(int championId, string expected)
    {
        var own = (JObject)StubGame([championId]).Json["teams"]![0]!;

        Assert.Equal(expected, (string?)own["damage"]);
    }

    [Fact]
    public void ChatMessage_CompositionStillBeingReadAndASide_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            GameBarComposition.ChatMessage(null, Request("own"))
        );

        Assert.Equal("未发送：阵容分析还在读取，或当前模式没有 arammeta 统计。", error.Message);
    }

    [Theory]
    [InlineData("single")]
    [InlineData("text")]
    [InlineData("")]
    public void ChatMessage_TargetThatIsNotASide_Throws(string target)
    {
        var composition = StubGame([1, 2, 3, 4, 5]);

        var error = Assert.Throws<InvalidOperationException>(() =>
            GameBarComposition.ChatMessage(composition, Request(target))
        );

        Assert.Equal("未发送：阵容分析只能按我方或对方发送。", error.Message);
    }

    [Fact]
    public void ChatMessage_NothingReadAndNoSide_ReportsTheSideFirst()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            GameBarComposition.ChatMessage(null, Request("single"))
        );

        Assert.Equal("未发送：阵容分析只能按我方或对方发送。", error.Message);
    }

    [Theory]
    [InlineData(true, "own", MayhemOwnLine)]
    [InlineData(true, "enemy", MayhemEnemyLine)]
    [InlineData(false, "own", AramOwnLine)]
    [InlineData(false, "enemy", AramEnemyLine)]
    public void ChatMessage_Side_ReturnsTheExactChatLine(bool mayhem, string target, string expected)
    {
        var composition = StubGame([1, 2, 3, 4, 5], mayhem);

        string line = Assert.Single(GameBarComposition.ChatMessage(composition, Request(target)));

        Assert.Equal(expected, line);
    }

    [Theory]
    [InlineData("own")]
    [InlineData("enemy")]
    public void ChatMessage_ReadComposition_FitsOneChatMessage(string target)
    {
        var composition = Composition(Roster(2400, KiwiMode, FirstTeam, SecondTeam));

        string line = Assert.Single(GameBarComposition.ChatMessage(composition, Request(target)));

        Assert.InRange(line.Length, 1, 180);
    }

    private static OverlayChatRequest Request(string target) =>
        new("id", "s", target, -1, 0, 0, false, false, "composition");

    private static string Name(int id) => Index.Champions.First(champion => champion.Id == id).Name;

    private static GameComposition Composition(LoadingRoster roster) =>
        Assert.IsType<GameComposition>(GameBarComposition.Build(Index, Grades, roster, Name));

    private static GameComposition Composition(LoadingRoster roster, Func<int, string> champion) =>
        Assert.IsType<GameComposition>(GameBarComposition.Build(Index, Grades, roster, champion));

    private static GameComposition StubGame(int[] own, bool mayhem = true) => StubGame(own, [10], mayhem);

    private static GameComposition StubGame(int[] own, int[] enemy, bool mayhem = true) =>
        Assert.IsType<GameComposition>(
            GameBarComposition.Build(
                StubIndex,
                StubGrades,
                Roster(mayhem ? 2400 : 450, mayhem ? KiwiMode : AramMode, own, enemy),
                id => "英雄 " + id
            )
        );

    // The self player is player 0 on team 200; the two arrays are the two sides of the roster.
    private static LoadingRoster Roster(int queue, string mode, int[] own, int[] enemy, bool self = true) =>
        new(
            "1",
            queue,
            mode,
            [
                .. own.Select(
                    (id, index) => new LoadingPlayer($"p{index}", "", "", index, id, 200, self && index == 0)
                ),
                .. enemy.Select((id, index) => new LoadingPlayer($"e{index}", "", "", index, id, 100, false)),
            ]
        );

    private static AramMetaIndex BuildStubIndex()
    {
        var champions = new JObject();
        foreach (var (id, phys, magic, front) in StubChampions)
        {
            champions[$"{id}"] = new JObject
            {
                ["name_zh"] = $"英雄{id}",
                ["alias"] = $"Hero{id}",
                ["tags"] = new JArray("Mage"),
                ["wr"] = 0.5,
                ["g"] = 100,
                ["comp"] = new JObject
                {
                    ["phys"] = phys,
                    ["magic"] = magic,
                    ["true"] = 0,
                    ["front"] = front,
                    ["damage"] = 0,
                    ["engage"] = 0,
                    ["wave"] = 0,
                    ["poke"] = 0,
                    ["sustain"] = 0,
                    ["cc"] = 0,
                },
            };
        }

        return AramMetaIndex.Parse(
            new JObject
            {
                ["patch_prefix"] = StubPatch,
                ["detailVersion"] = "test",
                ["champs"] = champions,
                ["recommendation_composition"] = new JObject { ["lack_thresholds"] = new JObject { ["front"] = 1 } },
            }
        );
    }
}
