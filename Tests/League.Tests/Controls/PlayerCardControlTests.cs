using League.Controls;
using League.Models;
using League.Services;
using League.Tests.Support;

namespace League.Tests.Controls;

/// <summary>
/// The player card on its own: the identity header, the two ranked queues, the recent-game list, the history status
/// strip and the champion bar. The expected values come from the card checks in
/// Tests/StatisticsChecks/RoomViewChecks.cs and PlayerChampionChecks.cs, which this class replaces. The card is a
/// UserControl, so every test puts it in the host window, whose client area is wider than the card and taller than
/// it: a host that clips its child never delivers the child's window messages.
/// </summary>
[Trait("Category", "UI")]
public class PlayerCardControlTests
{
    private const int CardWidth = 360;
    private const int CardHeight = 480;
    private const int HostMargin = 20;
    private const int RankedHeaderHeight = 154;
    private const int UnrankedHeaderHeight = 120;
    private const int TwoLineRowHeight = 46;
    private const int SingleLineRowHeight = 30;
    private const int StatusStripHeight = 32;

    [Fact]
    public void SetPlayerInfo_PlayerName_ShowsTheShortName()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);

            card.SetPlayerInfo("玩家#1234", "华贵铂金 II", "璀璨钻石 II", null!, "公开", Rows(1), Color.Black);

            Assert.Equal("玩家#1234", card.lblPlayerName.Text);
        });
    }

    [Fact]
    public void SetPlayerInfo_PlayerName_MakesTheNameBold()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);

            card.SetPlayerInfo("玩家#1234", "华贵铂金 II", "璀璨钻石 II", null!, "公开", Rows(1), Color.Black);

            Assert.True(card.lblPlayerName.Font.Bold, "The player name is the header's bold line.");
        });
    }

    [Theory]
    [InlineData("华贵铂金 II(56)", "璀璨钻石 II(3)", "铂金 II·56", "钻石 II·3")]
    [InlineData("", "", "", "")]
    public void SetPlayerInfo_RankText_ShowsBothQueuesInShortForm(
        string solo,
        string flex,
        string shownSolo,
        string shownFlex
    )
    {
        UiHost.Run(host =>
        {
            var card = Card(host);

            card.SetPlayerInfo("玩家#1234", solo, flex, null!, "公开", Rows(1), Color.Black);

            Assert.Equal(shownSolo, card.lblSoloRank.Text);
            Assert.Equal(shownFlex, card.lblFlexRank.Text);
        });
    }

    [Fact]
    public void SetPlayerInfo_OnlyFlexRanked_CaptionsTheRankAsFlex()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);

            card.SetPlayerInfo("玩家#1234", "未定级", "璀璨钻石 II(3)", null!, "公开", Rows(1), Color.Black);

            Assert.Equal(["灵活"], VisibleLabels(card).Intersect(["单双", "灵活"]));
        });
    }

    [Fact]
    public void SetPlayerInfo_OnlyFlexRanked_StartsTheRankAtTheLeft()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);

            card.SetPlayerInfo("玩家#1234", "未定级", "璀璨钻石 II(3)", null!, "公开", Rows(1), Color.Black);

            Assert.True(
                card.lblFlexRank.Right < card.SummaryPanel.Width / 2,
                "With the solo queue unranked, the flex rank moves into the first slot instead of floating right."
            );
        });
    }

    [Theory]
    [InlineData("华贵铂金 II", "璀璨钻石 II", true, true)]
    [InlineData("未定级(0)", "璀璨钻石 II", false, true)]
    [InlineData("华贵铂金 II", "未知", true, false)]
    [InlineData("未定级", "未评级", false, false)]
    [InlineData("", "", false, false)]
    public void SetPlayerInfo_RankText_ShowsOnlyRankedQueues(string solo, string flex, bool soloShown, bool flexShown)
    {
        UiHost.Run(host =>
        {
            var card = Card(host);

            card.SetPlayerInfo("玩家#1234", solo, flex, null!, "公开", Rows(1), Color.Black);

            Assert.Equal(soloShown, card.lblSoloRank.Visible);
            Assert.Equal(flexShown, card.lblFlexRank.Visible);
        });
    }

    [Theory]
    [InlineData("华贵铂金 II", "璀璨钻石 II", RankedHeaderHeight)]
    [InlineData("华贵铂金 II", "未知", RankedHeaderHeight)]
    [InlineData("未定级", "未知", UnrankedHeaderHeight)]
    [InlineData("", "", UnrankedHeaderHeight)]
    public void SetPlayerInfo_RankText_ReservesHeaderSpaceForTheVisibleRanks(string solo, string flex, int height)
    {
        UiHost.Run(host =>
        {
            var card = Card(host);

            card.SetPlayerInfo("玩家#1234", solo, flex, null!, "公开", Rows(1), Color.Black);

            Assert.Equal(height, card.SummaryPanel.Height);
        });
    }

    [Fact]
    public void SetPlayerInfo_UnrankedPlayer_KeepsTheSummaryInsideTheCard()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);

            card.SetPlayerInfo("玩家#1234", "未定级(0)", "未定级", null!, "公开", Rows(1), Color.Black);

            Assert.Same(card, card.SummaryPanel.Parent);
            Assert.False(card.lblSoloRank.Visible);
            Assert.False(card.lblFlexRank.Visible);
        });
    }

    [Fact]
    public void SetPlayerInfo_Privacy_ShowsThePrivacyText()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);

            card.SetPlayerInfo("玩家#1234", "", "", null!, "仅好友可见", Rows(1), Color.Black);

            Assert.Equal("仅好友可见", card.lblPrivacyStatus.Text);
        });
    }

    [Theory]
    [InlineData(123456789L, 99, "puuid-99")]
    [InlineData(0L, 0, "")]
    public void SetPlayerInfo_Identity_KeepsTheSummonerChampionAndPuuId(long summonerId, int championId, string puuid)
    {
        UiHost.Run(host =>
        {
            var card = Card(host);

            card.SetPlayerInfo("玩家#1234", "", "", null!, "公开", Rows(1), Color.Black, summonerId, championId, puuid);

            Assert.Equal(summonerId, card.CurrentSummonerId);
            Assert.Equal(championId, card.CurrentChampionId);
            Assert.Equal(puuid, card.CurrentPuuId);
        });
    }

    [Theory]
    [InlineData(70, 130, 180)]
    [InlineData(0, 0, 0)]
    public void SetPlayerInfo_NameColor_PaintsEveryNameLinkState(int red, int green, int blue)
    {
        UiHost.Run(host =>
        {
            var card = Card(host);
            var color = Color.FromArgb(red, green, blue);

            card.SetPlayerInfo("玩家#1234", "", "", null!, "公开", Rows(1), color);

            Assert.Equal(color, card.lblPlayerName.LinkColor);
            Assert.Equal(color, card.lblPlayerName.VisitedLinkColor);
            Assert.Equal(color, card.lblPlayerName.ActiveLinkColor);
        });
    }

    [Fact]
    public void SetPlayerInfo_HeroImage_ShowsItInTheAvatarSlot()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);
            using var hero = new Bitmap(8, 8);

            card.SetPlayerInfo("玩家#1234", "", "", hero, "公开", Rows(1), Color.Black);

            Assert.Same(hero, card.picHero.Image);
        });
    }

    [Fact]
    public void SetAvatarOnly_Avatar_KeepsAnOwnedCopyOfTheImage()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);
            using var avatar = new Bitmap(9, 7);

            card.SetAvatarOnly(avatar);

            Assert.NotSame(avatar, card.picHero.Image);
            Assert.Equal(avatar.Size, card.picHero.Image!.Size);
        });
    }

    [Theory]
    [InlineData("加载中…", "华贵铂金 II", true)]
    [InlineData("玩家#1234", "加载中...", true)]
    [InlineData("玩家#1234", "华贵铂金 II", false)]
    public void SetPlayerInfo_LoadingMarker_SetsTheLoadingState(string playerName, string solo, bool loading)
    {
        UiHost.Run(host =>
        {
            var card = Card(host);

            card.SetPlayerInfo(playerName, solo, "", null!, "公开", [], Color.Black);

            Assert.Equal(loading, card.IsLoading);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(20)]
    public void SetPlayerInfo_CachedRows_ShowsOneRowPerGame(int games)
    {
        UiHost.Run(host =>
        {
            var card = Card(host);

            card.SetPlayerInfo("玩家#1234", "", "", null!, "公开", Rows(games), Color.Black);

            Assert.Equal(games, card.ListViewControl.Items.Count);
        });
    }

    [Fact]
    public void SetPlayerInfo_CachedRow_AddsACloneInsteadOfTheCachedItem()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);
            var cached = Rows(1)[0];

            card.SetPlayerInfo("玩家#1234", "", "", null!, "公开", [cached], Color.Black);

            var shown = card.ListViewControl.Items[0];
            Assert.NotSame(cached, shown);
            Assert.Equal(cached.SubItems[1].Text, shown.SubItems[1].Text);
            Assert.Equal(cached.SubItems[2].Text, shown.SubItems[2].Text);
            Assert.Null(cached.ListView);
        });
    }

    [Fact]
    public void SetPlayerInfo_SecondCall_ReplacesTheHistoryInsteadOfAppending()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);
            card.SetPlayerInfo("玩家#1234", "", "", null!, "公开", Rows(20), Color.Black);

            card.SetPlayerInfo("玩家#1234", "", "", null!, "公开", Rows(1), Color.Black);

            Assert.Single(card.ListViewControl.Items.Cast<ListViewItem>());
        });
    }

    [Fact]
    public void SetPlayerInfo_WithoutHistory_LeavesTheListEmpty()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);
            card.SetPlayerInfo("玩家#1234", "", "", null!, "公开", Rows(5), Color.Black);

            card.SetPlayerInfo("玩家#1234", "", "", null!, "公开", null!, Color.Black);

            Assert.Empty(card.ListViewControl.Items);
        });
    }

    [Fact]
    public void SetPlayerInfo_RecentGames_FitsEachGameOnOneLine()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);
            card.SetPlayerInfo("玩家#1234", "华贵铂金 II", "璀璨钻石 II", null!, "公开", Rows(5), Color.Black);

            Assert.InRange(card.ListViewControl.GetItemRect(0).Height, SingleLineRowHeight, TwoLineRowHeight - 1);
        });
    }

    [Fact]
    public void SetPlayerInfo_RecentGame_SpellsOutTheWholeGameOnHover()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);

            card.SetPlayerInfo("玩家#1234", "", "", null!, "公开", Rows(1), Color.Black);

            Assert.Equal("海克斯大乱斗 · 胜利 · 22/10/24 · 2026-09-01", card.ListViewControl.Items[0].ToolTipText);
        });
    }

    [Fact]
    public void ListViewControl_TwoCards_ScrollIndependently()
    {
        UiHost.Run(host =>
        {
            var (first, second) = TwoCards(host);
            first.SetPlayerInfo("玩家1#0001", "", "", null!, "公开", Rows(20), Color.Black);
            second.SetPlayerInfo("玩家2#0002", "", "", null!, "公开", Rows(20), Color.Black);

            first.ListViewControl.TopItem = first.ListViewControl.Items[10];

            Assert.True(first.ListViewControl.TopItem.Index > 0, "The scrolled card moved its own history list.");
            Assert.Same(second.ListViewControl.Items[0], second.ListViewControl.TopItem);
        });
    }

    [Fact]
    public void SetHistoryLoadStatus_Text_ShowsTheStatusAndStopsLoading()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);
            card.SetPlayerInfo("加载中…", "", "", null!, "未知", [], Color.Black);

            card.SetHistoryLoadStatus("读取超时", false, true, () => { });

            Assert.False(card.IsLoading, "A finished history read leaves the loading state.");
            Assert.Contains("读取超时", Labels(card));
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetHistoryLoadStatus_CanRetry_ShowsTheRetryButton(bool canRetry)
    {
        UiHost.Run(host =>
        {
            var card = Card(host);
            card.SetPlayerInfo("玩家#1234", "", "", null!, "公开", [], Color.Black);

            card.SetHistoryLoadStatus("读取超时", false, canRetry, () => { });

            Assert.Equal(canRetry, RetryButton(card).Visible);
        });
    }

    [Fact]
    public void SetHistoryLoadStatus_EmptyText_HidesTheStatusStrip()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);
            card.SetPlayerInfo("玩家#1234", "", "", null!, "公开", [], Color.Black);
            card.SetHistoryLoadStatus("读取超时", false, true, () => { });
            var strip = RetryButton(card).Parent!;

            card.SetHistoryLoadStatus("", false, false, () => { });

            Assert.False(strip.Visible);
        });
    }

    [Fact]
    public void SetHistoryLoadStatus_RetryClicked_AsksForTheHistoryAgain()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);
            card.SetPlayerInfo("玩家#1234", "", "", null!, "公开", [], Color.Black);
            int retries = 0;
            card.SetHistoryLoadStatus("读取超时", false, true, () => retries++);

            WindowMessages.Click(RetryButton(card), new Point(5, 5));

            Assert.Equal(1, retries);
        });
    }

    [Fact]
    public void SetHistoryLoadStatus_StatusStrip_ReservesItsOwnRowAboveTheHistoryList()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);
            card.SetPlayerInfo("玩家#1234", "华贵铂金 II", "璀璨钻石 II", null!, "公开", Rows(5), Color.Black);
            card.SetHistoryLoadStatus("读取超时", false, true, () => { });
            card.PerformLayout();
            var historyHost = card.ListViewControl.Parent!;
            var strip = RetryButton(card).Parent!;

            Assert.True(
                historyHost.Bottom <= strip.Top,
                "The native history list stops above the retry strip instead of covering it."
            );
            Assert.True(strip.Bottom <= card.ClientSize.Height, "The retry strip stays inside the card.");
            Assert.True(
                card.Controls.GetChildIndex(strip) > card.Controls.GetChildIndex(historyHost),
                "Docking the strip after the fill host is what reserves its row."
            );
        });
    }

    [Fact]
    public void SetHistoryLoadStatus_NoGames_FillsTheHistoryAreaBelowTheSummary()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);
            card.SetPlayerInfo("战绩读取失败", "", "", null!, "", [], Color.Black);

            card.SetHistoryLoadStatus("读取超时", false, true, () => { });
            card.PerformLayout();
            var strip = RetryButton(card).Parent!;

            Assert.Equal(card.SummaryPanel.Bottom, strip.Top);
            Assert.Equal(card.ClientSize.Height - card.Padding.Bottom, strip.Bottom);
        });
    }

    [Fact]
    public void SetHistoryLoadStatus_NoGames_CentresTheRetryButton()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);
            card.SetPlayerInfo("战绩读取失败", "", "", null!, "", [], Color.Black);

            card.SetHistoryLoadStatus("读取超时", false, true, () => { });
            card.PerformLayout();
            var retry = RetryButton(card);

            Assert.InRange(retry.Left + (retry.Width / 2) - (retry.Parent!.ClientSize.Width / 2), -1, 1);
        });
    }

    [Fact]
    public void SetPlayerInfo_GamesArrive_ShrinksTheStatusBackToAStrip()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);
            card.SetPlayerInfo("玩家#1234", "", "", null!, "公开", [], Color.Black);
            card.SetHistoryLoadStatus("读取超时", false, true, () => { });

            card.SetPlayerInfo("玩家#1234", "", "", null!, "公开", Rows(5), Color.Black);

            Assert.Equal(StatusStripHeight, RetryButton(card).Parent!.Height);
        });
    }

    [Fact]
    public void SetPlayerInfo_UnknownChampion_NamesTheHeroAndWaitsForTheTier()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);

            card.SetPlayerInfo("玩家#1234", "", "", null!, "公开", [], Color.Black, 0, 99);

            Assert.Contains("英雄 99", Labels(card));
            Assert.Contains("梯队待加载", Labels(card));
        });
    }

    [Fact]
    public void BindChampionInfo_LoadedTier_ShowsTheTierAndItsWinRate()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);
            card.SetPlayerInfo("玩家#1234", "", "", null!, "公开", [], Color.Black, 0, 99);

            card.BindChampionInfo(_ => Task.FromResult(Tier("T2", "胜 52.0%")), (_, _) => { });

            Assert.Contains("T2 · 胜 52.0%", Labels(card));
        });
    }

    [Fact]
    public void CurrentChampionId_ClearedSelection_RemovesTheOldTier()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);
            card.SetPlayerInfo("玩家#1234", "", "", null!, "公开", [], Color.Black, 0, 99);
            card.BindChampionInfo(_ => Task.FromResult(Tier("T2", "胜 52.0%")), (_, _) => { });
            var tier = Descendants(card).OfType<Label>().Single(label => label.Text == "T2 · 胜 52.0%");

            card.CurrentChampionId = 0;

            Assert.Equal("", tier.Text);
            Assert.Contains("尚未选择", Labels(card));
        });
    }

    [Fact]
    public void BindChampionInfo_RetiredAnswer_DoesNotOverwriteTheCurrentTier()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);
            var retired = new TaskCompletionSource<PlayerChampionTier>();
            var current = new TaskCompletionSource<PlayerChampionTier>();
            card.CurrentChampionId = 99;
            card.BindChampionInfo(id => id == 99 ? retired.Task : current.Task, (_, _) => { });
            card.CurrentChampionId = 25;

            // Both answers arrive on the interface thread while the card is waiting for them, the fresh one first:
            // the retired 99 answer must find that its generation is over.
            current.SetResult(Tier("T2", "胜 52.0%"));
            retired.SetResult(new PlayerChampionTier("T5", "胜 40.0%", "过期请求", 450, "none"));

            Assert.Contains("T2 · 胜 52.0%", Labels(card));
            Assert.DoesNotContain("T5 · 胜 40.0%", Labels(card));
        });
    }

    [Fact]
    public void ChampionBar_Click_OpensTheLoadedChampionTier()
    {
        UiHost.Run(host =>
        {
            var card = Card(host);
            card.SetPlayerInfo("玩家#1234", "", "", null!, "公开", [], Color.Black, 0, 99);
            var opened = new List<(int Id, PlayerChampionTier Tier)>();
            card.BindChampionInfo(_ => Task.FromResult(Tier("T2", "胜 52.0%")), (id, tier) => opened.Add((id, tier)));
            var heroName = Descendants(card).OfType<Label>().Single(label => label.Text == "英雄 99");

            WindowMessages.Click(heroName, new Point(5, 5));

            Assert.Single(opened);
            Assert.Equal(99, opened[0].Id);
            Assert.Equal(450, opened[0].Tier.Queue);
        });
    }

    private static PlayerChampionTier Tier(string tier, string rate) => new(tier, rate, "测试来源", 450, "none");

    // The host is made wider and taller than the card: a clipped child never receives its window messages.
    private static PlayerCardControl Card(Form host, int left = 0)
    {
        host.ClientSize = new Size((2 * CardWidth) + HostMargin, CardHeight + HostMargin);
        var card = new PlayerCardControl { Location = new Point(left, 0), Size = new Size(CardWidth, CardHeight) };
        host.Controls.Add(card);
        host.PerformLayout();
        return card;
    }

    private static (PlayerCardControl First, PlayerCardControl Second) TwoCards(Form host) =>
        (Card(host), Card(host, CardWidth + 10));

    private static List<ListViewItem> Rows(int games) => [.. Enumerable.Range(0, games).Select(Row)];

    private static ListViewItem Row(int index)
    {
        var row = new ListViewItem { ForeColor = index % 2 == 0 ? Color.Green : Color.Red };
        row.SubItems.AddRange(["海克斯大乱斗", "22/10/24", $"2026-09-{index + 1:00}"]);
        return row;
    }

    private static Button RetryButton(Control card) =>
        Descendants(card).OfType<Button>().Single(button => button.Text == "重试");

    private static IEnumerable<string> Labels(Control card) =>
        Descendants(card).OfType<Label>().Select(label => label.Text);

    private static IEnumerable<string> VisibleLabels(Control card) =>
        Descendants(card).OfType<Label>().Where(label => label.Visible).Select(label => label.Text);

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
}
