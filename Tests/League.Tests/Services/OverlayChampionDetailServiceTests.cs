using League.Models;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The champion detail page behind the Game Bar widget: which roster slot may request it, the arammeta and op.gg
/// recommendations it is built from, and the search text of an augment the client has renamed. The expected values
/// come from the checks in Tests/StatisticsChecks/OverlayChampionDetailChecks.cs, which this class replaces. No
/// request leaves the process: the arammeta captures under TestData/AramMeta are the whole input.
/// </summary>
public class OverlayChampionDetailServiceTests
{
    private const string Session = "instance:match";
    private const string OpaqueId = "0f8fad5bd9cb469fa16570867728950e";
    private const long Now = 1_700_000_000_000;

    [Fact]
    public void Parse_AnEnemySlotWithoutAccountIdentifiers_ReturnsThatHero()
    {
        JObject request = Request(slot: 5, championId: 103);

        OverlayDetailRequest? parsed = OverlayDetailRequest.Parse(request, Session, Roster(), Now);

        Assert.Equal(5, parsed?.Slot);
        Assert.Equal(103, parsed?.ChampionId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30_000)]
    public void Parse_AStampAtTheEdgeOfTheThirtySecondWindow_IsAccepted(long age)
    {
        JObject request = Request(slot: 5, championId: 103, stamp: Now - age);

        OverlayDetailRequest? parsed = OverlayDetailRequest.Parse(request, Session, Roster(), Now);

        Assert.Equal(103, parsed?.ChampionId);
    }

    [Theory]
    [InlineData(30_001)]
    [InlineData(60_000)]
    [InlineData(-1)]
    public void Parse_StaleAndFutureStamps_CannotStartALoad(long age)
    {
        JObject request = Request(slot: 5, championId: 103, stamp: Now - age);

        Assert.Null(OverlayDetailRequest.Parse(request, Session, Roster(), Now));
    }

    [Theory]
    [InlineData("next-match")]
    [InlineData("")]
    public void Parse_ASessionThatIsNotTheCurrentGame_IsRejected(string session)
    {
        JObject request = Request(slot: 5, championId: 103, session: session);

        Assert.Null(OverlayDetailRequest.Parse(request, Session, Roster(), Now));
    }

    [Theory]
    [InlineData(1, 103)]
    [InlineData(0, 103)]
    [InlineData(5, 450)]
    [InlineData(9, 103)]
    [InlineData(10, 103)]
    public void Parse_AnEmptyOrMismatchedRosterSlot_CannotRequestAHero(int slot, int championId)
    {
        JObject request = Request(slot, championId);

        Assert.Null(OverlayDetailRequest.Parse(request, Session, Roster(), Now));
    }

    [Fact]
    public void Parse_ReturningToRoster_CancelsTheSelection()
    {
        JObject request = Request(slot: -1, championId: 0);

        OverlayDetailRequest? parsed = OverlayDetailRequest.Parse(request, Session, Roster(), Now);

        Assert.Equal(-1, parsed?.Slot);
        Assert.Equal(0, parsed?.ChampionId);
    }

    [Fact]
    public void Parse_CancellingWithAHero_IsRejected()
    {
        JObject request = Request(slot: -1, championId: 103);

        Assert.Null(OverlayDetailRequest.Parse(request, Session, Roster(), Now));
    }

    [Theory]
    [InlineData("../../other")]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public void Parse_AnIdentifierThatIsNotOpaque_IsRejected(string id)
    {
        JObject request = Request(slot: 5, championId: 103, id: id);

        Assert.Null(OverlayDetailRequest.Parse(request, Session, Roster(), Now));
    }

    [Theory]
    [InlineData("protocol", "2")]
    [InlineData("updatedAt", "\"1700000000000\"")]
    [InlineData("slot", "\"5\"")]
    [InlineData("championId", "\"103\"")]
    public void Parse_AFieldOfTheWrongTypeOrProtocol_IsRejected(string field, string value)
    {
        JObject request = Request(slot: 5, championId: 103);
        request[field] = JToken.Parse(value);

        Assert.Null(OverlayDetailRequest.Parse(request, Session, Roster(), Now));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("updatedAt")]
    [InlineData("slot")]
    [InlineData("championId")]
    [InlineData("session")]
    public void Parse_ARequestMissingAField_IsRejected(string field)
    {
        JObject request = Request(slot: 5, championId: 103);
        request.Remove(field);

        Assert.Null(OverlayDetailRequest.Parse(request, Session, Roster(), Now));
    }

    [Theory]
    [InlineData(2400, "海克斯大乱斗")]
    [InlineData(3270, "海克斯大乱斗")]
    [InlineData(450, "极地大乱斗")]
    [InlineData(900, "无限火力")]
    [InlineData(1900, "无限火力")]
    [InlineData(1700, "斗魂竞技场")]
    [InlineData(1710, "斗魂竞技场")]
    [InlineData(1200, "极限闪击")]
    [InlineData(1300, "极限闪击")]
    [InlineData(400, "召唤师峡谷")]
    [InlineData(420, "召唤师峡谷")]
    [InlineData(490, "召唤师峡谷")]
    [InlineData(0, "当前模式")]
    public void ModeName_AKnownQueue_NamesTheModeTheWidgetShows(int queue, string expected)
    {
        Assert.Equal(expected, OverlayChampionDetailService.ModeName(queue));
    }

    [Theory]
    [InlineData("坦克引擎", "tkyq")]
    [InlineData("终极刷新", "zjsx")]
    [InlineData("Ult Bot 大招！", "ultbotdz")]
    [InlineData("天降熊", "tjx")]
    [InlineData("空投熊", "ktx")]
    public void Of_ChineseAndLatinText_KeepsOneInitialPerCharacter(string text, string expected)
    {
        Assert.Equal(expected, ChineseInitials.Of(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("〇")]
    [InlineData("！？ 　")]
    public void Of_EmptyOrUnknownCharacters_ReturnAnEmptyString(string? text)
    {
        Assert.Equal(string.Empty, ChineseInitials.Of(text));
    }

    [Fact]
    public void UpdateAugmentSearch_AnAugmentTheClientHasNotRenamed_IsSearchableByItsOwnInitials()
    {
        JObject row = AugmentRow("天降熊");

        OverlayChampionDetailService.UpdateAugmentSearch(row);

        Assert.Equal("tjx", (string?)row["search"]);
    }

    [Fact]
    public void UpdateAugmentSearch_AClientRename_LeadsWithTheShownNameAndRepeats()
    {
        JObject row = AugmentRow("天降熊");
        OverlayChampionDetailService.UpdateAugmentSearch(row);
        row["icons"]![0]!["name"] = "空投熊";

        OverlayChampionDetailService.UpdateAugmentSearch(row);

        Assert.Equal("ktx 天降熊 tjx", (string?)row["search"]);
        OverlayChampionDetailService.UpdateAugmentSearch(row);
        Assert.Equal("ktx 天降熊 tjx", (string?)row["search"]);
    }

    [Fact]
    public void UpdateAugmentSearch_ARowWithoutIcons_HasNoSearchText()
    {
        var row = new JObject { ["title"] = "" };

        OverlayChampionDetailService.UpdateAugmentSearch(row);

        Assert.Equal(string.Empty, (string?)row["search"]);
    }

    [Fact]
    public void FromAramMeta_TheCapturedPage_ShowsAllThreeRarities()
    {
        JToken[] sections = Sections(Page(AramMetaFixture.Detail()));

        Assert.Equal(new[] { "棱彩强化", "黄金强化", "白银强化" }, Augments(sections).Select(Title));
        Assert.DoesNotContain(sections, section => (string?)section["tab"] == "runes");
    }

    [Fact]
    public void FromAramMeta_TheCapturedPage_NamesArammetaAndItsPatch()
    {
        JObject data = OverlayChampionDetailService.FromAramMeta(Page(AramMetaFixture.Detail()), AramMetaFixture.Ahri);

        Assert.Equal($"arammeta · {AramMetaFixture.Patch} · 来源推荐顺序", (string?)data["source"]);
    }

    [Fact]
    public void FromAramMeta_AFailedUpdate_SaysTheOldCacheIsUsed()
    {
        AramMetaPage page = Page(AramMetaFixture.Detail()) with { UpdateFailed = true };

        JObject data = OverlayChampionDetailService.FromAramMeta(page, AramMetaFixture.Ahri);

        Assert.Equal(
            $"arammeta · {AramMetaFixture.Patch} · 来源推荐顺序 · 更新失败，使用旧缓存",
            (string?)data["source"]
        );
    }

    [Theory]
    [InlineData("kPrismatic", 0)]
    [InlineData("kGold", 1)]
    [InlineData("kSilver", 2)]
    public void FromAramMeta_EachRarity_KeepsTheHeroesPoolRowsInSourceOrder(string category, int index)
    {
        JToken[] sections = Sections(Page(AramMetaFixture.Detail()));

        Assert.Equal(PoolIds(category), RowIds(Augments(sections)[index]));
    }

    [Theory]
    [InlineData("kPrismatic", 0, "胜率 56.8%", "选取率 5.1%")]
    [InlineData("kGold", 1, "胜率 55.5%", "选取率 6.3%")]
    [InlineData("kSilver", 2, "胜率 54.4%", "选取率 5.0%")]
    public void FromAramMeta_EachRarity_CarriesTheHeroesOwnRates(
        string category,
        int index,
        string winRate,
        string pickRate
    )
    {
        JToken[] sections = Sections(Page(AramMetaFixture.Detail()));

        JObject row = (JObject)Augments(sections)[index]!["rows"]![0]!;
        Assert.Equal(PoolIds(category)[0], (int)row["icons"]![0]!["id"]!);
        Assert.Equal(winRate, (string?)row["winRateText"]);
        Assert.Equal(pickRate, (string?)row["pickRateText"]);
    }

    [Fact]
    public void FromAramMeta_TheCapturedPool_KeepsAll129AugmentRows()
    {
        JToken[] sections = Sections(Page(AramMetaFixture.Detail()));

        Assert.Equal(129, Augments(sections).Sum(RowCount));
    }

    [Fact]
    public void FromAramMeta_DuplicateAndUnclassifiedPoolRows_AddNoCards()
    {
        JObject detail = AramMetaFixture.Detail();
        var pool = (JArray)detail["poolAugments"]!;
        pool.Add(pool[0]!.DeepClone());
        pool.Add(new JObject { ["id"] = 0, ["wr"] = 1 });
        pool.Add(new JObject { ["id"] = int.MaxValue, ["wr"] = 1 });
        pool.Add(JValue.CreateNull());

        JToken[] sections = Sections(Page(detail));

        Assert.Equal(129, Augments(sections).Sum(RowCount));
    }

    [Fact]
    public void FromAramMeta_AMissingHero_NeverInheritsAnotherHeroesPoolStatistics()
    {
        JToken[] sections = Sections(Page(AramMetaFixture.Detail()), int.MaxValue);

        Assert.Equal(new[] { 0, 0, 0 }, Augments(sections).Select(RowCount));
    }

    [Fact]
    public void FromAramMeta_WithoutAPool_FallsBackToTheDetailedRecommendations()
    {
        var detail = (JObject)AramMetaFixture.Json("103-augments")["detail"]!;
        ((JArray)detail["poolAugments"]!).Clear();

        JToken[] sections = Sections(Page(detail));

        Assert.Equal(new[] { 16, 16, 16 }, Augments(sections).Select(RowCount));
    }

    [Fact]
    public void FromAramMeta_WithoutDetail_ShowsOnlyTheCachedAugmentRecommendations()
    {
        JToken[] sections = Sections(Page(null));

        Assert.Equal(new[] { "augments", "augments", "augments" }, sections.Select(section => (string?)section["tab"]));
    }

    [Theory]
    [InlineData("kPrismatic", 0)]
    [InlineData("kGold", 1)]
    [InlineData("kSilver", 2)]
    public void FromAramMeta_WithoutDetail_KeepsTheThreeOverviewRowsPerRarity(string category, int index)
    {
        AramMetaPage page = Page(null);
        AramMetaChampion champion = page.Index.Champions.Single(row => row.Id == AramMetaFixture.Ahri);
        int[] overview = ((JArray)champion.Data["top"]![category]!).Select(row => (int)row["id"]!).ToArray();

        JToken[] sections = Sections(page);

        Assert.Equal(3, overview.Length);
        Assert.Equal(overview, RowIds(Augments(sections)[index]));
    }

    [Fact]
    public void FromAramMeta_TheAugmentRows_CarryInitialsForTheWidgetSearch()
    {
        JToken[] sections = Sections(Page(AramMetaFixture.Detail()));

        JObject row = (JObject)Augments(sections)[0]!["rows"]![0]!;
        var icon = (JObject)row["icons"]![0]!;
        Assert.Equal((string?)icon["name"], (string?)icon["sourceName"]);
        Assert.Equal(ChineseInitials.Of((string?)icon["name"]), (string?)row["search"]);
    }

    [Fact]
    public void FromAramMeta_TheIndividualItems_KeepTheSourceOrderAndUsage()
    {
        JToken[] sections = Sections(Page(AramMetaFixture.Detail()));

        var equipment = (JObject)sections.Single(section => (string?)section["layout"] == "equipment");
        Assert.Equal("builds", (string?)equipment["tab"]);
        Assert.Equal(new[] { 123430, 2503, 6655 }, RowIds(equipment));
        Assert.Equal("胜率 58.5%", (string?)equipment["rows"]![0]!["winRateText"]);
        Assert.Equal("使用率 1.6%", (string?)equipment["rows"]![0]!["pickRateText"]);
    }

    private static JObject Request(
        int slot,
        int championId,
        long stamp = Now,
        string id = OpaqueId,
        string session = Session
    ) =>
        new()
        {
            ["protocol"] = 1,
            ["id"] = id,
            ["session"] = session,
            ["updatedAt"] = stamp,
            ["slot"] = slot,
            ["championId"] = championId,
        };

    private static int[] Roster() => [1, 0, 0, 0, 0, 103, 0, 0, 0, 0];

    private static JObject AugmentRow(string name) =>
        new()
        {
            ["icons"] = new JArray(
                new JObject
                {
                    ["kind"] = "augment",
                    ["id"] = 1,
                    ["name"] = name,
                    ["sourceName"] = name,
                }
            ),
        };

    private static AramMetaPage Page(JObject? detail) =>
        new(AramMetaIndex.Parse(AramMetaFixture.Index()), detail, false, DateTime.UtcNow);

    private static JToken[] Sections(AramMetaPage page, int hero = AramMetaFixture.Ahri) =>
        ((JArray)OverlayChampionDetailService.FromAramMeta(page, hero)["sections"]!).ToArray();

    private static JToken[] Augments(JToken[] sections) =>
        sections.Where(section => (string?)section["tab"] == "augments").ToArray();

    private static string Title(JToken section) => (string)section["title"]!;

    private static int RowCount(JToken section) => ((JArray)section["rows"]!).Count;

    private static int[] RowIds(JToken section) =>
        ((JArray)section["rows"]!).Select(row => (int)row["icons"]![0]!["id"]!).ToArray();

    /// <summary>The captured hero pool, in the order arammeta lists it, filtered by the augment's own rarity.</summary>
    private static int[] PoolIds(string category)
    {
        JObject capture = AramMetaFixture.Json("103-augments");
        var augments = (JObject)capture["augs"]!;
        return ((JArray)capture["detail"]!["poolAugments"]!)
            .OfType<JObject>()
            .Where(row => (string?)augments[((int)row["id"]!).ToString()]?["rarity"] == category)
            .Select(row => (int)row["id"]!)
            .ToArray();
    }
}
