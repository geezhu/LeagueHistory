using League.Services;

namespace League.Tests.Services;

/// <summary>
/// One line of champion tiers: a header naming the map, the group, the source and the version, then one item per
/// champion, wrapped at 180 characters.
///
/// Two details are worth calling out. A rate is only printed when it could be a rate at all - finite and between 0
/// and 1 - so a missing or out-of-range value becomes the em dash instead of a nonsense percentage; and the number is
/// formatted with the invariant culture, so the decimal point does not follow the machine's language.
/// </summary>
public class ChampionTierChatMessageTests
{
    private const string Prefix = "【海克斯大乱斗·我方】OP.GG 15.1 胜率/登场率：";

    private static ChampionTierChatEntry Entry(
        string name,
        string tier = "T1",
        double? winRate = 0.512,
        double? pickRate = 0.083
    ) => new(name, tier, winRate, pickRate);

    // ---- the header ----

    [Fact]
    public void Build_AnOrdinaryRequest_WritesTheHeaderThenTheItems()
    {
        IReadOnlyList<string> lines = ChampionTierChatMessage.Build(
            "海克斯大乱斗",
            "OP.GG",
            "15.1",
            "我方",
            [Entry("阿狸")]
        );

        Assert.Equal(Prefix + "阿狸 T1 51.2%/8.3%", Assert.Single(lines));
    }

    [Fact]
    public void Build_AdjustedRates_SaySoInTheHeader()
    {
        IReadOnlyList<string> lines = ChampionTierChatMessage.Build(
            "海克斯大乱斗",
            "arammeta",
            "15.1",
            "我方",
            [Entry("阿狸")],
            adjustedRates: true
        );

        Assert.StartsWith(
            "【海克斯大乱斗·我方】arammeta 15.1 调整后胜率/样本选取率：",
            Assert.Single(lines),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("带\r换行", "带 换行")]
    [InlineData("带\n换行", "带 换行")]
    [InlineData("  两端空白  ", "两端空白")]
    public void Build_TheHeaderPartsAreFlattenedBeforeTheyArePrinted(string raw, string flattened)
    {
        // A header must stay a single chat line, so CR/LF become spaces and the ends are trimmed.
        IReadOnlyList<string> lines = ChampionTierChatMessage.Build(
            raw,
            flattened,
            flattened,
            flattened,
            [Entry("阿狸")]
        );

        Assert.StartsWith(
            $"【{flattened}·{flattened}】{flattened} {flattened} ",
            Assert.Single(lines),
            StringComparison.Ordinal
        );
    }

    // ---- the rate rule ----

    [Theory]
    [InlineData(0.0, "0.0%")]
    [InlineData(0.5, "50.0%")]
    [InlineData(1.0, "100.0%")]
    [InlineData(0.512, "51.2%")]
    [InlineData(0.083, "8.3%")]
    [InlineData(0.125, "12.5%")]
    public void Build_ARateBetweenZeroAndOne_IsPrintedAsAPercentage(double rate, string expected)
    {
        IReadOnlyList<string> lines = ChampionTierChatMessage.Build(
            "海克斯大乱斗",
            "OP.GG",
            "15.1",
            "我方",
            [Entry("阿狸", winRate: rate, pickRate: rate)]
        );

        Assert.Equal(Prefix + $"阿狸 T1 {expected}/{expected}", Assert.Single(lines));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-0.001)]
    [InlineData(1.001)]
    [InlineData(12.5)]
    public void Build_ARateThatCouldNotBeARate_BecomesTheEmDash(double? rate)
    {
        IReadOnlyList<string> lines = ChampionTierChatMessage.Build(
            "海克斯大乱斗",
            "OP.GG",
            "15.1",
            "我方",
            [Entry("阿狸", winRate: rate, pickRate: rate)]
        );

        Assert.Equal(Prefix + "阿狸 T1 —/—", Assert.Single(lines));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Build_ANonFiniteRate_BecomesTheEmDash(double rate)
    {
        // Without the finiteness check these would print "NaN%" and "∞%".
        IReadOnlyList<string> lines = ChampionTierChatMessage.Build(
            "海克斯大乱斗",
            "OP.GG",
            "15.1",
            "我方",
            [Entry("阿狸", winRate: rate, pickRate: rate)]
        );

        Assert.Equal(Prefix + "阿狸 T1 —/—", Assert.Single(lines));
    }

    [Fact]
    public void Build_TheTwoRatesArePrintedIndependently()
    {
        // A win rate with no pick rate must not drag the other one down with it.
        IReadOnlyList<string> lines = ChampionTierChatMessage.Build(
            "海克斯大乱斗",
            "OP.GG",
            "15.1",
            "我方",
            [Entry("阿狸", winRate: 0.5, pickRate: null)]
        );

        Assert.Equal(Prefix + "阿狸 T1 50.0%/—", Assert.Single(lines));
    }

    [Fact]
    public void Build_ARateIsWrittenWithADotWhateverTheMachineLanguageIs()
    {
        // "50.0%" is the invariant form; a comma would appear if the number followed the current culture.
        IReadOnlyList<string> lines = ChampionTierChatMessage.Build(
            "海克斯大乱斗",
            "OP.GG",
            "15.1",
            "我方",
            [Entry("阿狸", winRate: 0.5, pickRate: 0.5)]
        );

        Assert.Contains("50.0%", Assert.Single(lines), StringComparison.Ordinal);
        Assert.DoesNotContain("50,0%", Assert.Single(lines), StringComparison.Ordinal);
    }

    // ---- the tier rule ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("暂无梯队")]
    public void Build_ATierThatSaysNothing_BecomesTheEmDash(string tier)
    {
        IReadOnlyList<string> lines = ChampionTierChatMessage.Build(
            "海克斯大乱斗",
            "OP.GG",
            "15.1",
            "我方",
            [Entry("阿狸", tier: tier)]
        );

        Assert.Equal(Prefix + "阿狸 — 51.2%/8.3%", Assert.Single(lines));
    }

    [Theory]
    [InlineData("T1", "T1")]
    [InlineData("   T2   ", "T2")]
    [InlineData("带\r换行", "带 换行")]
    public void Build_ATierWithContent_IsFlattenedAndKept(string tier, string expected)
    {
        IReadOnlyList<string> lines = ChampionTierChatMessage.Build(
            "海克斯大乱斗",
            "OP.GG",
            "15.1",
            "我方",
            [Entry("阿狸", tier: tier)]
        );

        Assert.Equal(Prefix + $"阿狸 {expected} 51.2%/8.3%", Assert.Single(lines));
    }

    // ---- the list ----

    [Fact]
    public void Build_NoChampions_SendsNothingAtAll()
    {
        // An empty list is not an error here: there is simply no line to send.
        Assert.Empty(ChampionTierChatMessage.Build("海克斯大乱斗", "OP.GG", "15.1", "我方", []));
    }

    [Fact]
    public void Build_SeveralChampions_AreJoinedByTheEnumeratingComma()
    {
        IReadOnlyList<string> lines = ChampionTierChatMessage.Build(
            "海克斯大乱斗",
            "OP.GG",
            "15.1",
            "我方",
            [Entry("阿狸", "T1"), Entry("锐雯", "T2", 0.4, 0.05)]
        );

        Assert.Equal(Prefix + "阿狸 T1 51.2%/8.3%、锐雯 T2 40.0%/5.0%", Assert.Single(lines));
    }

    [Fact]
    public void Build_ChampionsThatFit_StayOnOneLine()
    {
        IReadOnlyList<string> lines = ChampionTierChatMessage.Build(
            "海克斯大乱斗",
            "OP.GG",
            "15.1",
            "我方",
            [Entry("阿狸"), Entry("锐雯")]
        );

        Assert.Single(lines);
        Assert.True(lines[0].Length <= 180, $"The line is {lines[0].Length} characters, which is over the 180 limit.");
    }

    [Fact]
    public void Build_MoreChampionsThanOneLineHolds_WrapOntoAnotherLineThatRepeatsTheHeader()
    {
        // The names end in a marker so that none of them is a prefix of another - "英雄1" would otherwise be
        // counted inside "英雄10" and the count below would be wrong for a reason that has nothing to do with wrapping.
        var champions = Enumerable
            .Range(0, 12)
            .Select(i => Entry($"名字很长的英雄{i}号", "T1", 0.512, 0.083))
            .ToArray();

        IReadOnlyList<string> lines = ChampionTierChatMessage.Build("海克斯大乱斗", "OP.GG", "15.1", "我方", champions);

        Assert.True(lines.Count > 1, "Twelve long items should have been wrapped onto more than one line.");
        Assert.All(
            lines,
            line =>
            {
                Assert.StartsWith(Prefix, line, StringComparison.Ordinal);
                Assert.True(
                    line.Length <= 180,
                    $"A wrapped line is {line.Length} characters, which is over the 180 limit."
                );
            }
        );
        // Every champion is named exactly once across the wrapped lines.
        string joined = string.Concat(lines);
        foreach (ChampionTierChatEntry champion in champions)
        {
            Assert.Equal(1, CountOccurrences(joined, champion.Name));
        }
    }

    [Fact]
    public void Build_OneItemLongerThanTheWholeLine_IsStillWritten()
    {
        // The wrap only happens between items, so a single item that cannot fit starts a line of its own that is
        // allowed to run past the limit rather than being cut.
        IReadOnlyList<string> lines = ChampionTierChatMessage.Build(
            "海克斯大乱斗",
            "OP.GG",
            "15.1",
            "我方",
            [Entry(new string('长', 200))]
        );

        Assert.Single(lines);
        Assert.True(lines[0].Length > 180, "A single oversized item is written whole rather than cut.");
        Assert.Contains(new string('长', 200), lines[0], StringComparison.Ordinal);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        for (
            int i = haystack.IndexOf(needle, StringComparison.Ordinal);
            i >= 0;
            i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)
        )
        {
            count++;
        }

        return count;
    }
}
