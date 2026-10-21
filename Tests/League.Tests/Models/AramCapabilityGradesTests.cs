using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// Grading a team's capability totals against random teams drawn from the same statistics index - ARAM hands out
/// champions at random, so uniform draws approximate real teams.
///
/// Only the empty index is covered here. A full build draws twenty thousand random teams for each of five team sizes,
/// which is a sample big enough to take a noticeable time and whose exact percentiles are a property of the drawing
/// method rather than of any rule worth pinning. What <em>is</em> worth pinning is what an index with no usable numbers
/// produces, because that is the state a failed or filtered download leaves behind - and there the grade has to fall
/// back to the bottom band instead of dividing by an empty sample.
/// </summary>
public class AramCapabilityGradesTests
{
    /// <summary>An index whose champions carry no capability numbers at all.</summary>
    private static AramMetaIndex EmptyIndex() =>
        new(Patch: "16.20.1", Version: "1", Champions: [], Augments: new JObject(), Raw: new JObject());

    /// <summary>An index that has champions, but none of them has the "comp" block the grades read.</summary>
    private static AramMetaIndex IndexWithoutComposition() =>
        new(
            Patch: "16.20.1",
            Version: "1",
            Champions:
            [
                new(
                    Id: 1,
                    Name: "英雄1",
                    Alias: "Hero1",
                    Roles: ["MAGE"],
                    WinRate: 0.5,
                    RawWinRate: 0.5,
                    Games: 100,
                    Data: new JObject { ["win"] = 0.5 }
                ),
            ],
            Augments: new JObject(),
            Raw: new JObject()
        );

    private static AramTeamCapability Capability(bool lack = false, double sum = 10) =>
        new("cc", "控制", sum, Threshold: 5, Lack: lack);

    // ---- the empty index ----

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Percentile_AnIndexWithNoChampions_IsZero(int size)
    {
        // Nothing was sampled, so no team is below this one - and it must not divide by an empty sample to find out.
        Assert.Equal(0, AramCapabilityGrades.Build(EmptyIndex()).Percentile(Capability(), size));
    }

    [Fact]
    public void Percentile_ChampionsWithoutComposition_AreAlsoNothingSampled()
    {
        // A download that worked but carried no numbers is the same case as no download.
        Assert.Equal(0, AramCapabilityGrades.Build(IndexWithoutComposition()).Percentile(Capability(), 5));
    }

    [Fact]
    public void Grade_ACapabilityBelowItsThreshold_IsTheGapGrade()
    {
        // The gap band short-circuits before the percentile: a lack is not a low rank, it is a hole.
        Assert.Equal(
            AramCapabilityGrades.Gap,
            AramCapabilityGrades.Build(EmptyIndex()).Grade(Capability(lack: true), 5)
        );
        Assert.Equal("D", AramCapabilityGrades.Gap);
    }

    [Fact]
    public void Grade_AnUnsampledCapability_FallsToTheBottomBand()
    {
        // Percentile zero lands in the last band, not in "unknown".
        Assert.Equal("C", AramCapabilityGrades.Build(EmptyIndex()).Grade(Capability(), 5));
    }

    // ---- the team size is clamped, not trusted ----

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(99, 5)]
    public void Percentile_ATeamSizeOutsideTheRange_IsTheNearestEdge(int given, int edge)
    {
        // The caller passes a roster size; a nonsense one must read as the nearest real one rather than throw.
        AramCapabilityGrades grades = AramCapabilityGrades.Build(EmptyIndex());

        Assert.Equal(grades.Percentile(Capability(), edge), grades.Percentile(Capability(), given));
    }
}
