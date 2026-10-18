using League.Models;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// Which survival items an inventory holds, by base id, so mode copies share one entry. The expected values come
/// from the checks in Tests/StatisticsChecks/SurvivalSoundChecks.cs.
/// </summary>
public class SurvivalItemsTests
{
    [Theory]
    [InlineData(223157, 3157)]
    [InlineData(773157, 3157)]
    [InlineData(3026, 3026)]
    [InlineData(223026, 3026)]
    public void BaseId_PrefixedItemId_ReturnsTheLastFourDigits(int itemId, int expected)
    {
        int baseId = SurvivalItems.BaseId(itemId);

        Assert.Equal(expected, baseId);
    }

    [Fact]
    public void Find_SurvivalItems_KeepsTheKnownOnesAndDropsTheirModeCopies()
    {
        LiveMatchItem[] items = [new(3157, "中娅沙漏", 1), new(223157, "中娅沙漏", 1), new(1001, "靴子", 1)];

        var found = SurvivalItems.Find(items);

        Assert.Equal(new SurvivalItem(3157, "中娅沙漏", "凝滞", "主动：2.5 秒内免疫伤害且不可被选取"), found.Single());
    }

    [Fact]
    public void Find_ItemsWithoutASurvivalEffect_ReturnsNothing()
    {
        var found = SurvivalItems.Find([new(1001, "靴子", 1), new(3070, "眼泪", 1)]);

        Assert.Empty(found);
    }

    [Fact]
    public void Find_NoInventory_ReturnsNothing()
    {
        var found = SurvivalItems.Find(null);

        Assert.Empty(found);
    }

    [Fact]
    public void Find_TwoDifferentSurvivalItems_KeepsBothInInventoryOrder()
    {
        var found = SurvivalItems.Find([new(3026, "守护天使", 1), new(3157, "中娅沙漏", 1)]);

        Assert.Equal([3026, 3157], found.Select(item => item.Id));
    }
}
