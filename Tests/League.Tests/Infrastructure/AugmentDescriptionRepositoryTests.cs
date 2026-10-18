using League.Infrastructure;
using League.Models;

namespace League.Tests.Infrastructure;

/// <summary>
/// The augment descriptions the Mayhem pages show. The table is a static list, so what is worth pinning is what the
/// rest of the program relies on: it is populated, the same table comes back every time, lookups ignore case, and
/// every tracked augment has a description except the two known gaps. The expected values come from the checks in
/// Tests/StatisticsChecks/AugmentDescriptionChecks.cs, which this class replaces.
/// </summary>
public class AugmentDescriptionRepositoryTests
{
    /// <summary>Tracked augments whose text is not in the table yet; filling them in must not fail the tests.</summary>
    private static readonly string[] KnownGaps = ["大招工具人", "急速之追求"];

    [Fact]
    public void GetAll_TheTable_IsPopulated()
    {
        Dictionary<string, string> descriptions = AugmentDescriptionRepository.GetAll();

        Assert.InRange(descriptions.Count, 201, int.MaxValue);
    }

    [Fact]
    public void GetAll_EveryCall_ReturnsTheSameTable()
    {
        Dictionary<string, string> first = AugmentDescriptionRepository.GetAll();

        Dictionary<string, string> second = AugmentDescriptionRepository.GetAll();

        Assert.Same(first, second);
    }

    [Fact]
    public void GetAll_AKnownAugment_CarriesItsDescription()
    {
        Dictionary<string, string> descriptions = AugmentDescriptionRepository.GetAll();

        Assert.Contains("层数", descriptions["坦克引擎"], StringComparison.Ordinal);
    }

    [Fact]
    public void GetAll_TheSameName_ReturnsTheSameDescription()
    {
        Dictionary<string, string> descriptions = AugmentDescriptionRepository.GetAll();

        Assert.Same(descriptions["无限循环往复"], AugmentDescriptionRepository.GetAll()["无限循环往复"]);
    }

    [Fact]
    public void GetAll_Lookups_IgnoreCase()
    {
        Dictionary<string, string> descriptions = AugmentDescriptionRepository.GetAll();

        Assert.Equal(StringComparer.OrdinalIgnoreCase, descriptions.Comparer);
    }

    [Fact]
    public void GetAll_NoDescription_IsEmpty()
    {
        Dictionary<string, string> descriptions = AugmentDescriptionRepository.GetAll();

        Assert.DoesNotContain(descriptions, entry => entry.Value.Length == 0);
    }

    [Fact]
    public void GetAll_TrackedAugmentsWithoutADescription_AreTheKnownGaps()
    {
        Dictionary<string, string> descriptions = AugmentDescriptionRepository.GetAll();

        string[] missing =
        [
            .. AugmentCatalog.All.Select(info => info.Name).Where(name => !descriptions.ContainsKey(name)),
        ];

        Assert.Empty(missing.Except(KnownGaps));
    }
}
