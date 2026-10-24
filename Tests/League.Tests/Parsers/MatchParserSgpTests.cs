using System.Globalization;
using League.Controls;
using League.Infrastructure;
using League.Models;
using League.Parsers;
using League.Tests.Support;
using Newtonsoft.Json.Linq;
using static League.FormMain;

namespace League.Tests.Parsers;

/// <summary>
/// The SGP summary parser behind the history rows. The resource tables are left empty, so every champion, spell, rune,
/// item and augment lookup misses without reading a file or the network, and every value below is the parser's own.
/// The expected values come from the checks in Tests/StatisticsChecks/MatchParsingChecks.cs, which this class replaces.
/// The parser hands back a real card, so the tests run on the window thread.
/// </summary>
[Collection(EnvironmentCollection.Name)]
[Trait("Category", "UI")]
public class MatchParserSgpTests : IDisposable
{
    private const string SelfSummonerId = "12345";
    private const string SelfGameName = "测试玩家";
    private const string SelfTagLine = "0001";
    private const string SelfPuuid = "self-puuid-0001";
    private const long GameId = 7412345678L;
    private const long GameStart = 1767225600000L;

    private readonly ResourceLoading _previousResources = Globals.resLoading;

    public MatchParserSgpTests()
    {
        // An empty table: every lookup misses, so nothing here depends on a real asset or on a request going out.
        Globals.resLoading = new ResourceLoading();
    }

    public void Dispose() => Globals.resLoading = _previousResources;

    public static TheoryData<int, string> QueueModes =>
        new()
        {
            { 420, "单双排" },
            { 440, "灵活排位" },
            { 400, "匹配征召" },
            { 430, "匹配盲选" },
            { 450, "大乱斗" },
            { 900, "无限乱斗" },
            { 1900, "自选无限火力" },
            { 480, "快速模式" },
            { 1700, "斗魂竞技场" },
            { 2400, "海克斯乱斗" },
            { 2450, "经典海克斯乱斗" },
            { 1020, "克隆大作战" },
            { 1300, "极限闪击" },
            { 1400, "终极魔典" },
            { 700, "冠军杯赛" },
            { 830, "人机入门" },
            { 840, "人机新手" },
            { 850, "人机中等" },
            { 870, "人机入门" },
            { 880, "人机新手" },
            { 890, "人机中等" },
            { 2000, "新手教程1" },
            { 2010, "新手教程2" },
            { 2020, "新手教程3" },
            { 1090, "云顶匹配" },
            { 1100, "云顶排位" },
            { 1130, "云顶疾速" },
            { 1160, "云顶双人" },
            { 0, "自定义" },
            { 3100, "自定义峡谷" },
            { 3140, "训练模式" },
            { 3270, "自定义海克斯" },
            { 4310, "经典峡谷" },
            { 4320, "经典人机" },
            { 950, "末日人机" },
            { 960, "末日人机" },
        };

    [Theory]
    [MemberData(nameof(QueueModes))]
    public void QueueModeName_KnownQueue_ReturnsItsMode(int queue, string expected)
    {
        string? mode = MatchParserSgp.QueueModeName(queue);

        Assert.Equal(expected, mode);
    }

    [Fact]
    public void QueueModeName_QueueOutsideTheTable_ReturnsNothing()
    {
        string? mode = MatchParserSgp.QueueModeName(99999);

        Assert.Null(mode);
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_MissingGame_ReturnsTheEmptyPlaceholder()
    {
        UiHost.Run(async _ =>
        {
            Panel panel = await ParseAsync(null);

            Assert.Equal(new Size(0, 0), panel.Size);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_GameWithoutParticipants_ReturnsTheEmptyPlaceholder()
    {
        UiHost.Run(async _ =>
        {
            Panel panel = await ParseAsync(new JObject());

            Assert.Equal(new Size(0, 0), panel.Size);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_JsonFieldIsNotAnObject_ReturnsTheEmptyPlaceholder()
    {
        UiHost.Run(async _ =>
        {
            Panel panel = await ParseAsync(JObject.Parse("""{"json":5,"participants":[]}"""));

            Assert.Equal(new Size(0, 0), panel.Size);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_EmptyParticipantList_ReturnsTheEmptyPlaceholder()
    {
        UiHost.Run(async _ =>
        {
            var game = Doc(2400, "MATCHED_GAME", "KIWI");

            Panel panel = await ParseAsync(game);

            Assert.Equal(new Size(0, 0), panel.Size);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_InfoIsNotAnObject_ReturnsTheEmptyPlaceholder()
    {
        UiHost.Run(async _ =>
        {
            var game = new JObject { ["info"] = 5, ["participants"] = new JArray(Self()) };

            Panel panel = await ParseAsync(game);

            Assert.Equal(new Size(0, 0), panel.Size);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_SelfWithoutAPuuid_ReturnsTheEmptyPlaceholder()
    {
        UiHost.Run(async _ =>
        {
            var game = SelfGame();
            game["participants"]![0]!["puuid"] = null;

            Panel panel = await ParseAsync(game);

            Assert.Equal(new Size(0, 0), panel.Size);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_ParticipantsNestedUnderInfo_ReadsTheGame()
    {
        UiHost.Run(async _ =>
        {
            var game = new JObject
            {
                ["json"] = new JObject
                {
                    ["gameId"] = GameId,
                    ["info"] = new JObject
                    {
                        ["gameDuration"] = 600,
                        ["queueId"] = 450,
                        ["gameMode"] = "ARAM",
                        ["gameType"] = "MATCHED_GAME",
                        ["participants"] = new JArray(Self()),
                    },
                },
            };

            MatchInfo info = await RowOfAsync(game);

            Assert.Equal("10分0秒", info.DurationText);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_InfoBesideTheGameFields_ReadsTheGame()
    {
        UiHost.Run(async _ =>
        {
            var game = new JObject
            {
                ["info"] = new JObject
                {
                    ["gameDuration"] = 600,
                    ["queueId"] = 450,
                    ["gameMode"] = "ARAM",
                    ["gameType"] = "MATCHED_GAME",
                    ["participants"] = new JArray(Self()),
                },
            };

            MatchInfo info = await RowOfAsync(game);

            Assert.Equal("10分0秒", info.DurationText);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_SummaryRow_KeepsTheIndexItWasGiven()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await SelfRowAsync(index: 3);

            Assert.Equal(3, info.Index);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_SummaryRow_ReadsTheGameId()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await SelfRowAsync();

            Assert.Equal(GameId, info.GameId);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_SummaryRow_ReadsBothIdentitiesOfThePlayer()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await SelfRowAsync();

            Assert.Equal(SelfPuuid, info.CurrPuuid);
            Assert.Equal(SelfSummonerId, info.CurrSummonerId);
        });
    }

    [Theory]
    [InlineData(true, "胜利")]
    [InlineData(false, "失败")]
    public void ParseGameToPanelFromSgpAsync_SummaryRow_NamesTheResult(bool win, string expected)
    {
        UiHost.Run(async _ =>
        {
            var game = SelfGame();
            game["participants"]![0]!["win"] = win;

            MatchInfo info = await RowOfAsync(game);

            Assert.Equal(expected, info.ResultText);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_SummaryRow_ReadsTheDuration()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await SelfRowAsync();

            Assert.Equal("22分5秒", info.DurationText);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_WithoutADuration_ShowsZero()
    {
        UiHost.Run(async _ =>
        {
            var game = SelfGame();
            game.Remove("gameDuration");

            MatchInfo info = await RowOfAsync(game);

            Assert.Equal("0分0秒", info.DurationText);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_SummaryRow_WritesTheStartTimeInLocalTime()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await SelfRowAsync();

            DateTime shown = DateTime.ParseExact(info.GameTime, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

            Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(GameStart).LocalDateTime, shown);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_WithoutAStartTime_ShowsNoTime()
    {
        UiHost.Run(async _ =>
        {
            var game = SelfGame();
            game.Remove("gameStartTimestamp");

            MatchInfo info = await RowOfAsync(game);

            Assert.Equal("未知时间", info.GameTime);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_SummaryRow_ReadsTheScore()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await SelfRowAsync();

            Assert.Equal(10, info.Kills);
            Assert.Equal(2, info.Deaths);
            Assert.Equal(7, info.Assists);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_SummaryRow_ShowsTheQueueAsTheMode()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await SelfRowAsync();

            Assert.Equal("海克斯乱斗", info.Mode);
            Assert.Equal("q_2400", info.QueueId);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_SummaryRow_RanksDamageGoldAndKillParticipation()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await RowOfAsync(RankedGame());

            Assert.Equal("20K |2", info.DamageText);
            Assert.Equal("12K |2", info.GoldText);
            Assert.Equal("50% |2", info.KPPercentage);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_TiedBestDamage_IsRankedFirst()
    {
        UiHost.Run(async _ =>
        {
            var rival = Player("rival-puuid", "Bravo", "90001", teamId: 200);
            rival["totalDamageDealtToChampions"] = 20000;

            MatchInfo info = await RowOfAsync(Doc(2400, "MATCHED_GAME", "KIWI", Self(), rival));

            Assert.Equal("20K |1", info.DamageText);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_KillParticipationWithoutChallenges_UsesTheFlatField()
    {
        UiHost.Run(async _ =>
        {
            var rival = Player("rival-puuid", "Bravo", "90001", teamId: 200);
            rival.Remove("challenges");
            rival["killParticipation"] = 0.9;

            MatchInfo info = await RowOfAsync(Doc(2400, "MATCHED_GAME", "KIWI", Self(), rival));

            Assert.Equal("50% |2", info.KPPercentage);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_SummaryRow_ReadsTheDamageTaken()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await SelfRowAsync();

            Assert.Equal("15K |1", info.DamageTakenText);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_UnknownChampion_KeepsThePlaceholderName()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await SelfRowAsync();

            Assert.Equal("未知英雄", info.ChampionName);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_UnknownSummonerSpells_KeepThePlaceholderNames()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await SelfRowAsync();

            Assert.Equal("未知召唤师技能", info.SpellNames[0]);
            Assert.Equal("未知召唤师技能", info.SpellNames[1]);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_WithoutARunePage_StillHasThreeStatRunes()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await SelfRowAsync();

            Assert.Equal(3, info.StatRunes.Length);
            Assert.Null(info.StatRunes[0]);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_SummaryRow_KeepsItsOwnCopyOfTheGame()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await SelfRowAsync();

            Assert.Equal(GameId, info.RawGameData["gameId"]!.Value<long>());
            Assert.Equal(SelfPuuid, info.RawGameData["participants"]![0]!["puuid"]!.ToString());
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_SummaryRow_ReleasesTheGameDocumentItWasGiven()
    {
        UiHost.Run(async _ =>
        {
            var game = SelfGame();

            await ParseAsync(game);

            Assert.Empty(game);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_SelfPlayer_IsThePlayerTheRowIsAbout()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await SelfRowAsync();

            Assert.Equal(SelfGameName, info.SelfPlayer.FullName);
            Assert.True(info.SelfPlayer.IsSelf);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_GameNameWithoutATag_IsKeptWhole()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await RowOfAsync(SelfGame(), gameName: "Alpha");

            Assert.Equal("Alpha", info.SelfPlayer.FullName);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_SummaryRow_MarksUsInOurOwnTeam()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await RowOfAsync(SquadGame());

            Assert.Equal("Alpha", info.BlueTeam.Single(player => player.IsSelf).FullName);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_SummaryRow_SplitsBothTeamsInNameOrder()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await RowOfAsync(SquadGame());

            Assert.Equal(new[] { "Alpha", "Bravo" }, info.BlueTeam.Select(player => player.FullName));
            Assert.Equal(new[] { "Charlie", "Delta" }, info.RedTeam.Select(player => player.FullName));
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_SummaryRow_DropsPlayersOfAnyOtherTeam()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await RowOfAsync(SquadGame());

            Assert.Equal(4, info.BlueTeam.Count + info.RedTeam.Count);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_SummaryRow_ReadsTheSummonerIdOfEachTeammate()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await RowOfAsync(SquadGame());

            Assert.Equal(20003L, info.RedTeam[0].SummonerId);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_TeammateWithADifferentSummonerId_IsNotSelf()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await RowOfAsync(SquadGame());

            Assert.False(info.BlueTeam.Single(player => player.FullName == "Bravo").IsSelf);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_TeammateWithoutASummonerId_MatchesTheGameName()
    {
        UiHost.Run(async _ =>
        {
            var namesake = Player("namesake-puuid", SelfGameName, summonerId: null);

            MatchInfo info = await RowOfAsync(Doc(2400, "MATCHED_GAME", "KIWI", Self(), namesake));

            Assert.True(
                info.BlueTeam.Single(player => player.FullName == SelfGameName && player.SummonerId == 0).IsSelf
            );
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_TeammateWithoutAnyName_IsCalledUnknown()
    {
        UiHost.Run(async _ =>
        {
            var nameless = Player("nameless-puuid", name: null, summonerId: null);

            MatchInfo info = await RowOfAsync(Doc(2400, "MATCHED_GAME", "KIWI", Self(), nameless));

            Assert.Equal("Unknown", info.BlueTeam.Single(player => player.SummonerId == 0).FullName);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_TeammateWithoutAnyName_IsNotSelf()
    {
        UiHost.Run(async _ =>
        {
            var nameless = Player("nameless-puuid", name: null, summonerId: null);

            MatchInfo info = await RowOfAsync(Doc(2400, "MATCHED_GAME", "KIWI", Self(), nameless));

            Assert.False(info.BlueTeam.Single(player => player.SummonerId == 0).IsSelf);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_TeammateWithOnlyASummonerName_MatchesTheGameName()
    {
        UiHost.Run(async _ =>
        {
            var legacy = Player("legacy-puuid", name: null, summonerId: null);
            legacy["summonerName"] = SelfGameName;

            MatchInfo info = await RowOfAsync(Doc(2400, "MATCHED_GAME", "KIWI", Self(), legacy));

            Assert.True(info.BlueTeam.Single(player => player.SummonerId == 0).IsSelf);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_TeammateWithoutARiotName_UsesTheSummonerName()
    {
        UiHost.Run(async _ =>
        {
            var legacy = Player("legacy-puuid", name: null, summonerId: null);
            legacy["summonerName"] = "Bravo";

            MatchInfo info = await RowOfAsync(Doc(2400, "MATCHED_GAME", "KIWI", Self(), legacy));

            Assert.Equal("Bravo", info.BlueTeam.Single(player => player.SummonerId == 0).FullName);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_SummonerIdMismatch_FallsBackToTheRiotId()
    {
        UiHost.Run(async _ =>
        {
            var game = SelfGame();
            game["participants"]![0]!["summonerId"] = "999";

            MatchInfo info = await RowOfAsync(game, summonerId: "4321", gameName: SelfGameName);

            Assert.Equal(SelfPuuid, info.CurrPuuid);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_OnlyTheGameNameMatches_FindsThePlayer()
    {
        UiHost.Run(async _ =>
        {
            var game = SelfGame();
            game["participants"]![0]!["riotIdTagline"] = "0002";

            MatchInfo info = await RowOfAsync(game, summonerId: "4321");

            Assert.Equal(SelfPuuid, info.CurrPuuid);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_OnlyTheSummonerNameMatches_FindsThePlayer()
    {
        UiHost.Run(async _ =>
        {
            var game = SelfGame();
            game["participants"]![0]!["riotIdGameName"] = "Somebody";
            game["participants"]![0]!["summonerName"] = SelfGameName;

            MatchInfo info = await RowOfAsync(game, summonerId: "4321");

            Assert.Equal(SelfPuuid, info.CurrPuuid);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_APartialGameNameMatches_FindsThePlayer()
    {
        UiHost.Run(async _ =>
        {
            var game = SelfGame();
            game["participants"]![0]!["riotIdGameName"] = SelfGameName + "二号";

            MatchInfo info = await RowOfAsync(game, summonerId: "4321");

            Assert.Equal(SelfPuuid, info.CurrPuuid);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_NothingMatches_KeepsTheFirstParticipant()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await RowOfAsync(SquadGame(), summonerId: "4321", gameName: "Nobody");

            Assert.Equal(SelfPuuid, info.CurrPuuid);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_PlayerAugments_KeepOnlyTheFilledSlots()
    {
        UiHost.Run(async _ =>
        {
            var self = Self();
            self["playerAugment1"] = 0;
            self["playerAugment2"] = 1234;
            self["playerAugment6"] = 5678;

            MatchInfo info = await RowOfAsync(Doc(2400, "MATCHED_GAME", "KIWI", self));

            Assert.Equal(new[] { 1234, 5678 }, info.Augments.Select(augment => augment.id));
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_AugmentWithoutArtwork_SaysThePictureIsMissing()
    {
        UiHost.Run(async _ =>
        {
            var self = Self();
            self["playerAugment1"] = 1234;

            MatchInfo info = await RowOfAsync(Doc(2400, "MATCHED_GAME", "KIWI", self));

            Assert.Equal("Augment 1234", info.Augments[0].name);
            Assert.Contains("图片未加载", info.Augments[0].longDesc, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_TheList_LoadsOnlyTheOwnPlayersAugments()
    {
        // The row shows only its own augments; the detail view loads the other players' before drawing them.
        UiHost.Run(async _ =>
        {
            MatchInfo info = await RowOfAsync(SquadGame());

            Assert.Equal([SelfPuuid], info.AllPlayersAugments.Keys);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_ParticipantWithoutAPuuid_IsLeftOutOfTheAugmentTable()
    {
        UiHost.Run(async _ =>
        {
            var game = SelfGame();
            game["participants"]![0]!["playerAugment1"] = 1234;
            var stranger = Player("stranger-puuid", "Bravo", "20002");
            stranger.Remove("puuid");
            ((JArray)game["participants"]!).Add(stranger);

            MatchInfo info = await RowOfAsync(game);

            Assert.Equal(new[] { SelfPuuid }, info.AllPlayersAugments.Keys);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_Items_KeepOnlyTheFilledSlots()
    {
        UiHost.Run(async _ =>
        {
            var self = Self();
            self["item0"] = 1001;
            self["item1"] = 1002;
            self["item5"] = 1005;

            MatchInfo info = await RowOfAsync(Doc(2400, "MATCHED_GAME", "KIWI", self));

            Assert.Equal(new[] { 1001, 1002, 1005 }, info.Items.Select(item => item.Id));
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_PrimaryRunePage_KeepsAtMostFourRunes()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await RowOfAsync(RunedGame());

            Assert.Equal(new[] { 8005, 9101, 9104, 8014 }, info.PrimaryRunes.Select(rune => rune.id));
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_SecondaryRunePage_KeepsAtMostTwoRunes()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await RowOfAsync(RunedGame());

            Assert.Equal(new[] { 8226, 8210 }, info.SecondaryRunes.Select(rune => rune.id));
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_StatRunes_AreReadInPageOrder()
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await RowOfAsync(RunedGame());

            Assert.Equal(new[] { 5008, 5008, 5002 }, info.StatRunes.Select(rune => rune.id));
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_RuneSelectionWithoutAPerk_IsDropped()
    {
        UiHost.Run(async _ =>
        {
            var self = Self();
            self["perks"] = new JObject
            {
                ["statPerks"] = new JObject
                {
                    ["offense"] = 5008,
                    ["flex"] = 5008,
                    ["defense"] = 5002,
                },
                ["styles"] = new JArray(
                    new JObject { ["description"] = "primaryStyle", ["selections"] = Selections(8005, 0, 9101) }
                ),
            };

            MatchInfo info = await RowOfAsync(Doc(2400, "MATCHED_GAME", "KIWI", self));

            Assert.Equal(new[] { 8005, 9101 }, info.PrimaryRunes.Select(rune => rune.id));
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_StatRuneWithoutAnId_IsLeftEmpty()
    {
        UiHost.Run(async _ =>
        {
            var self = Self();
            self["perks"] = new JObject
            {
                ["statPerks"] = new JObject
                {
                    ["offense"] = 0,
                    ["flex"] = 5008,
                    ["defense"] = 5002,
                },
            };

            MatchInfo info = await RowOfAsync(Doc(2400, "MATCHED_GAME", "KIWI", self));

            Assert.Equal(new[] { 0, 5008, 5002 }, info.StatRunes.Select(rune => rune.id));
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_PerksWithoutStatPerks_StillHasThreeStatRunes()
    {
        UiHost.Run(async _ =>
        {
            var self = Self();
            self["perks"] = new JObject { ["styles"] = new JArray() };

            MatchInfo info = await RowOfAsync(Doc(2400, "MATCHED_GAME", "KIWI", self));

            Assert.Equal(3, info.StatRunes.Length);
            Assert.Null(info.StatRunes[0]);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_PerksWithoutStyles_HasNoRunePages()
    {
        UiHost.Run(async _ =>
        {
            var self = Self();
            self["perks"] = new JObject { ["statPerks"] = new JObject { ["offense"] = 5008 } };

            MatchInfo info = await RowOfAsync(Doc(2400, "MATCHED_GAME", "KIWI", self));

            Assert.Empty(info.PrimaryRunes);
            Assert.Empty(info.SecondaryRunes);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_PrimaryPageWithoutSelections_IsEmpty()
    {
        UiHost.Run(async _ =>
        {
            var self = Self();
            self["perks"] = new JObject
            {
                ["statPerks"] = new JObject { ["offense"] = 5008 },
                ["styles"] = new JArray(new JObject { ["description"] = "primaryStyle" }),
            };

            MatchInfo info = await RowOfAsync(Doc(2400, "MATCHED_GAME", "KIWI", self));

            Assert.Empty(info.PrimaryRunes);
            Assert.Empty(info.SecondaryRunes);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_WithoutASecondaryPage_HasNoSecondaryRunes()
    {
        UiHost.Run(async _ =>
        {
            var self = Self();
            self["perks"] = new JObject
            {
                ["statPerks"] = new JObject { ["offense"] = 5008 },
                ["styles"] = new JArray(
                    new JObject { ["description"] = "primaryStyle", ["selections"] = Selections(8005) }
                ),
            };

            MatchInfo info = await RowOfAsync(Doc(2400, "MATCHED_GAME", "KIWI", self));

            Assert.Equal(new[] { 8005 }, info.PrimaryRunes.Select(rune => rune.id));
            Assert.Empty(info.SecondaryRunes);
        });
    }

    [Theory]
    [InlineData(2400, "MATCHED_GAME", "KIWI", "海克斯乱斗", "q_2400")]
    [InlineData(450, "MATCHED_GAME", "ARAM", "大乱斗", "q_450")]
    [InlineData(0, "MATCHED_GAME", "", "自定义", "")]
    [InlineData(0, "CUSTOM_GAME", "", "自定义", "")]
    [InlineData(4310, "CUSTOM_GAME", "", "经典峡谷", "q_4310")]
    [InlineData(3270, "CUSTOM_GAME", "", "自定义海克斯", "q_3270")]
    [InlineData(99999, "CUSTOM_GAME", "JADE", "经典峡谷", "q_99999")]
    [InlineData(99999, "CUSTOM_GAME", "jade", "经典峡谷", "q_99999")]
    [InlineData(99999, "CUSTOM_GAME", "KIWI", "自定义海克斯", "q_99999")]
    [InlineData(99999, "CUSTOM_GAME", "PRACTICETOOL", "训练模式", "q_99999")]
    [InlineData(99999, "CUSTOM_GAME", "CLASSIC", "自定义峡谷", "q_99999")]
    [InlineData(99999, "CUSTOM_GAME", "ARAM", "自定义大乱斗", "q_99999")]
    [InlineData(99999, "CUSTOM_GAME", "CHERRY", "自定义海克斯", "q_99999")]
    [InlineData(99999, "CUSTOM_GAME", "TWISTED", "自定义", "q_99999")]
    [InlineData(99999, "MATCHED_GAME", "CLASSIC", "召唤师峡谷", "q_99999")]
    [InlineData(99999, "MATCHED_GAME", "ARAM", "大乱斗", "q_99999")]
    [InlineData(99999, "MATCHED_GAME", "CHERRY", "斗魂竞技场", "q_99999")]
    [InlineData(99999, "MATCHED_GAME", "JADE", "经典峡谷", "q_99999")]
    [InlineData(99999, "MATCHED_GAME", "URF", "无限乱斗", "q_99999")]
    [InlineData(99999, "MATCHED_GAME", "TUTORIAL", "新手教程", "q_99999")]
    [InlineData(99999, "MATCHED_GAME", "", "未知模式(99999)", "q_99999")]
    public void ParseGameToPanelFromSgpAsync_QueueAndGameType_NameTheMode(
        int queueId,
        string gameType,
        string gameMode,
        string expectedMode,
        string expectedQueue
    )
    {
        UiHost.Run(async _ =>
        {
            MatchInfo info = await RowOfAsync(Doc(queueId, gameType, gameMode, Self()));

            Assert.Equal(expectedMode, info.Mode);
            Assert.Equal(expectedQueue, info.QueueId);
        });
    }

    [Fact]
    public void ParseGameToPanelFromSgpAsync_ClickingAnEnemyIcon_ReportsThatSummonerId()
    {
        UiHost.Run(async host =>
        {
            var parser = new MatchParserSgp();
            string? reported = null;
            parser.PlayerIconClicked += id => reported = id;
            host.ClientSize = new Size(1040, 300);
            var game = Doc(2400, "MATCHED_GAME", "KIWI", Self(), Player("enemy-puuid", "Charlie", "20003", 200));

            var card = (MatchListPanel)
                await parser.ParseGameToPanelFromSgpAsync(game, SelfSummonerId, SelfGameName, SelfTagLine, 0);
            host.Controls.Add(card);
            Repaint(card);

            WindowMessages.Click(card, new Point(1000 - 148 + 12, 18 + 12));

            Assert.Equal("20003", reported);
        });
    }

    private static Task<Panel> ParseAsync(
        JObject? game,
        int index = 0,
        string summonerId = SelfSummonerId,
        string gameName = SelfGameName,
        string tagLine = SelfTagLine
    ) => new MatchParserSgp().ParseGameToPanelFromSgpAsync(game!, summonerId, gameName, tagLine, index);

    private static async Task<MatchInfo> SelfRowAsync(int index = 0) => await RowOfAsync(SelfGame(), index);

    private static async Task<MatchInfo> RowOfAsync(
        JObject game,
        int index = 0,
        string summonerId = SelfSummonerId,
        string gameName = SelfGameName,
        string tagLine = SelfTagLine
    )
    {
        var card = (MatchListPanel)await ParseAsync(game, index, summonerId, gameName, tagLine);
        return card.MatchInfo;
    }

    private static Bitmap Repaint(MatchListPanel card)
    {
        var painted = new Bitmap(card.Width, card.Height);
        card.DrawToBitmap(painted, new Rectangle(Point.Empty, card.Size));
        return painted;
    }

    private static JObject SelfGame() => Doc(2400, "MATCHED_GAME", "KIWI", Self());

    private static JObject SquadGame() =>
        Doc(
            450,
            "MATCHED_GAME",
            "ARAM",
            Player(SelfPuuid, "Alpha", SelfSummonerId),
            Player("ally-puuid", "Bravo", "20002"),
            Player("enemy-puuid-1", "Charlie", "20003", 200),
            Player("enemy-puuid-2", "Delta", "20004", 200),
            Player("spectator-puuid", "Echo", "20005", 300)
        );

    private static JObject RankedGame()
    {
        var strong = Player("rival-puuid", "Bravo", "90001", 200);
        strong["totalDamageDealtToChampions"] = 30000;
        strong["goldEarned"] = 20000;
        strong["challenges"] = new JObject { ["killParticipation"] = 0.7 };
        var weak = Player("rookie-puuid", "Charlie", "90002", 200);
        weak["totalDamageDealtToChampions"] = 10000;
        weak["goldEarned"] = 5000;
        weak["challenges"] = new JObject { ["killParticipation"] = 0.2 };
        return Doc(2400, "MATCHED_GAME", "KIWI", Self(), strong, weak);
    }

    private static JObject RunedGame()
    {
        var self = Self();
        self["perks"] = new JObject
        {
            ["statPerks"] = new JObject
            {
                ["offense"] = 5008,
                ["flex"] = 5008,
                ["defense"] = 5002,
            },
            ["styles"] = new JArray(
                new JObject
                {
                    ["description"] = "primaryStyle",
                    ["selections"] = Selections(8005, 9101, 9104, 8014, 8226),
                },
                new JObject { ["description"] = "subStyle", ["selections"] = Selections(8226, 8210, 8304) }
            ),
        };
        return Doc(2400, "MATCHED_GAME", "KIWI", self);
    }

    private static JArray Selections(params int[] perks) =>
        new(perks.Select(perk => (object)new JObject { ["perk"] = perk }));

    private static JObject Doc(int queueId, string gameType, string gameMode, params JObject[] participants) =>
        new()
        {
            ["gameId"] = GameId,
            ["gameDuration"] = 1325,
            ["gameStartTimestamp"] = GameStart,
            ["queueId"] = queueId,
            ["gameMode"] = gameMode,
            ["gameType"] = gameType,
            ["participants"] = new JArray(participants),
        };

    private static JObject Self() => Player(SelfPuuid, SelfGameName, SelfSummonerId);

    private static JObject Player(string puuid, string? name, string? summonerId, int teamId = 100)
    {
        var player = new JObject
        {
            ["puuid"] = puuid,
            ["riotIdTagline"] = SelfTagLine,
            ["teamId"] = teamId,
            ["win"] = true,
            ["kills"] = 10,
            ["deaths"] = 2,
            ["assists"] = 7,
            ["goldEarned"] = 12000,
            ["totalDamageDealtToChampions"] = 20000,
            ["totalDamageTaken"] = 15000,
            ["championId"] = 103,
            ["challenges"] = new JObject { ["killParticipation"] = 0.5 },
        };
        IfPresent(player, "riotIdGameName", name);
        IfPresent(player, "summonerId", summonerId);
        return player;
    }

    /// <summary>
    /// A field the game never sent is left out of the document. Writing a null token instead is not the same thing:
    /// the parser reads the number fields through <c>Value&lt;long&gt;()</c>, which throws on a null token, and the
    /// player is then dropped from both team lists.
    /// </summary>
    private static void IfPresent(JObject player, string field, string? value)
    {
        if (value != null)
        {
            player[field] = value;
        }
    }
}
