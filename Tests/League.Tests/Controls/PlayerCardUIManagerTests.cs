using League.Controls;
using League.Managers;
using League.Models;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Controls;

/// <summary>
/// The room's team grid: which card sits in which slot, which card object survives a re-render, and what a hidden or
/// failed player looks like. The expected values come from the card half of Tests/StatisticsChecks/RoomPlayerChecks.cs,
/// which this class replaces; the loader half of those checks is covered by RoomPlayerLoaderTests. The manager takes
/// an ordinary Form and its team table, so the grid runs without a client, a network read or FormMain.
/// </summary>
[Trait("Category", "UI")]
public class PlayerCardUIManagerTests
{
    private const int GridWidth = 1400;
    private const int GridHeight = 800;
    private const int TeamSize = 5;

    [Fact]
    public void Render_TenCompletedStates_ShowsTenCardsWithoutLoadingState()
    {
        UiHost.Run(host =>
        {
            var (manager, table) = Grid(host);

            manager.Render(Roster(Info));

            Assert.Equal(10, table.Controls.Count);
            Assert.False(Card(table, 0, 0).IsLoading, "A completed player is not shown as loading.");
        });
    }

    [Fact]
    public void Render_IncompleteTeam_OccupiesAllTenGridSlots()
    {
        UiHost.Run(host =>
        {
            var (manager, table) = Grid(host);

            manager.Render(Roster(_ => null));

            Assert.Equal(10, OccupiedSlots(table));
        });
    }

    [Fact]
    public void Render_BothTeams_KeepsEachSummaryInsideItsOwnCard()
    {
        UiHost.Run(host =>
        {
            var (manager, table) = Grid(host);

            manager.Render(Roster(Info));

            var ally = Card(table, 0, 0);
            var enemy = Card(table, 0, 1);
            Assert.True(ally.Visible && enemy.Visible, "Both teams remain visible together.");
            Assert.Same(ally, ally.SummaryPanel.Parent);
            Assert.Same(enemy, enemy.SummaryPanel.Parent);
        });
    }

    [Fact]
    public void Render_OnePlayer_KeepsTheFixedTwoByFiveGrid()
    {
        UiHost.Run(host =>
        {
            var (manager, table) = Grid(host);
            var single = State(1, true, 0, 1, Info(1));

            manager.Render([single]);

            Assert.Equal(5, table.ColumnCount);
            Assert.Equal(2, table.RowCount);
            Assert.True(table.Width >= 1100, "A sparse room keeps the grid wide enough for ten stable slots.");
        });
    }

    [Fact]
    public void Render_SwappedAllySlots_RetainsCardObjectsAndLeavesEnemiesInPlace()
    {
        UiHost.Run(host =>
        {
            var (manager, table) = Grid(host);
            var states = Roster(Info);
            manager.Render(states);
            var original = Card(table, 0, 0);
            var originalEnemy = Card(table, 0, 1);

            states[0] = states[0] with { Column = 1, Revision = 2, Player = Player(1, 99) };
            states[1] = states[1] with { Column = 0, Revision = 2 };
            manager.Render(states);

            Assert.Equal(10, table.Controls.Count);
            Assert.Same(original, Card(table, 1, 0));
            Assert.Same(originalEnemy, Card(table, 0, 1));
        });
    }

    [Fact]
    public void Render_HeroChange_RetainsTheLoadedHistory()
    {
        UiHost.Run(host =>
        {
            var (manager, table) = Grid(host);
            var states = Roster(Info);
            manager.Render(states);
            var card = Card(table, 0, 0);

            states[0] = states[0] with { Revision = 2, Player = Player(1, 99) };
            manager.Render(states);

            Assert.Equal(99, card.CurrentChampionId);
            Assert.Single(card.ListViewControl.Items.Cast<ListViewItem>());
        });
    }

    [Fact]
    public void Render_SharedCachedRows_AddsACloneToEachCard()
    {
        UiHost.Run(host =>
        {
            var (manager, table) = Grid(host);
            var states = Roster(Info);
            manager.Render(states);
            var shared = Info(20);

            states[0] = states[0] with { Info = shared, Revision = 2 };
            states[1] = states[1] with { Info = shared, Revision = 2 };
            manager.Render(states);

            var first = Card(table, 0, 0).ListViewControl.Items[0];
            var second = Card(table, 1, 0).ListViewControl.Items[0];
            Assert.NotSame(first, second);
            Assert.Equal(shared.MatchItems[0].SubItems[2].Text, first.SubItems[2].Text);
            Assert.Null(shared.MatchItems[0].ListView);
        });
    }

    [Fact]
    public void Render_RepeatedSnapshot_ReusesTheCardsWithoutDuplicatingThem()
    {
        UiHost.Run(host =>
        {
            var (manager, table) = Grid(host);
            var states = Roster(_ => null);
            manager.Render(states);
            var original = Card(table, 0, 0);
            var originalEnemy = Card(table, 0, 1);

            manager.Render(states);

            Assert.Equal(10, table.Controls.Count);
            Assert.Same(original, Card(table, 0, 0));
            Assert.Same(originalEnemy, Card(table, 0, 1));
        });
    }

    [Fact]
    public void Render_FailedPlayer_StopsLoadingAndOffersTheRetryButton()
    {
        UiHost.Run(host =>
        {
            var (manager, table) = Grid(host);
            var states = Roster(Info);

            states[2] = states[2] with { Info = null, CanRetry = true, Status = "读取超时", Revision = 2 };
            manager.Render(states);

            var failed = Card(table, 2, 0);
            Assert.False(failed.IsLoading, "A failed read has left the loading state.");
            Assert.True(RetryButton(failed).Visible, "A failed read offers a retry.");
        });
    }

    [Fact]
    public void Render_FailedPlayer_ReservesItsRowForTheRetryStatus()
    {
        UiHost.Run(host =>
        {
            var (manager, table) = Grid(host);
            var states = Roster(Info);
            states[2] = states[2] with { Info = null, CanRetry = true, Status = "读取超时", Revision = 2 };

            manager.Render(states);
            var failed = Card(table, 2, 0);
            failed.PerformLayout();
            var historyHost = failed.ListViewControl.Parent!;
            var strip = RetryButton(failed).Parent!;

            Assert.True(
                historyHost.Bottom <= strip.Top,
                "The native history list stops above the retry status instead of covering it."
            );
            Assert.True(strip.Bottom <= failed.ClientSize.Height, "The retry status stays inside the card.");
        });
    }

    [Fact]
    public void Render_RetryClicked_RequestsThatPlayerAndGeneration()
    {
        UiHost.Run(host =>
        {
            var (manager, table) = Grid(host);
            var requested = new List<(string Key, long Generation)>();
            manager.RetryRequested = (key, generation) => requested.Add((key, generation));
            var states = Roster(Info);
            states[2] = states[2] with { Info = null, CanRetry = true, Status = "读取超时", Revision = 2 };
            manager.Render(states);

            WindowMessages.Click(RetryButton(Card(table, 2, 0)), new Point(5, 5));

            Assert.Single(requested);
            Assert.Equal("ally/3", requested[0].Key);
            Assert.Equal(1, requested[0].Generation);
        });
    }

    [Fact]
    public void Render_DepartedPlayer_DisposesTheCompleteCard()
    {
        UiHost.Run(host =>
        {
            var (manager, table) = Grid(host);
            var states = Roster(Info);
            manager.Render(states);
            var departed = Card(table, 0, 0);

            manager.Render([.. states.Skip(1)]);

            Assert.Equal(9, table.Controls.Count);
            Assert.True(departed.IsDisposed, "The departed player's complete card is disposed.");
        });
    }

    [Fact]
    public void Render_HiddenPlayer_KeepsTheHeroAndMarksTheHistoryUnavailable()
    {
        UiHost.Run(host =>
        {
            var (manager, table) = Grid(host);

            manager.Render(HiddenRoster());

            var hidden = Card(table, 3, 1);
            Assert.Equal(10, OccupiedSlots(table));
            Assert.Equal("隐藏玩家", hidden.lblPlayerName.Text);
            Assert.False(hidden.IsLoading, "A hidden profile is a terminal state, not an unfinished read.");
            Assert.True(hidden.CurrentChampionId > 0, "A hidden player still carries the champion they picked.");
            Assert.Empty(hidden.ListViewControl.Items.Cast<ListViewItem>());
            Assert.Contains("客户端未提供玩家资料，战绩不可用", Labels(hidden));
        });
    }

    [Fact]
    public void Render_HiddenPlayer_KeepsTheChampionTier()
    {
        UiHost.Run(host =>
        {
            var (manager, table) = Grid(host);
            manager.Render(HiddenRoster());
            var hidden = Card(table, 3, 1);

            hidden.BindChampionInfo(
                _ => Task.FromResult(new PlayerChampionTier("T2", "胜 52.0%", "test", 2400, "none")),
                (_, _) => { }
            );

            Assert.Contains("T2 · 胜 52.0%", Labels(hidden));
        });
    }

    [Fact]
    public void Render_EmptyRoster_ClearsEveryCard()
    {
        UiHost.Run(host =>
        {
            var (manager, table) = Grid(host);
            manager.Render(Roster(Info));

            manager.Render([]);

            Assert.Empty(table.Controls.Cast<Control>());
        });
    }

    private static (PlayerCardUIManager Manager, TableLayoutPanel Table) Grid(Form host)
    {
        host.ClientSize = new Size(GridWidth, GridHeight);
        var table = new TableLayoutPanel { Dock = DockStyle.Fill };
        host.Controls.Add(table);
        host.PerformLayout();
        return (new PlayerCardUIManager(host, table), table);
    }

    private static PlayerCardControl Card(TableLayoutPanel table, int column, int row) =>
        table.GetControlFromPosition(column, row)!.Controls.OfType<PlayerCardControl>().Single();

    private static int OccupiedSlots(TableLayoutPanel table) =>
        Enumerable
            .Range(0, table.ColumnCount * table.RowCount)
            .Count(slot => table.GetControlFromPosition(slot % TeamSize, slot / TeamSize) != null);

    private static RoomPlayerState[] Roster(Func<long, PlayerMatchInfo?> info) =>
        [
            .. Enumerable.Range(1, TeamSize).Select(id => State(id, true, id - 1, 1, info(id))),
            .. Enumerable.Range(1, TeamSize).Select(id => State(id, false, id - 1, 1, info(id + TeamSize))),
        ];

    private static RoomPlayerState[] HiddenRoster() =>
        [
            .. Enumerable.Range(1, TeamSize).Select(id => State(id, true, id - 1, 1, Info(id))),
            .. Enumerable.Range(0, TeamSize).Select(index => HiddenState(index, 100 + index)),
        ];

    private static RoomPlayerState State(long id, bool ally, int column, long revision, PlayerMatchInfo? info) =>
        new(
            1,
            (ally ? "ally/" : "enemy/") + id,
            ally,
            column,
            Player(id),
            info,
            info == null ? "正在读取战绩…" : "",
            false,
            revision
        );

    private static RoomPlayerState HiddenState(int column, int championId) =>
        new(
            2,
            "enemy/selection-" + column,
            false,
            column,
            new JObject
            {
                ["summonerId"] = 0,
                ["puuid"] = "00000000-0000-0000-0000-000000000000",
                ["championId"] = championId,
                ["__roomSelectionOnly"] = true,
            },
            HiddenInfo(championId),
            "",
            false,
            10
        );

    private static JObject Player(long id, int championId = 0) =>
        new()
        {
            ["summonerId"] = id,
            ["puuid"] = "test-player-" + id,
            ["championId"] = championId,
            ["team"] = 2,
        };

    private static PlayerMatchInfo Info(long id) =>
        new()
        {
            Player = new PlayerInfo
            {
                SummonerId = id,
                Puuid = "test-player-" + id,
                GameName = "玩家 " + id,
                SoloRank = "白银 II",
                FlexRank = "未知",
                IsPublic = "公开",
                NameColor = Color.Black,
            },
            MatchItems = { new ListViewItem(["", "大乱斗", "3/2/5", "2026-09-17"]) },
        };

    private static PlayerMatchInfo HiddenInfo(int championId) =>
        new()
        {
            Player = new PlayerInfo
            {
                GameName = "隐藏玩家",
                ChampionId = championId,
                SoloRank = "隐藏",
                FlexRank = "隐藏",
                IsPublic = "隐藏",
                NameColor = Color.Gray,
            },
        };

    private static Button RetryButton(Control card) =>
        Descendants(card).OfType<Button>().Single(button => button.Text == "重试");

    private static IEnumerable<string> Labels(Control card) =>
        Descendants(card).OfType<Label>().Select(label => label.Text);

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
}
