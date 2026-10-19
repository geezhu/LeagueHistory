using League.Models;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// The "英雄熟练度" chat line: a header naming the map and the side, then one item per entry,
/// wrapped at 180 characters. The text of an item depends only on the entry's mastery record,
/// so every case builds the record directly.
/// </summary>
public class ChampionMasteryChatMessageTests
{
    private const string Prefix = "【普通大乱斗·我方】英雄熟练度：";

    [Fact]
    public void Build_NoEntries_IsRefused()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            ChampionMasteryChatMessage.Build("普通大乱斗", "我方", [])
        );

        Assert.Equal("未发送：英雄熟练度尚未就绪或无法查询，请稍后重试。", error.Message);
    }

    [Fact]
    public void Build_EveryMasteryUnavailable_IsRefused()
    {
        // The gate needs one *available* record; being listed is not enough.
        ChampionMasteryChatEntry[] entries =
        [
            Entry("甲", Mastery(available: false)),
            Entry("乙", Mastery(available: false)),
            Entry("丙", null),
        ];

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            ChampionMasteryChatMessage.Build("普通大乱斗", "我方", entries)
        );

        Assert.Equal("未发送：英雄熟练度尚未就绪或无法查询，请稍后重试。", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Build_OneAvailableEntryAnywhereInTheList_IsEnoughToSend(int position)
    {
        // The gate is "any available", not "the first one available" — so its position must not matter.
        var entries = new List<ChampionMasteryChatEntry>
        {
            Entry("甲", Mastery(available: false)),
            Entry("乙", Mastery(available: false)),
            Entry("丙", Mastery(available: false)),
        };
        entries[position] = Entry("丁", Mastery(available: true, points: 7));

        IReadOnlyList<string> lines = ChampionMasteryChatMessage.Build("普通大乱斗", "我方", entries);

        Assert.Single(lines);
        Assert.Contains("丁", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Build_HeaderSeparatesTheMapAndTheGroupWithTheMiddleDot()
    {
        IReadOnlyList<string> lines = ChampionMasteryChatMessage.Build(
            "海克斯大乱斗",
            "对方",
            [Entry("甲", Mastery(available: true, points: 7))]
        );

        Assert.Equal("【海克斯大乱斗·对方】英雄熟练度：甲 7点", Assert.Single(lines));
    }

    [Theory]
    [InlineData("带\r换行", "带 换行")]
    [InlineData("带\n换行", "带 换行")]
    [InlineData("  两端空白  ", "两端空白")]
    public void Build_TheMapAndGroupAreFlattenedBeforeTheyArePrinted(string raw, string flattened)
    {
        // A header must stay a single chat line, so CR/LF become spaces and the ends are trimmed.
        IReadOnlyList<string> lines = ChampionMasteryChatMessage.Build(
            raw,
            flattened,
            [Entry("甲", Mastery(available: true, points: 7))]
        );

        Assert.StartsWith($"【{flattened}·{flattened}】", Assert.Single(lines), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_AvailableWithLevelAndPoints_NamesBoth()
    {
        IReadOnlyList<string> lines = ChampionMasteryChatMessage.Build(
            "普通大乱斗",
            "我方",
            [Entry("甲", Mastery(available: true, level: 7, points: 7))]
        );

        Assert.Equal(Prefix + "甲 7级 / 7点", Assert.Single(lines));
    }

    [Fact]
    public void Build_AvailableWithoutALevel_NamesThePointsAlone()
    {
        IReadOnlyList<string> lines = ChampionMasteryChatMessage.Build(
            "普通大乱斗",
            "我方",
            [Entry("甲", Mastery(available: true, level: null, points: 7))]
        );

        Assert.Equal(Prefix + "甲 7点", Assert.Single(lines));
    }

    [Fact]
    public void Build_AvailableWithoutPoints_SaysThereIsNoRecord()
    {
        IReadOnlyList<string> lines = ChampionMasteryChatMessage.Build(
            "普通大乱斗",
            "我方",
            [Entry("甲", Mastery(available: true, points: null))]
        );

        Assert.Equal(Prefix + "甲 暂无成就记录", Assert.Single(lines));
    }

    [Fact]
    public void Build_PointsAreGroupedTheWayTheCurrentCultureGroupsNumbers()
    {
        // The source formats with "N0", which inserts the culture's group separator, so this
        // case must not hard-code one: it compares against the same formatting call.
        const long points = 1234567;

        IReadOnlyList<string> lines = ChampionMasteryChatMessage.Build(
            "普通大乱斗",
            "我方",
            [Entry("甲", Mastery(available: true, level: 7, points: points))]
        );

        Assert.Equal(Prefix + "甲 7级 / " + points.ToString("N0") + "点", Assert.Single(lines));
    }

    [Fact]
    public void Build_AvailableButStale_MarksTheItemAsCached()
    {
        IReadOnlyList<string> lines = ChampionMasteryChatMessage.Build(
            "普通大乱斗",
            "我方",
            [Entry("甲", Mastery(available: true, level: 7, points: 7, stale: true))]
        );

        Assert.Equal(Prefix + "甲 7级 / 7点（缓存）", Assert.Single(lines));
    }

    [Fact]
    public void Build_UnavailableWithAHiddenIdentity_SaysTheIdentityIsNotPublic()
    {
        // "未公开" in the record's message is what selects this wording, not the unavailability itself.
        IReadOnlyList<string> lines = ChampionMasteryChatMessage.Build(
            "普通大乱斗",
            "我方",
            [Entry("甲", PlayerChampionMastery.Unavailable(1)), Entry("乙", Mastery(available: true, points: 7))]
        );

        Assert.Contains("甲 身份未公开", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Build_UnavailableForAnyOtherReason_IsSimplyUnavailable()
    {
        IReadOnlyList<string> lines = ChampionMasteryChatMessage.Build(
            "普通大乱斗",
            "我方",
            [Entry("甲", Mastery(available: false)), Entry("乙", Mastery(available: true, points: 7))]
        );

        Assert.Contains("甲 暂不可用", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("身份未公开", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Build_AnEntryWithoutARecord_IsUnavailable()
    {
        IReadOnlyList<string> lines = ChampionMasteryChatMessage.Build(
            "普通大乱斗",
            "我方",
            [Entry("甲", null), Entry("乙", Mastery(available: true, points: 7))]
        );

        Assert.Contains("甲 暂不可用", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Build_SeveralEntries_AreJoinedByTheEnumeratingComma()
    {
        IReadOnlyList<string> lines = ChampionMasteryChatMessage.Build(
            "普通大乱斗",
            "我方",
            [
                Entry("甲", Mastery(available: true, level: 7, points: 7)),
                Entry("乙", Mastery(available: true, level: 6, points: 8)),
            ]
        );

        Assert.Equal(Prefix + "甲 7级 / 7点、乙 6级 / 8点", Assert.Single(lines));
    }

    [Fact]
    public void Build_EntriesThatFit_StayOnOneLine()
    {
        IReadOnlyList<string> lines = ChampionMasteryChatMessage.Build(
            "普通大乱斗",
            "我方",
            [
                Entry("甲", Mastery(available: true, level: 7, points: 7)),
                Entry("乙", Mastery(available: true, level: 7, points: 7)),
            ]
        );

        Assert.Single(lines);
        Assert.True(lines[0].Length <= 180, $"The line is {lines[0].Length} characters, which is over the 180 limit.");
    }

    [Fact]
    public void Build_MoreEntriesThanOneLineHolds_WrapsOntoASecondLineThatRepeatsTheHeader()
    {
        // Ten long items cannot fit under 180 characters, so the builder must flush and restart.
        var entries = Enumerable
            .Range(0, 10)
            .Select(i => Entry($"英雄名字很长很长{i}", Mastery(available: true, level: 12, points: 123456)))
            .ToArray();

        IReadOnlyList<string> lines = ChampionMasteryChatMessage.Build("普通大乱斗", "我方", entries);

        Assert.True(lines.Count > 1, "Ten long items should have been wrapped onto more than one line.");
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
        // Every entry is still named exactly once across the wrapped lines.
        string joined = string.Join("", lines);
        for (int i = 0; i < entries.Length; i++)
        {
            Assert.Equal(1, CountOccurrences(joined, entries[i].Champion));
        }
    }

    [Fact]
    public void Build_AnEmptyChampionName_StillProducesAnItem()
    {
        // The builder does not guard the name; it trims it and prints what is left.
        IReadOnlyList<string> lines = ChampionMasteryChatMessage.Build(
            "普通大乱斗",
            "我方",
            [Entry("", Mastery(available: true, points: 7))]
        );

        Assert.Equal(Prefix + " 7点", Assert.Single(lines));
    }

    private static ChampionMasteryChatEntry Entry(string champion, PlayerChampionMastery? mastery) =>
        new(champion, mastery);

    private static PlayerChampionMastery Mastery(
        bool available,
        int? level = null,
        long? points = null,
        bool stale = false,
        string message = ""
    ) =>
        new(
            ChampionId: 1,
            Level: level,
            Points: points,
            LastPlayTime: null,
            Available: available,
            Stale: stale,
            Message: message
        );

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
