using League.PrimaryElection;

namespace League.Tests.PrimaryElection;

/// <summary>
/// Which preselection list champion select uses: the list for the assigned position, the bench list for the modes
/// that swap from the bench, and the general list whenever that one is missing or empty. The expected values come
/// from the checks in Tests/StatisticsChecks/PreliminaryChecks.cs, which this class replaces.
/// </summary>
public class PreliminaryListsTests
{
    [Fact]
    public void Resolve_RankedWithAnAssignedLane_UsesThatLanesList()
    {
        var config = new PreliminaryConfig { Heroes = [Hero(1, 2), Hero(2, 1)] };
        config.PositionHeroes["middle"] = [Hero(103, 1)];

        var (key, heroes) = PreliminaryLists.Resolve(config, 420, "middle");

        Assert.Equal("middle", key);
        Assert.Equal(103, heroes.Single().ChampionId);
    }

    [Fact]
    public void Resolve_ALaneWithoutAList_FallsBackToTheGeneralListSorted()
    {
        var config = new PreliminaryConfig { Heroes = [Hero(1, 2), Hero(2, 1)] };

        var (key, heroes) = PreliminaryLists.Resolve(config, 420, "utility");

        Assert.Equal("general", key);
        Assert.Equal(new[] { 2, 1 }, heroes.Select(hero => hero.ChampionId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ARAM")]
    [InlineData("middle ")]
    [InlineData("support")]
    public void Resolve_APositionWithoutItsOwnList_UsesTheGeneralList(string position)
    {
        var config = new PreliminaryConfig { Heroes = [Hero(5, 1)] };
        config.PositionHeroes["middle"] = [Hero(103, 1)];

        var (key, heroes) = PreliminaryLists.Resolve(config, 430, position);

        Assert.Equal("general", key);
        Assert.Equal(5, heroes.Single().ChampionId);
    }

    [Theory]
    [InlineData(450)]
    [InlineData(2400)]
    [InlineData(900)]
    public void Resolve_BenchMode_UsesTheAramList(int queueId)
    {
        var config = new PreliminaryConfig { Heroes = [Hero(5, 1)] };
        config.PositionHeroes["aram"] = [Hero(157, 1)];

        var (key, heroes) = PreliminaryLists.Resolve(config, queueId, "");

        Assert.Equal("aram", key);
        Assert.Equal(157, heroes.Single().ChampionId);
    }

    [Fact]
    public void Resolve_AnEmptyBenchList_FallsBackToTheGeneralList()
    {
        var config = new PreliminaryConfig { Heroes = [Hero(5, 1)] };
        config.PositionHeroes["aram"] = [];

        var (key, heroes) = PreliminaryLists.Resolve(config, 450, "middle");

        Assert.Equal("general", key);
        Assert.Equal(5, heroes.Single().ChampionId);
    }

    [Fact]
    public void Resolve_NoConfiguration_HasNoHeroes()
    {
        var (key, heroes) = PreliminaryLists.Resolve(null, 420, "top");

        Assert.Equal("general", key);
        Assert.Empty(heroes);
    }

    [Fact]
    public void Resolve_AListSavedBeforeTheSplit_BecomesTheGeneralList()
    {
        var legacy = Newtonsoft.Json.JsonConvert.DeserializeObject<PreliminaryConfig>(
            """{"Heroes":[{"ChampionId":5,"Priority":1}],"EnableAutoPreliminary":true}"""
        )!;

        var (key, heroes) = PreliminaryLists.Resolve(legacy, 420, "top");

        Assert.Equal("general", key);
        Assert.Equal(5, heroes.Single().ChampionId);
    }

    [Fact]
    public void Get_TheGeneralList_IsTheHeroesField()
    {
        var config = new PreliminaryConfig();

        List<PreliminaryHero> list = PreliminaryLists.Get(config, PreliminaryLists.General);

        Assert.Same(config.Heroes, list);
    }

    [Fact]
    public void Get_ALaneListThatIsNotThereYet_IsCreatedAndKept()
    {
        var config = new PreliminaryConfig();

        List<PreliminaryHero> list = PreliminaryLists.Get(config, "middle");

        Assert.Same(config.PositionHeroes["middle"], list);
    }

    [Fact]
    public void Get_ALaneListThatIsThere_IsReturnedAsItIs()
    {
        var config = new PreliminaryConfig { PositionHeroes = { ["middle"] = [Hero(103, 1)] } };

        List<PreliminaryHero> list = PreliminaryLists.Get(config, "middle");

        Assert.Equal(103, list.Single().ChampionId);
    }

    [Theory]
    [InlineData("general", "通用")]
    [InlineData("aram", "乱斗")]
    [InlineData("middle", "中单")]
    public void NameOf_KnownKey_ReturnsTheNameShown(string key, string expected)
    {
        Assert.Equal(expected, PreliminaryLists.NameOf(key));
    }

    [Fact]
    public void NameOf_UnknownKey_ReturnsTheKeyItself()
    {
        Assert.Equal("unknown", PreliminaryLists.NameOf("unknown"));
    }

    [Theory]
    [InlineData(450, true)]
    [InlineData(2400, true)]
    [InlineData(900, true)]
    [InlineData(420, false)]
    [InlineData(430, false)]
    [InlineData(1900, false)]
    public void IsBenchQueue_QueueId_SaysWhetherChampionsComeFromTheBench(int queueId, bool expected)
    {
        Assert.Equal(expected, PreliminaryLists.IsBenchQueue(queueId));
    }

    [Fact]
    public void AnyHeroes_AGeneralListWithAChampion_IsTrue()
    {
        Assert.True(PreliminaryLists.AnyHeroes(new PreliminaryConfig { Heroes = [Hero(5, 1)] }));
    }

    [Fact]
    public void AnyHeroes_OnlyALaneListWithAChampion_IsTrue()
    {
        var config = new PreliminaryConfig { PositionHeroes = { ["middle"] = [Hero(103, 1)] } };

        Assert.True(PreliminaryLists.AnyHeroes(config));
    }

    [Fact]
    public void AnyHeroes_NoHeroesAtAll_IsFalse()
    {
        Assert.False(PreliminaryLists.AnyHeroes(new PreliminaryConfig()));
    }

    [Fact]
    public void AnyHeroes_NoConfiguration_IsFalse()
    {
        Assert.False(PreliminaryLists.AnyHeroes(null));
    }

    [Fact]
    public void All_TheListsShownInTheWindow_AreTheGeneralLaneAndBenchLists()
    {
        Assert.Equal(
            new[] { "general", "top", "jungle", "middle", "bottom", "utility", "aram" },
            PreliminaryLists.All.Select(list => list.Key)
        );
        Assert.Equal("乱斗", PreliminaryLists.All.Single(list => list.Key == "aram").Name);
    }

    private static PreliminaryHero Hero(int id, int priority) => new() { ChampionId = id, Priority = priority };
}
