using League.Models;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The champion detail page behind the Game Bar widget: which roster slot may request it, the arammeta and op.gg
/// recommendations it is built from, the search text of an augment the client has renamed, and the reply the widget
/// reads back. The expected values come from the checks in Tests/StatisticsChecks/OverlayChampionDetailChecks.cs,
/// which this class replaces. No request leaves the process: the arammeta captures under TestData/AramMeta are the
/// whole input, the op.gg statistics are built here, and the replies are written into a temporary folder.
/// </summary>
public class OverlayChampionDetailServiceTests : IDisposable
{
    private const string Session = "instance:match";
    private const string OpaqueId = "0f8fad5bd9cb469fa16570867728950e";
    private const long Now = 1_700_000_000_000;

    private readonly TempFolder _folder = new("league-detail");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Parse_AnEnemySlotWithoutAccountIdentifiers_ReturnsThatHero()
    {
        JObject request = Request(slot: 5, championId: 103);

        OverlayDetailRequest? parsed = OverlayDetailRequest.Parse(request, Session, SlotHeroes(), Now);

        Assert.Equal(5, parsed?.Slot);
        Assert.Equal(103, parsed?.ChampionId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30_000)]
    public void Parse_AStampAtTheEdgeOfTheThirtySecondWindow_IsAccepted(long age)
    {
        JObject request = Request(slot: 5, championId: 103, stamp: Now - age);

        OverlayDetailRequest? parsed = OverlayDetailRequest.Parse(request, Session, SlotHeroes(), Now);

        Assert.Equal(103, parsed?.ChampionId);
    }

    [Theory]
    [InlineData(30_001)]
    [InlineData(60_000)]
    [InlineData(-1)]
    public void Parse_StaleAndFutureStamps_CannotStartALoad(long age)
    {
        JObject request = Request(slot: 5, championId: 103, stamp: Now - age);

        Assert.Null(OverlayDetailRequest.Parse(request, Session, SlotHeroes(), Now));
    }

    [Theory]
    [InlineData("next-match")]
    [InlineData("")]
    public void Parse_ASessionThatIsNotTheCurrentGame_IsRejected(string session)
    {
        JObject request = Request(slot: 5, championId: 103, session: session);

        Assert.Null(OverlayDetailRequest.Parse(request, Session, SlotHeroes(), Now));
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

        Assert.Null(OverlayDetailRequest.Parse(request, Session, SlotHeroes(), Now));
    }

    [Fact]
    public void Parse_ReturningToRoster_CancelsTheSelection()
    {
        JObject request = Request(slot: -1, championId: 0);

        OverlayDetailRequest? parsed = OverlayDetailRequest.Parse(request, Session, SlotHeroes(), Now);

        Assert.Equal(-1, parsed?.Slot);
        Assert.Equal(0, parsed?.ChampionId);
    }

    [Fact]
    public void Parse_CancellingWithAHero_IsRejected()
    {
        JObject request = Request(slot: -1, championId: 103);

        Assert.Null(OverlayDetailRequest.Parse(request, Session, SlotHeroes(), Now));
    }

    [Theory]
    [InlineData("../../other")]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public void Parse_AnIdentifierThatIsNotOpaque_IsRejected(string id)
    {
        JObject request = Request(slot: 5, championId: 103, id: id);

        Assert.Null(OverlayDetailRequest.Parse(request, Session, SlotHeroes(), Now));
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

        Assert.Null(OverlayDetailRequest.Parse(request, Session, SlotHeroes(), Now));
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

        Assert.Null(OverlayDetailRequest.Parse(request, Session, SlotHeroes(), Now));
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

    [Theory]
    [InlineData(0, 0.5682, 0.0509)]
    [InlineData(1, 0.5549, 0.0628)]
    [InlineData(2, 0.5441, 0.0498)]
    public void FromAramMeta_EachRarity_CarriesNumericRatesForSorting(int index, double winRate, double pickRate)
    {
        JToken[] sections = Sections(Page(AramMetaFixture.Detail()));

        JObject row = (JObject)Augments(sections)[index]!["rows"]![0]!;
        Assert.Equal(winRate, (double)row["winRate"]!, 4);
        Assert.Equal(pickRate, (double)row["pickRate"]!, 4);
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

    [Theory]
    [InlineData("aram")]
    [InlineData("ranked")]
    [InlineData("urf")]
    [InlineData("nexus_blitz")]
    [InlineData("arena")]
    public void FromOpgg_OtherModes_NeverShowMayhemRecommendations(string mode)
    {
        JObject data = OverlayChampionDetailService.FromOpgg(Build(mode));

        JToken[] sections = ((JArray)data["sections"]!).ToArray();
        Assert.Contains(sections, section => (string?)section["tab"] == "builds");
        Assert.DoesNotContain(sections, section => (string?)section["tab"] == "augments");
    }

    [Fact]
    public void FromOpgg_TheDetails_CarryNoPlayerIdentifiers()
    {
        JObject data = OverlayChampionDetailService.FromOpgg(Build("aram"));

        Assert.DoesNotContain("puuid", data.ToString());
    }

    [Fact]
    public void FromOpgg_TheIndividualItems_ComeFirstFromThisHeroesSingleItemStatistics()
    {
        JObject data = OverlayChampionDetailService.FromOpgg(Build());

        JToken equipment = ((JArray)data["sections"]!)[0]!;
        Assert.Equal("equipment", (string?)equipment["layout"]);
        Assert.Equal(new[] { 6655, 4645, 6653 }, RowIds(equipment));
        Assert.Equal("胜率 54.4%", (string?)equipment["rows"]![0]!["winRateText"]);
        Assert.Equal("使用率 14.0%", (string?)equipment["rows"]![0]!["pickRateText"]);
    }

    [Fact]
    public void FromOpgg_ASingleItemWithoutItsOwnRate_UsesItsOwnWinCount()
    {
        JObject data = OverlayChampionDetailService.FromOpgg(Build());

        JObject row = (JObject)((JArray)data["sections"]!)[0]!["rows"]![2]!;

        Assert.Equal(6653, (int)row["icons"]![0]!["id"]!);
        Assert.Equal("胜率 60.0%", (string?)row["winRateText"]);
    }

    [Fact]
    public void FromOpgg_WithoutSingleItemStatistics_DoesNotFabricateRowsFromCombinations()
    {
        OpggBuild build = Build() with { Data = new JObject { ["core_items"] = CoreItems() } };

        JObject data = OverlayChampionDetailService.FromOpgg(build);

        JToken[] sections = ((JArray)data["sections"]!).ToArray();
        Assert.Equal(0, RowCount(sections[0]));
        Assert.Equal(2, RowCount(sections[1]));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(31, 30)]
    public void FromOpgg_MoreSingleItemStatisticsThanTheWidgetShows_KeepsThirty(int supplied, int shown)
    {
        JObject data = OverlayChampionDetailService.FromOpgg(BuildWithSingles(supplied));

        Assert.Equal(shown, RowCount(((JArray)data["sections"]!)[0]!));
    }

    [Fact]
    public void FromOpgg_LaterEquipment_ExcludesTheChosenCoreCombination()
    {
        JObject data = OverlayChampionDetailService.FromOpgg(Build());

        JToken cores = ((JArray)data["sections"]!)[1]!;
        Assert.Equal("核心出装 · 按使用率排序", (string?)cores["title"]);
        Assert.Equal(new[] { 6655, 4645 }, IconIds(cores["rows"]![0]!["icons"]!));
        Assert.Equal(new[] { 6653 }, IconIds(cores["rows"]![0]!["later"]!));
    }

    [Fact]
    public void FromOpgg_TheSpellSection_HighlightsTheMostUsedAndTheHighestWinPair()
    {
        JObject data = OverlayChampionDetailService.FromOpgg(Build());

        JToken spells = ((JArray)data["sections"]!).Single(section => (string?)section["title"] == "召唤师技能");
        Assert.Equal(
            new[] { "使用率最高", "胜率最高" },
            ((JArray)spells["rows"]!).Select(row => (string?)row["title"])
        );
        Assert.Equal(new[] { 4, 14 }, IconIds(spells["rows"]![0]!["icons"]!));
        Assert.Equal(new[] { 4, 21 }, IconIds(spells["rows"]![1]!["icons"]!));
    }

    [Fact]
    public void FromOpgg_TheRunes_AreSortedByUseAndCarryTheirHighlights()
    {
        JObject data = OverlayChampionDetailService.FromOpgg(Build());

        JToken runes = ((JArray)data["sections"]!).Single(section => (string?)section["tab"] == "runes");
        Assert.Equal(
            new[] { "方案 1 · 使用率最高", "方案 2 · 胜率最高" },
            ((JArray)runes["rows"]!).Select(row => (string?)row["title"])
        );
        Assert.Equal(9, ((JArray)runes["rows"]![0]!["icons"]!).Count);
    }

    [Fact]
    public void FromOpgg_TheSkillOrder_ReadsAsOneRow()
    {
        JObject data = OverlayChampionDetailService.FromOpgg(Build());

        JToken skills = ((JArray)data["sections"]!).Single(section => (string?)section["tab"] == "skills");
        Assert.Equal("1 → 2 → 3", (string?)skills["rows"]![0]!["title"]);
    }

    [Theory]
    [InlineData("aram", "none", "OP.GG · 16.17 · 全球 / 全段位")]
    [InlineData("ranked", "mid", "OP.GG · 16.17 · 全球 / 全段位 · 中路")]
    [InlineData("ranked", "support", "OP.GG · 16.17 · 全球 / 全段位 · 辅助")]
    [InlineData("ranked", "unknown", "OP.GG · 16.17 · 全球 / 全段位 · 参考分路")]
    public void FromOpgg_ASourceLine_NamesTheVersionAndTheRankedLane(string mode, string position, string expected)
    {
        JObject data = OverlayChampionDetailService.FromOpgg(Build(mode, position));

        Assert.Equal(expected, (string?)data["source"]);
    }

    [Fact]
    public async Task ReadDetailRequestAsync_ACurrentSelectionWithoutAccountIdentifiers_ReachesTheMainProcess()
    {
        DetailReply reply = await ExchangeAsync();

        Assert.Equal(0, reply.Request?.Slot);
        Assert.Equal(AramMetaFixture.Ahri, reply.Request?.ChampionId);
    }

    [Fact]
    public async Task PublishDetail_TheReply_IdentifiesTheRequestedHero()
    {
        DetailReply reply = await ExchangeAsync();

        Assert.Equal(reply.Request?.Id, (string?)reply.Reply["requestId"]);
        Assert.Equal(AramMetaFixture.Ahri, (int?)reply.Reply["championId"]);
    }

    [Fact]
    public async Task PublishDetail_EveryAugmentRow_SurvivesTheReplyChannel()
    {
        DetailReply reply = await ExchangeAsync();

        Assert.Equal(129, ((JArray)reply.Reply["sections"]!).Sum(RowCount));
    }

    [Fact]
    public async Task PublishDetail_TheWholePayload_FitsTheWidgetReplyLimit()
    {
        DetailReply reply = await ExchangeAsync();

        Assert.InRange(reply.Size, 1L, 4_000_000L - 1);
    }

    [Fact]
    public async Task PublishDetail_TheReply_CarriesNoPrivateRosterKeys()
    {
        DetailReply reply = await ExchangeAsync();

        // The control: the reply really is the detail page, so the absence of the keys is not an empty file.
        Assert.Equal($"arammeta · {AramMetaFixture.Patch} · 来源推荐顺序", (string?)reply.Reply["source"]);
        Assert.DoesNotContain("private-", reply.Text);
    }

    [Fact]
    public async Task PublishDetail_TheResponseWrite_LeavesNoTemporaryFile()
    {
        await ExchangeAsync();

        Assert.Empty(Directory.GetFiles(_folder.Root, "*.tmp"));
    }

    /// <summary>One hero's op.gg statistics as op.gg reports them, with the version the sources were captured from.</summary>
    private static OpggBuild Build(string mode = "aram", string position = "none") =>
        new(new OpggQuery("global", mode, "all", position, 103), "16.17", Data(), new JObject(), Runes());

    /// <summary>Single-item statistics in pick-rate order, for the widget's thirty-row cut.</summary>
    private static OpggBuild BuildWithSingles(int count) =>
        new(
            new OpggQuery("global", "aram", "all", "none", 103),
            "16.17",
            new JObject { ["last_items"] = ManySingles(count) },
            new JObject(),
            Runes()
        );

    /// <summary>
    /// Two items with their own win rate, one with only a win count, a core combination that must not become a
    /// single item, and a row without a usable id. Core items, spells and runes fill the remaining sections.
    /// </summary>
    private static JObject Data() =>
        new()
        {
            ["last_items"] = new JArray(
                Option([6655], winRate: 0.544, pickRate: 0.14, play: 7779),
                Option([4645], winRate: 0.52, pickRate: 0.09, play: 4000),
                Option([6653], win: 600, play: 1000, pickRate: 0.05),
                Option([6655, 4645], winRate: 0.99, pickRate: 0.5, play: 100),
                Option([0], winRate: 0.5, pickRate: 0.4, play: 10)
            ),
            ["core_items"] = CoreItems(),
            ["starter_items"] = new JArray(Option([1056], winRate: 0.5, pickRate: 0.4, play: 500)),
            ["boots"] = new JArray(Option([3020], winRate: 0.51, pickRate: 0.45, play: 600)),
            ["summoner_spells"] = new JArray(
                Option([4, 14], win: 500, winRate: 0.5, pickRate: 0.7, play: 1000),
                Option([4, 21], win: 300, winRate: 0.6, pickRate: 0.2, play: 500)
            ),
            ["skill_masteries"] = new JArray(new JObject { ["ids"] = new JArray(1, 2, 3) }),
        };

    private static JArray CoreItems() =>
        new(
            Option([6655, 4645], winRate: 0.99, pickRate: 0.5, play: 100),
            Option([3157, 3020], winRate: 0.55, pickRate: 0.3, play: 900)
        );

    private static JArray ManySingles(int count) =>
        new(
            Enumerable
                .Range(0, count)
                .Select(index =>
                    Option([6000 + index], winRate: 0.5, pickRate: 0.5 - (index * 0.01), play: 100 + index)
                )
        );

    private static JObject Option(
        int[] ids,
        double? winRate = null,
        double? pickRate = null,
        long? play = null,
        long? win = null
    ) =>
        new()
        {
            ["ids"] = new JArray(ids),
            ["win_rate"] = winRate,
            ["pick_rate"] = pickRate,
            ["play"] = play,
            ["win"] = win,
        };

    private static OpggRunes[] Runes() =>
        [
            new(8000, 8100, [8001, 8002, 8003, 8004, 8005, 8006, 8007, 8008, 8009], 100, 0.5) { PickRate = 0.2 },
            new(8200, 8300, [8201, 8202, 8203, 8204, 8205, 8206, 8207, 8208, 8209], 200, 0.6) { PickRate = 0.1 },
        ];

    private static int[] IconIds(JToken icons) => ((JArray)icons).Select(icon => (int)icon["id"]!).ToArray();

    /// <summary>A roster whose first slot, on the own team, holds the requested hero, with private account keys.</summary>
    private static LoadingRoster Roster() =>
        new("1", 450, "ARAM", [new("private-key", "测试玩家", "private-account", 1, 103, 100, true)]);

    /// <summary>The widget's detail exchange: the request the main process read and the reply file it wrote.</summary>
    private sealed record DetailReply(OverlayDetailRequest? Request, JObject Reply, string Text, long Size);

    /// <summary>
    /// Drives one whole exchange the way the widget does: publish the roster, ask for the selected hero's detail,
    /// answer it, and read back the file the widget parses. The bridge writes on pool threads, so the reply is only
    /// read once <see cref="GameBarBridge.Completion"/> has drained both channels.
    /// </summary>
    private async Task<DetailReply> ExchangeAsync()
    {
        OverlayDetailRequest? read = null;
        var bridge = new GameBarBridge(_folder.Root);
        try
        {
            bridge.Publish(
                Roster(),
                new Dictionary<string, PlayerChampionTier>(),
                new Dictionary<int, Image>(),
                champion: _ => "九尾妖狐",
                details: _ => "测试缓存战绩",
                session: "one",
                status: "当前对局",
                enabled: true,
                active: true,
                show: true
            );
            JObject snapshot = await ReadWhenWrittenAsync(_folder.File("snapshot.json"));
            var request = new JObject
            {
                ["protocol"] = 1,
                ["id"] = Guid.NewGuid().ToString("N"),
                ["session"] = snapshot["session"],
                ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["slot"] = 0,
                ["championId"] = AramMetaFixture.Ahri,
            };
            await File.WriteAllTextAsync(_folder.File("detail-request.json"), request.ToString());
            read = await bridge.ReadDetailRequestAsync();
            Assert.NotNull(read);
            JObject payload = OverlayChampionDetailService.FromAramMeta(
                Page((JObject)AramMetaFixture.Json("103-augments")["detail"]!),
                AramMetaFixture.Ahri
            );
            payload["state"] = "ready";
            bridge.PublishDetail(read!, payload);
        }
        finally
        {
            bridge.Dispose();
            await bridge.Completion;
        }

        string path = _folder.File("detail.json");
        string text = await File.ReadAllTextAsync(path);
        return new(read, JObject.Parse(text), text, new FileInfo(path).Length);
    }

    /// <summary>
    /// The snapshot write carries no completion signal of its own and the widget itself polls this file, so the test
    /// waits for it too. The deadline turns a bridge that never writes into a failure instead of a hung test.
    /// </summary>
    private static async Task<JObject> ReadWhenWrittenAsync(string path)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!File.Exists(path))
        {
            await Task.Delay(10, deadline.Token);
        }

        return JObject.Parse(await File.ReadAllTextAsync(path, deadline.Token));
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

    private static int[] SlotHeroes() => [1, 0, 0, 0, 0, 103, 0, 0, 0, 0];

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
