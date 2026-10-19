using League.Controls;
using League.Models;
using League.Services;
using League.Tests.Support;

namespace League.Tests.Controls;

/// <summary>
/// The loading screen's own view: which team fills which column, the ten slot controls it keeps, the champion names
/// and per-player details it asks its two delegates for, the rank rates, the achievement identity check and the
/// borrowed artwork. The expected values come from the checks in Tests/StatisticsChecks/LoadingViewChecks.cs, which
/// this class replaces; the old check read <c>_teams</c>, <c>_details</c> and <c>_masteryTips</c> back through
/// reflection, and every one of those is reached here through the control tree instead.
/// The view is a <see cref="UserControl"/>, so every test puts it in the host window with a client area wider and
/// taller than the view: a host that clips its child never delivers the child's window messages.
/// The tier colour and the achievement tooltip are not asserted: both belong to helpers the public surface does not
/// reach (<c>AramMetaView.TierColor</c> is <c>internal</c>, and the view's <see cref="ToolTip"/> is never added to a
/// container, so nothing outside the class can ask it what it shows).
/// </summary>
[Trait("Category", "UI")]
public class LoadingOverlayViewTests
{
    private const int HostWidth = 1100;
    private const int HostHeight = 700;
    private const int PlayersPerTeam = 5;

    // The champion delegate records every id it is asked about, and the details delegate every player it is asked
    // about, so a test can tell "the view asked" apart from "the view hard-coded the text".
    private static readonly List<int> AskedForChampions = [];

    private static readonly List<string> AskedForDetails = [];

    [Fact]
    public void UpdateRoster_AConfinedSelfOnTheRedTeam_PutsTeam200InTheOwnColumn()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 200);

            view.UpdateRoster(roster, Ranks(roster.Players), Icons(), "已缓存");

            Assert.Equal(["p5", "p6", "p7", "p8", "p9"], OwnColumn(view).Select(player => player.Key));
        });
    }

    [Fact]
    public void UpdateRoster_AConfinedSelfOnTheBlueTeam_PutsTeam100InTheOwnColumn()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100);

            view.UpdateRoster(roster, Ranks(roster.Players), Icons(), "已缓存");

            Assert.Equal(["p0", "p1", "p2", "p3", "p4"], OwnColumn(view).Select(player => player.Key));
        });
    }

    [Fact]
    public void UpdateRoster_AnIncompleteRoster_KeepsTheUnusedSlots()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100) with
            {
                Players = [new LoadingPlayer("only", "玩家甲", "a", 7L, 99, 100, true)],
            };

            view.UpdateRoster(roster, Ranks(roster.Players), Icons(), "已缓存");

            Assert.Equal(1, Cards(view).Count(card => card.Tag is LoadingPlayer));
            Assert.Equal(10, Cards(view).Count);
        });
    }

    [Fact]
    public void UpdateRoster_NoConfirmedSelf_NamesBothSidesWithoutAssumingTheOwnColumn()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 0);

            view.UpdateRoster(roster, Ranks(roster.Players), Icons(), "等待玩家数据");

            Assert.Equal(["蓝方", "红方"], TeamHeaders(view));
        });
    }

    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public void UpdateRoster_AConfirmedSelf_NamesBothSidesAgainstTheOwnTeam(int selfTeam)
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: selfTeam);
            int other = selfTeam == 100 ? 200 : 100;

            view.UpdateRoster(roster, Ranks(roster.Players), Icons(), "已缓存");

            Assert.Equal([roster.TeamLabel(selfTeam), roster.TeamLabel(other)], TeamHeaders(view));
        });
    }

    [Fact]
    public void UpdateRoster_RepeatedUpdates_RetainAllTenSlotControls()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100);
            var ranks = Ranks(roster.Players);
            var icons = Icons();
            view.UpdateRoster(roster, ranks, icons, "已缓存");
            var retained = Cards(view);

            Reupdate(view, roster, ranks, icons, count: 100);

            Assert.Equal(10, Cards(view).Count);
            Assert.Equal(retained, Cards(view));
        });
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public void UpdateRoster_AKnownChampion_ShowsWhatTheChampionDelegateReturns(int champion)
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100);

            view.UpdateRoster(roster, Ranks(roster.Players), Icons(), "已缓存");

            Assert.Equal($"英雄 {champion}", ChampionLabel(view, $"p{champion - 1}").Text);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UpdateRoster_AChampionThatIsNotPickedYet_DoesNotAskTheDelegate(int champion)
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100) with
            {
                Players = [new LoadingPlayer("p0", "玩家甲", "a", 1L, champion, 100, true)],
            };

            view.UpdateRoster(roster, Ranks(roster.Players), Icons(), "已缓存");

            Assert.Empty(AskedForChampions);
            Assert.Single(LabelsWithin(Card(view, "p0")), label => label.Text == "英雄待确认");
        });
    }

    [Fact]
    public void UpdateRoster_APlayerWithoutAName_ShowsThePlaceholder()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100) with
            {
                Players = [new LoadingPlayer("p0", "  ", "a", 1L, 99, 100, true)],
            };

            view.UpdateRoster(roster, Ranks(roster.Players), Icons(), "已缓存");

            Assert.Single(LabelsWithin(Card(view, "p0")), label => label.Text == "玩家名称尚未提供");
        });
    }

    [Fact]
    public void UpdateRoster_TheConfinedSelf_MarksItsOwnCard()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100);

            view.UpdateRoster(roster, Ranks(roster.Players), Icons(), "已缓存");

            Assert.Single(LabelsWithin(Card(view, "p0")), label => label.Text == "p0 · 自己");
            Assert.DoesNotContain(LabelsWithin(Card(view, "p1")), label => label.Text == "p1 · 自己");
        });
    }

    [Theory]
    [InlineData(true, "胜 52.8% · 选 7.5%")]
    [InlineData(false, "统计读取中…")]
    public void UpdateRoster_APlayerWithAndWithoutTierStatistics_ReadsDifferently(bool known, string expected)
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100);
            var ranks = new Dictionary<string, PlayerChampionTier>();
            if (known)
            {
                ranks["p0"] = Tier("T4");
            }

            view.UpdateRoster(roster, ranks, Icons(), "已缓存");

            Assert.Equal(expected, RatesLabel(view, "p0").Text);
        });
    }

    [Fact]
    public void UpdateRoster_AMatchingMasteryRecord_ShowsItsOwnText()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100);
            var masteries = new Dictionary<string, PlayerChampionMastery> { ["p0"] = Mastery(1, 12345) };

            view.UpdateRoster(roster, Ranks(roster.Players), Icons(), "已缓存", masteries);

            Assert.Equal("成就 7级 · 1.2万", MasteryLabel(view, "p0").Text);
            Assert.Equal(masteries["p0"].Text, MasteryLabel(view, "p0").Text);
        });
    }

    [Fact]
    public void UpdateRoster_MasteryForAnotherChampion_NeverReachesTheNewChampion()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100);
            var ranks = Ranks(roster.Players);
            var icons = Icons();
            var masteries = new Dictionary<string, PlayerChampionMastery> { ["p0"] = Mastery(1, 12345) };
            view.UpdateRoster(roster, ranks, icons, "已缓存", masteries);

            masteries["p0"] = Mastery(99, 99999);
            view.UpdateRoster(roster, ranks, icons, "已更新", masteries);

            // The record is no longer about this slot's champion, so the slot waits for the right one instead.
            Assert.Equal("成就读取中…", MasteryLabel(view, "p0").Text);
        });
    }

    [Fact]
    public void UpdateRoster_AHiddenPlayer_DoesNotHideEveryoneElsesMastery()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100) with
            {
                Players =
                [
                    new LoadingPlayer("p0", "玩家甲", "a", 1L, 99, 100, true),
                    new LoadingPlayer("hidden", "隐藏玩家", "", 0L, 25, 200, false, true),
                ],
            };
            var masteries = new Dictionary<string, PlayerChampionMastery>
            {
                ["p0"] = Mastery(99, 12345),
                ["hidden"] = PlayerChampionMastery.Unavailable(25),
            };

            // The hidden player's own slot asks about champion 25, so its unavailable record is the one that lands.
            view.UpdateRoster(roster, Ranks(roster.Players), Icons(), "已缓存", masteries);

            Assert.Single(LabelsWithin(view), label => label.Text == "成就未公开");
            Assert.Single(LabelsWithin(Card(view, "hidden")), label => label.Text == "成就未公开");
        });
    }

    [Fact]
    public void UpdateRoster_AnEmptyRoster_RemovesEveryPlayerIconAndSlotText()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100);
            using var image = new Bitmap(48, 48);
            view.UpdateRoster(roster, Ranks(roster.Players), new Dictionary<int, Image> { [99] = image }, "已缓存");

            view.UpdateRoster(null, Ranks(roster.Players), Icons(), "等待新对局");

            Assert.Equal(0, Cards(view).Count(card => card.Tag is LoadingPlayer));
            Assert.Equal(0, IconsOf(view).Count(icon => icon.Image != null));
            Assert.Equal(0, LabelsWithin(view).Count(label => label.Text == "成就读取中…"));
        });
    }

    [Fact]
    public void UpdateRoster_AnEmptyRoster_ReportsThatTheHeroStatisticsAreMissing()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);

            view.UpdateRoster(null, new Dictionary<string, PlayerChampionTier>(), Icons(), "等待新对局");

            Assert.Equal(DetailsText(), Details(view).Text);
        });
    }

    [Fact]
    public void UpdateRoster_TierStatisticsAlreadyRead_ListsTheirSourceInTheDetails()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var ranks = new Dictionary<string, PlayerChampionTier> { ["p0"] = Tier("T2") };

            view.UpdateRoster(null, ranks, Icons(), "等待新对局");

            Assert.Equal(
                "点击玩家卡片，查看已有的段位与近期战绩。胜率和选用率属于英雄模式统计。\nOP.GG · 普通大乱斗",
                Details(view).Text
            );
        });
    }

    [Fact]
    public void UpdateRoster_AStatus_ReachesTheStatusLine()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100);

            view.UpdateRoster(roster, Ranks(roster.Players), Icons(), "已缓存 · 蓝方先手");

            Assert.Equal("已缓存 · 蓝方先手", Status(view).Text);
        });
    }

    [Fact]
    public void Click_AChampionLabelInsideAPlayerCard_ShowsThatPlayersDetails()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100);
            view.UpdateRoster(roster, Ranks(roster.Players), Icons(), "已缓存");
            var champion = ChampionLabel(view, "p7");

            WindowMessages.Click(champion, Centre(champion));

            Assert.Equal("p7 · 英雄 8\n详情p7\nOP.GG · 普通大乱斗", Details(view).Text);
        });
    }

    [Fact]
    public void Click_AChampionLabel_AsksBothDelegatesAboutThatPlayer()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100);
            view.UpdateRoster(roster, Ranks(roster.Players), Icons(), "已缓存");
            var champion = ChampionLabel(view, "p7");

            WindowMessages.Click(champion, Centre(champion));

            Assert.Equal(["p7"], AskedForDetails);
        });
    }

    [Fact]
    public void Click_ASlotThatHoldsNoPlayer_LeavesTheDetailsAlone()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100) with
            {
                Players = [new LoadingPlayer("p0", "玩家甲", "a", 1L, 99, 100, true)],
            };
            view.UpdateRoster(roster, Ranks(roster.Players), Icons(), "已缓存");
            var empty = EmptySlot(view);
            string before = Details(view).Text;

            WindowMessages.Click(empty, Centre(empty));

            Assert.Equal(before, Details(view).Text);
        });
    }

    [Fact]
    public void UpdateRoster_ALaterChampionForTheSelectedPlayer_FollowsThatPlayer()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100);
            var ranks = Ranks(roster.Players);
            var icons = Icons();
            view.UpdateRoster(roster, ranks, icons, "已缓存");
            var champion = ChampionLabel(view, "p7");
            WindowMessages.Click(champion, Centre(champion));
            var changed = roster with
            {
                Players =
                [
                    .. roster.Players.Select(player => player.Key == "p7" ? player with { ChampionId = 99 } : player),
                ],
            };

            view.UpdateRoster(changed, ranks, icons, "更新");

            Assert.Equal("p7 · 英雄 99\n详情p7\nOP.GG · 普通大乱斗", Details(view).Text);
        });
    }

    [Fact]
    public void UpdateRoster_ResettingTheRosterWhileAPlayerIsSelected_DropsTheSelection()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100);
            var ranks = Ranks(roster.Players);
            var icons = Icons();
            view.UpdateRoster(roster, ranks, icons, "已缓存");
            var champion = ChampionLabel(view, "p7");
            WindowMessages.Click(champion, Centre(champion));

            view.UpdateRoster(null, new Dictionary<string, PlayerChampionTier>(), icons, "等待新对局");

            Assert.Equal(DetailsText(), Details(view).Text);
        });
    }

    [Fact]
    public void UpdateRoster_ASlotIcon_BorrowsTheOwnersArtwork()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100);
            using var image = new Bitmap(48, 48);

            // The first slot's champion carries id 1, so its card is the one that borrows this image.
            view.UpdateRoster(roster, Ranks(roster.Players), new Dictionary<int, Image> { [1] = image }, "已缓存");

            Assert.Single(IconsOf(view), icon => ReferenceEquals(icon.Image, image));
        });
    }

    [Fact]
    public void UpdateRoster_AReplacedRoster_LeavesBorrowedArtworkToItsOwner()
    {
        UiHost.Run(host =>
        {
            using var view = View(host);
            var roster = Roster(teamOfSelf: 100);
            using var image = new Bitmap(48, 48);
            var icons = new Dictionary<int, Image> { [99] = image };
            view.UpdateRoster(roster, Ranks(roster.Players), icons, "已缓存");

            view.UpdateRoster(null, new Dictionary<string, PlayerChampionTier>(), icons, "等待");

            Assert.Equal(48, image.Width);
        });
    }

    [Fact]
    public void UpdateRoster_AfterTheViewIsClosed_IgnoresTheLateResult()
    {
        UiHost.Run(host =>
        {
            var view = View(host);
            var roster = Roster(teamOfSelf: 100);
            using var image = new Bitmap(48, 48);
            var icons = new Dictionary<int, Image> { [99] = image };
            view.UpdateRoster(roster, new Dictionary<string, PlayerChampionTier>(), icons, "已缓存");
            string before = Details(view).Text;
            // Disposing the view empties its control tree, so what the late call must not do is touch the shared
            // artwork or throw on a window that is gone.
            view.Dispose();

            view.UpdateRoster(roster, new Dictionary<string, PlayerChampionTier>(), icons, "过期结果");

            Assert.Equal(DetailsText(), before);
            Assert.Equal(48, image.Width);
        });
    }

    private static LoadingOverlayView View(Form host)
    {
        AskedForChampions.Clear();
        AskedForDetails.Clear();
        var view = new LoadingOverlayView(
            id =>
            {
                AskedForChampions.Add(id);
                return $"英雄 {id}";
            },
            player =>
            {
                AskedForDetails.Add(player.Key);
                return $"详情{player.Key}";
            }
        )
        {
            Dock = DockStyle.Fill,
        };
        host.ClientSize = new Size(HostWidth, HostHeight);
        host.Controls.Add(view);
        host.PerformLayout();
        return view;
    }

    // A roster of ten players: the first five are on team 100 and the last five on team 200, exactly one of them is
    // the confirmed self (on the team the case asks for), and champion ids are one above the index, so every card
    // names a different champion.
    private static LoadingRoster Roster(int teamOfSelf)
    {
        var players = new LoadingPlayer[2 * PlayersPerTeam];
        for (int index = 0; index < players.Length; index++)
        {
            int team = index < PlayersPerTeam ? 100 : 200;
            players[index] = new LoadingPlayer(
                $"p{index}",
                $"p{index}",
                $"puuid-{index}",
                index + 1L,
                index + 1,
                team,
                false
            );
        }

        int self =
            teamOfSelf == 100 ? 0
            : teamOfSelf == 200 ? PlayersPerTeam
            : -1;
        if (self >= 0)
        {
            players[self] = players[self] with { Self = true };
        }

        return new LoadingRoster("game", 450, "ARAM", players);
    }

    private static Dictionary<string, PlayerChampionTier> Ranks(IEnumerable<LoadingPlayer> players) =>
        players.ToDictionary(player => player.Key, _ => Tier("T2"));

    private static PlayerChampionTier Tier(string tier) =>
        new(tier, "胜 52.8%", "OP.GG\n普通大乱斗", 450, "none") { PickRate = "7.5%" };

    private static PlayerChampionMastery Mastery(int champion, long points) => new(champion, 7, points, null, true);

    private static Dictionary<int, Image> Icons() => new();

    private static void Reupdate(
        LoadingOverlayView view,
        LoadingRoster roster,
        IReadOnlyDictionary<string, PlayerChampionTier> ranks,
        IReadOnlyDictionary<int, Image> icons,
        int count
    )
    {
        for (int index = 0; index < count; index++)
        {
            view.UpdateRoster(roster, ranks, icons, "已缓存");
        }
    }

    /// <summary>The ten slot cards the view builds once and keeps.</summary>
    private static List<Panel> Cards(LoadingOverlayView view) => Teams(view).Controls.OfType<Panel>().ToList();

    /// <summary>What the own column holds, in the order the roster puts those players in.</summary>
    private static List<LoadingPlayer> OwnColumn(LoadingOverlayView view) =>
        Column(view, side: 0).Select(panel => (LoadingPlayer)panel.Tag!).ToList();

    private static List<Panel> Column(LoadingOverlayView view, int side) =>
        Enumerable
            .Range(0, PlayersPerTeam)
            .Select(row => Teams(view).GetControlFromPosition(side, row + 1))
            .OfType<Panel>()
            .ToList();

    private static List<string> TeamHeaders(LoadingOverlayView view) =>
        Enumerable.Range(0, 2).Select(side => Teams(view).GetControlFromPosition(side, 0)!.Text).ToList();

    private static TableLayoutPanel Teams(LoadingOverlayView view) => view.Controls.OfType<TableLayoutPanel>().Single();

    private static Panel EmptySlot(LoadingOverlayView view) => Cards(view).First(panel => panel.Tag == null);

    private static Panel Card(LoadingOverlayView view, string key) =>
        Cards(view).Single(panel => panel.Tag is LoadingPlayer player && player.Key == key);

    /// <summary>The one label inside a card that names the champion; the click target for the whole card.</summary>
    private static Label ChampionLabel(LoadingOverlayView view, string key, int? champion = null)
    {
        var card = Card(view, key);
        var player = (LoadingPlayer)card.Tag!;
        int named = champion ?? player.ChampionId;
        var label = LabelsWithin(card).Single(one => one.Text == $"英雄 {named}");
        // A child window is clipped to its parent, so a click off the card never reaches it: the test says that here
        // rather than reporting a missing selection later.
        Assert.True(card.ClientRectangle.Contains(card.PointToClient(label.PointToScreen(Point.Empty))));
        return label;
    }

    private static Label RatesLabel(LoadingOverlayView view, string key) =>
        LabelsWithin(Card(view, key)).Single(label => label.Text.Contains(" · 选 ") || label.Text == "统计读取中…");

    private static Label MasteryLabel(LoadingOverlayView view, string key) =>
        LabelsWithin(Card(view, key)).Single(label => label.Text.StartsWith("成就", StringComparison.Ordinal));

    private static List<PictureBox> IconsOf(LoadingOverlayView view) => Descendants(view).OfType<PictureBox>().ToList();

    // The view's two own labels are docked top and bottom; the docking that decides their places inside the grid is
    // the grid's, so they are told apart by where the layout put them, not by their own Dock property.
    private static Label Status(LoadingOverlayView view) => OwnLabels(view)[0];

    private static Label Details(LoadingOverlayView view) => OwnLabels(view)[^1];

    private static List<Label> OwnLabels(LoadingOverlayView view) =>
        view.Controls.OfType<Label>().OrderBy(label => label.Top).ToList();

    private static string DetailsText() =>
        "点击玩家卡片，查看已有的段位与近期战绩。胜率和选用率属于英雄模式统计。\n英雄统计尚未读取。";

    private static List<Label> LabelsWithin(Control parent) => Descendants(parent).OfType<Label>().ToList();

    private static Point Centre(Control control) => new(control.Width / 2, control.Height / 2);

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
}
