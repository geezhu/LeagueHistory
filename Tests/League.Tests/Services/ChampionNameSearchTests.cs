using League.Models;
using League.Services;

namespace League.Tests.Services;

/// <summary>Finding a champion from what the player types on the champion alert settings.</summary>
public class ChampionNameSearchTests
{
    private static readonly Champion[] Champions =
    [
        new()
        {
            Id = -1,
            Name = "无",
            Description = "",
            Alias = "None",
        },
        new()
        {
            Id = 11,
            Name = "无极剑圣",
            Description = "易",
            Alias = "MasterYi",
        },
        new()
        {
            Id = 35,
            Name = "恶魔小丑",
            Description = "萨科",
            Alias = "Shaco",
        },
        new()
        {
            Id = 28,
            Name = "寡妇制造者",
            Description = "伊芙琳",
            Alias = "Evelynn",
        },
        new()
        {
            Id = 29,
            Name = "瘟疫之源",
            Description = "图奇",
            Alias = "Twitch",
        },
    ];

    [Theory]
    [InlineData("恶魔小丑", 35)]
    [InlineData("萨科", 35)]
    [InlineData("shaco", 35)]
    [InlineData("恶魔小丑（萨科）", 35)]
    [InlineData("  易 ", 11)]
    [InlineData("emxc", 35)]
    [InlineData("伊芙", 28)]
    public void Search_UniqueQuery_FindsChampion(string query, int expected)
    {
        var found = ChampionNameSearch.Search(query, Champions);

        Assert.Equal(expected, Assert.Single(found).Id);
    }

    [Fact]
    public void Search_SharedPrefix_ReturnsEveryMatch()
    {
        var found = ChampionNameSearch.Search("w", Champions);

        Assert.Equal([11, 29], found.Select(c => c.Id).Order());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("None")]
    [InlineData("不存在")]
    public void Search_EmptyPlaceholderOrUnknown_FindsNothing(string? query)
    {
        var found = ChampionNameSearch.Search(query, Champions);

        Assert.Empty(found);
    }

    [Fact]
    public void Label_WithTitle_ShowsNameAndTitle()
    {
        string label = ChampionNameSearch.Label(Champions[2]);

        Assert.Equal("恶魔小丑（萨科）", label);
    }
}
