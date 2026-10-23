using System.Globalization;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// A player's champion mastery: the level and points, and the wording the widget shows for them.
///
/// Two things shape the wording. "Not available" has three causes and three different lines - the player's identity
/// is not public, it is still being confirmed, or the client simply gave nothing - and they are told apart by what the
/// page's message says. And a page that loaded but has no record for this champion is "available with no record",
/// which is not the same as a page that failed to load.
/// </summary>
public class ChampionMasteryTests
{
    private static JObject Record(int championId, int? level = 5, long? points = 12000, long? lastPlay = null)
    {
        var record = new JObject { ["championId"] = championId };
        if (level.HasValue)
        {
            record["championLevel"] = level.Value;
        }

        if (points.HasValue)
        {
            record["championPoints"] = points.Value;
        }

        if (lastPlay.HasValue)
        {
            record["lastPlayTime"] = lastPlay.Value;
        }

        return record;
    }

    private static ChampionMasteryPage Page(
        string message = "",
        bool stale = false,
        DateTimeOffset? fetchedAt = null,
        params JObject[] records
    ) => new(new JArray(records.Cast<object>().ToArray()), fetchedAt, message, stale);

    private static PlayerChampionMastery Mastery(
        bool available = true,
        int? level = 5,
        long? points = 12000,
        long? lastPlay = null,
        bool stale = false,
        DateTimeOffset? fetchedAt = null,
        string message = ""
    ) => new(1, level, points, lastPlay, available, stale, fetchedAt, message);

    // ---- the team card's top champions ----

    [Fact]
    public void Top_SeveralRecords_ListsTheMostPointsFirst()
    {
        var page = Page(records: [Record(1, points: 500), Record(2, points: 90000), Record(3, points: 12000)]);

        var top = page.Top(8).Select(m => m.ChampionId);

        Assert.Equal([2, 3, 1], top);
    }

    [Fact]
    public void Top_MoreRecordsThanAsked_KeepsTheCount()
    {
        var page = Page(records: [Record(1), Record(2), Record(3)]);

        Assert.Equal(2, page.Top(2).Count);
    }

    [Fact]
    public void Top_ARecordWithoutAChampion_IsLeftOut()
    {
        var page = Page(records: [new JObject { ["championPoints"] = 99999 }, Record(7)]);

        Assert.Equal([7], page.Top(8).Select(m => m.ChampionId));
    }

    [Fact]
    public void Top_APageThatDidNotLoad_IsEmpty()
    {
        Assert.Empty(new ChampionMasteryPage(null, null).Top(8));
    }

    // ---- reading a page ----

    [Fact]
    public void ForChampion_AMatchingRecord_ReadsItsNumbers()
    {
        PlayerChampionMastery mastery = Page(records: Record(103, level: 7, points: 50000)).ForChampion(103);

        Assert.Equal(7, mastery.Level);
        Assert.Equal(50000, mastery.Points);
        Assert.True(mastery.Available);
    }

    [Fact]
    public void ForChampion_AnotherChampionsRecord_IsNotUsed()
    {
        PlayerChampionMastery mastery = Page(records: Record(999, level: 7)).ForChampion(103);

        Assert.Null(mastery.Level);
        Assert.Null(mastery.Points);
    }

    [Fact]
    public void ForChampion_APageThatLoadedWithoutThisChampion_IsAvailableButEmpty()
    {
        // "The client answered and has nothing for this champion" is not the same as "the client did not answer".
        PlayerChampionMastery mastery = Page(records: Record(999)).ForChampion(103);

        Assert.True(mastery.Available);
        Assert.Equal("暂无成就记录", mastery.Text);
    }

    [Fact]
    public void ForChampion_APageThatDidNotLoad_IsNotAvailable()
    {
        // Without a payload there is nothing to answer with at all.
        PlayerChampionMastery mastery = new ChampionMasteryPage(null, null).ForChampion(103);

        Assert.False(mastery.Available);
        Assert.Null(mastery.Level);
    }

    [Fact]
    public void ForChampion_ThePagesStampAndMessage_AreCarriedOver()
    {
        DateTimeOffset stamp = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        PlayerChampionMastery mastery = Page(message: "来自缓存", stale: true, fetchedAt: stamp, Record(103))
            .ForChampion(103);

        Assert.True(mastery.Stale);
        Assert.Equal(stamp, mastery.FetchedAt);
        Assert.Equal("来自缓存", mastery.Message);
    }

    // ---- the three unavailable cases ----

    [Fact]
    public void Text_APlayerWhoseIdentityIsNotPublic_SaysSo()
    {
        Assert.Equal("成就未公开", PlayerChampionMastery.Unavailable(103).Text);
    }

    [Fact]
    public void Text_APlayerStillBeingConfirmed_SaysSo()
    {
        // The page's message is what tells the two apart.
        Assert.Equal("成就待确认", Mastery(available: false, message: "玩家身份待确认").Text);
    }

    [Fact]
    public void Text_AnythingElse_IsSimplyUnavailable()
    {
        Assert.Equal("成就暂不可用", Mastery(available: false, message: "请求失败").Text);
    }

    [Fact]
    public void Description_WhenUnavailable_IsTheMessageItself()
    {
        // The widget shows the reason as it came, without a summary of its own.
        Assert.Equal("请求失败", Mastery(available: false, message: "请求失败").Description);
    }

    // ---- the text of an available record ----

    [Fact]
    public void Text_WithNoPoints_SaysThereIsNoRecord()
    {
        Assert.Equal("暂无成就记录", Mastery(points: null).Text);
    }

    [Theory]
    [InlineData(100000000, "1亿")]
    [InlineData(150000000, "1.5亿")]
    [InlineData(10000, "1万")]
    [InlineData(12345, "1.2万")]
    public void Text_LargeNumbers_AreShortened(long points, string expected)
    {
        Assert.Equal("成就 5级 · " + expected, Mastery(points: points).Text);
    }

    [Fact]
    public void Text_ASmallNumber_IsPrintedInFull()
    {
        // Below ten thousand there is nothing to shorten, and the grouping follows the machine's language.
        const long points = 9999;

        Assert.Equal("成就 5级 · " + points.ToString("N0", CultureInfo.CurrentCulture), Mastery(points: points).Text);
    }

    [Fact]
    public void Text_WithoutALevel_NamesOnlyThePoints()
    {
        Assert.Equal("成就 1万", Mastery(level: null, points: 10000).Text);
    }

    [Fact]
    public void Text_ACachedRecord_SaysItIsCached()
    {
        Assert.Equal("成就 5级 · 1万 · 缓存", Mastery(points: 10000, stale: true).Text);
    }

    // ---- the description of an available record ----

    [Fact]
    public void Description_WithPoints_NamesTheLevelAndThePoints()
    {
        // The points are written in full here; only the short line shortens them.
        const long points = 12000;

        Assert.StartsWith(
            "英雄成就：5 级 · " + points.ToString("N0", CultureInfo.CurrentCulture) + " 点",
            Mastery(points: points).Description,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Description_WithoutALevel_LeavesItOut()
    {
        const long points = 12000;

        Assert.StartsWith(
            "英雄成就：" + points.ToString("N0", CultureInfo.CurrentCulture) + " 点",
            Mastery(level: null, points: points).Description,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Description_WithoutPoints_SaysTheClientReturnedNothing()
    {
        Assert.StartsWith(
            "客户端未返回该英雄的成就记录。",
            Mastery(points: null).Description,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Description_AlwaysEndsBySayingItIsNotSplitByMode()
    {
        // The sentence is there because the number sits next to mode statistics and could be read as one.
        Assert.EndsWith("\n成就为该玩家的长期积累，不区分游戏模式。", Mastery().Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Description_ARecentPlay_NamesTheDayAndTime()
    {
        // The stamp is written in local time, so the expectation is built the same way.
        long played = 1_767_225_600_000;

        Assert.Contains(
            "\n最近使用：" + DateTimeOffset.FromUnixTimeMilliseconds(played).ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            Mastery(lastPlay: played).Description,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Description_APlayTimeThatIsNotATime_IsLeftOut(long played)
    {
        // Zero means "never", and a negative stamp is not a date at all.
        Assert.DoesNotContain("最近使用", Mastery(lastPlay: played).Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Description_APlayTimeBeyondAnyDate_IsLeftOut()
    {
        // The upper bound exists so a nonsense stamp does not throw while the tooltip is being built.
        Assert.DoesNotContain("最近使用", Mastery(lastPlay: 253402300799999 + 1).Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Description_AStampFromTheLastFetch_NamesWhenItWasUpdated()
    {
        DateTimeOffset stamp = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        Assert.Contains(
            "\n数据更新：" + stamp.ToLocalTime().ToString("MM-dd HH:mm"),
            Mastery(fetchedAt: stamp).Description,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Description_AStaleRecord_SaysWhyAndForHowLong()
    {
        Assert.Contains("\n正在使用旧缓存，后台检查更新。", Mastery(stale: true).Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Description_ACachedRecordWithAMessage_ShowsTheMessageAsTheReason()
    {
        Assert.Contains(
            "\n正在使用旧缓存：接口失败",
            Mastery(stale: true, message: "接口失败").Description,
            StringComparison.Ordinal
        );
    }

    // ---- the record with nothing in it ----

    [Fact]
    public void Unavailable_HasNoNumbersAndSaysWhy()
    {
        PlayerChampionMastery mastery = PlayerChampionMastery.Unavailable(103);

        Assert.Equal(103, mastery.ChampionId);
        Assert.Null(mastery.Level);
        Assert.Null(mastery.Points);
        Assert.False(mastery.Available);
        Assert.Equal("玩家身份未公开，无法查询英雄成就。", mastery.Message);
    }
}
