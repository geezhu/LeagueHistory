using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The augments a player picked on the detail page, sent as one line per 180 characters: a header naming the hero,
/// arammeta and its patch, then each augment with its adjusted win rate and pick rate in the order it was picked.
/// </summary>
public class AugmentChatMessageTests
{
    private const string Prefix = "【阿狸·海克斯】arammeta 15.20 调整后胜率/选取率：";

    private static AramMetaRecommendation Row(int id, string name, double? winRate = 0.55, double? pickRate = 0.04) =>
        new([id], name, "", winRate, pickRate, 100, new JObject());

    private static string? NoClientName(int id) => null;

    // ---- choosing the picked rows ----

    [Fact]
    public void Select_SeveralPicks_KeepsThePickedOrder()
    {
        AramMetaRecommendation[] rows = [Row(1, "甲"), Row(2, "乙"), Row(3, "丙")];

        AugmentChatEntry[] entries = AugmentChatMessage.Select(rows, [3, 1], NoClientName);

        Assert.Equal(["丙", "甲"], entries.Select(e => e.Name));
    }

    [Fact]
    public void Select_APickWithoutStatistics_IsSkipped()
    {
        AugmentChatEntry[] entries = AugmentChatMessage.Select([Row(1, "甲")], [9, 1], NoClientName);

        Assert.Equal("甲", Assert.Single(entries).Name);
    }

    [Fact]
    public void Select_TheClientNamesTheAugment_UsesTheClientsName()
    {
        // The detail page shows the client's name once it arrives, so the message names it the same way.
        AugmentChatEntry[] entries = AugmentChatMessage.Select([Row(1, "arammeta 名")], [1], _ => "客户端名");

        Assert.Equal("客户端名", Assert.Single(entries).Name);
    }

    [Fact]
    public void Select_TheClientHasNoName_KeepsArammetasName()
    {
        AugmentChatEntry[] entries = AugmentChatMessage.Select([Row(1, "arammeta 名")], [1], _ => " ");

        Assert.Equal("arammeta 名", Assert.Single(entries).Name);
    }

    [Fact]
    public void Select_TheSameAugmentListedTwice_IsSentOnce()
    {
        AugmentChatEntry[] entries = AugmentChatMessage.Select([Row(1, "甲"), Row(1, "甲")], [1], NoClientName);

        Assert.Single(entries);
    }

    [Fact]
    public void Select_ThePickedRates_ComeFromTheRow()
    {
        AugmentChatEntry[] entries = AugmentChatMessage.Select([Row(1, "甲", 0.6, 0.1)], [1], NoClientName);

        AugmentChatEntry entry = Assert.Single(entries);
        Assert.Equal(0.6, entry.WinRate);
        Assert.Equal(0.1, entry.PickRate);
    }

    // ---- the message ----

    [Fact]
    public void Build_TwoAugments_WritesTheHeaderThenBothInOrder()
    {
        IReadOnlyList<string> lines = AugmentChatMessage.Build(
            "阿狸",
            "15.20",
            [new("护盾猛击", 0.5682, 0.0509), new("暴击伤害", 0.5, 0.1)]
        );

        Assert.Equal(Prefix + "护盾猛击 56.8%/5.1%、暴击伤害 50.0%/10.0%", Assert.Single(lines));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(1.2)]
    [InlineData(-0.1)]
    public void Build_ARateThatCannotBeARate_IsWrittenAsADash(double? rate)
    {
        IReadOnlyList<string> lines = AugmentChatMessage.Build("阿狸", "15.20", [new("甲", rate, rate)]);

        Assert.Equal(Prefix + "甲 —/—", Assert.Single(lines));
    }

    [Fact]
    public void Build_NamesWithLineBreaks_StayOnOneLine()
    {
        // A line break would end the chat message early and send the rest as a second, headerless line.
        IReadOnlyList<string> lines = AugmentChatMessage.Build("阿\n狸", "15.20", [new("护盾\r\n猛击", 0.5, 0.1)]);

        Assert.DoesNotContain('\n', Assert.Single(lines));
    }

    [Fact]
    public void Build_ManyLongNames_SplitsIntoLinesOfAtMost180Characters()
    {
        AugmentChatEntry[] augments = Enumerable
            .Range(0, 8)
            .Select(i => new AugmentChatEntry(new string('名', 30) + i, 0.5, 0.1))
            .ToArray();

        IReadOnlyList<string> lines = AugmentChatMessage.Build("阿狸", "15.20", augments);

        Assert.True(lines.Count > 1, "Eight 31-character names do not fit one 180-character line.");
        Assert.All(lines, line => Assert.True(line.Length <= 180, $"{line.Length} characters: {line}"));
        Assert.All(lines, line => Assert.StartsWith(Prefix, line));
    }

    [Fact]
    public void Build_NoAugments_SendsNothing()
    {
        Assert.Empty(AugmentChatMessage.Build("阿狸", "15.20", []));
    }
}
