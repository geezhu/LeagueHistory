using League.Clients;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

internal static class RoomViewChecks
{
    public static void Run()
    {
        PlayerCardSurface();
        TeamGridSurface();
        QueueFilters();
        DetailHeaders();
        TeamObjectives();
        RecentRoomStatistics();
        RoomHistoryCache();
    }

    private static void PlayerCardSurface()
    {
        using var form = HiddenForm(new Size(850, 650));
        form.Show();
        using (var playerCard = new PlayerCardControl { Size = new Size(360, 480) })
        {
            form.Controls.Add(playerCard);
            playerCard.BringToFront();
            var rows = Enumerable
                .Range(0, 5)
                .Select(i =>
                {
                    var row = new ListViewItem { ForeColor = i % 2 == 0 ? Color.Green : Color.Red };
                    row.SubItems.AddRange(new[] { "海克斯大乱斗", "22/10/24", "2026-09-08 01:54" });
                    return row;
                })
                .ToList();
            playerCard.SetPlayerInfo("玩家#1234", "华贵铂金 II", "璀璨钻石 II", null!, "公开", rows, Color.SteelBlue);
            playerCard.SetRecentStatistics(
                new PlayerMatchInfo
                {
                    RecentMatches = new()
                    {
                        new()
                        {
                            Kills = 2,
                            Deaths = 1,
                            Assists = 2,
                        },
                        new()
                        {
                            Kills = 4,
                            Deaths = 3,
                            Assists = 4,
                        },
                    },
                    WinHistory = new() { true, false },
                }
            );
            var avatar = playerCard.SummaryPanel.Controls.Cast<Control>().Single(c => c.AccessibleName == "召唤师头像");
            avatar.GetType().GetMethod("SetProfile")!.Invoke(avatar, new object?[] { null, 108 });
            Application.DoEvents();
            Check(
                playerCard.ListViewControl.GetItemRect(0).Height is >= 30 and < 46,
                "Redesigned player card fits each match on one line"
            );
            using var bitmap = new Bitmap(360, 480);
            playerCard.DrawToBitmap(bitmap, new Rectangle(0, 0, 360, 480));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "player-card.png"));
        }
    }

    private static void TeamGridSurface()
    {
        using (var teamForm = new Form { ClientSize = new Size(900, 600) })
        using (
            var teamTable = new TableLayoutPanel
            {
                Size = new Size(900, 600),
                ColumnCount = 5,
                RowCount = 2,
            }
        )
        {
            for (int i = 0; i < 5; i++)
            {
                teamTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            }

            teamTable.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            teamTable.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            teamForm.Controls.Add(teamTable);
            typeof(PlayerCardControl)
                .Assembly.GetType("League.Controls.PlayerTeamsLayout")!
                .GetMethod("Attach")!
                .Invoke(null, new object[] { teamTable });
            var friendly = new PlayerCardControl { Dock = DockStyle.Fill };
            var opposing = new PlayerCardControl { Dock = DockStyle.Fill };
            friendly.lblSoloRank.Text = "未定级(0)";
            friendly.lblFlexRank.Text = "未定级";
            var friendlyHost = new Panel { Tag = true, Dock = DockStyle.Fill };
            friendlyHost.Controls.Add(friendly);
            var opposingHost = new Panel { Tag = false, Dock = DockStyle.Fill };
            opposingHost.Controls.Add(opposing);
            teamTable.Controls.Add(friendlyHost, 0, 0);
            teamTable.Controls.Add(opposingHost, 0, 1);
            teamForm.Show();
            Application.DoEvents();
            Check(
                friendly.SummaryPanel.Parent == friendly
                    && !friendly.lblSoloRank.Visible
                    && !friendly.lblFlexRank.Visible,
                "Grid cards keep summaries and hide unranked labels"
            );
            Check(
                opposingHost.Visible && friendlyHost.Visible && opposing.SummaryPanel.Parent == opposing,
                "Both teams remain visible together"
            );
            SparseRoomTransitions(teamForm, teamTable, friendly, friendlyHost, opposingHost);
            teamForm.ClientSize = new Size(1200, 800);
            PopulateTeamCards(teamTable, friendly);
            Application.DoEvents();
            Check(
                friendly.lblSoloRank.Visible && friendly.lblFlexRank.Visible,
                "Both ranked queues remain visible in card header"
            );
            friendly.ListViewControl.TopItem = friendly.ListViewControl.Items[10];
            Check(
                friendly.ListViewControl.TopItem.Index > 0
                    && teamTable
                        .Controls.Cast<Control>()
                        .Where(p => p != friendlyHost)
                        .SelectMany(p => p.Controls.OfType<PlayerCardControl>())
                        .All(c => c.ListViewControl.TopItem.Index == 0),
                "Card histories scroll independently"
            );
            using (var gridBitmap = new Bitmap(1200, 800))
            {
                teamForm.DrawToBitmap(gridBitmap, new Rectangle(0, 0, 1200, 800));
                gridBitmap.Save(Path.Combine(AppContext.BaseDirectory, "room-grid.png"));
            }
            teamForm.Close();
        }
    }

    private static void SparseRoomTransitions(
        Form teamForm,
        TableLayoutPanel teamTable,
        PlayerCardControl friendly,
        Panel friendlyHost,
        Panel opposingHost
    )
    {
        for (int i = 0; i < 12; i++)
        {
            teamForm.ClientSize = new Size(900 + i * 3, 600);
            Application.DoEvents();
        }
        Check(
            teamTable.ColumnCount == 5 && teamTable.RowCount == 2 && teamTable.Width >= 1100,
            "Sparse rooms retain ten stable grid slots"
        );
        teamTable.Controls.Remove(opposingHost);
        opposingHost.Dispose();
        Application.DoEvents();
        Check(friendlyHost.Visible, "Single player remains visible after another player leaves");
        var teamsLayout = typeof(PlayerCardControl).Assembly.GetType("League.Controls.PlayerTeamsLayout")!;
        var originalSurface = teamTable.Parent;
        teamForm.Controls.Clear(); // Connection status replaces the page contents.
        teamsLayout.GetMethod("Hide")!.Invoke(null, new object[] { teamTable });
        teamsLayout.GetMethod("Show")!.Invoke(null, new object[] { teamTable, teamForm });
        Application.DoEvents();
        Check(
            originalSurface?.Parent == teamForm && originalSurface.Visible && teamTable.Visible,
            "Entering room restores detached team surface after connection status clears the page"
        );
        Check(friendly.SummaryPanel.Parent == friendly, "Restored room retains player summary inside card");
    }

    private static void PopulateTeamCards(TableLayoutPanel teamTable, PlayerCardControl friendly)
    {
        for (int slot = 0; slot < 10; slot++)
        {
            var card = slot == 0 ? friendly : new PlayerCardControl { Dock = DockStyle.Fill };
            var matches = Enumerable
                .Range(0, 20)
                .Select(i =>
                {
                    var item = new ListViewItem { ForeColor = i % 2 == 0 ? Color.Green : Color.Red };
                    item.SubItems.AddRange(new[] { "海克斯大乱斗", "12/5/18", "2026-09-08" });
                    return item;
                })
                .ToList();
            card.SetPlayerInfo(
                $"玩家{slot + 1}#1234",
                "华贵铂金 II",
                "璀璨钻石 II",
                null!,
                "公开",
                matches,
                Color.SteelBlue
            );
            card.SetRecentStatistics(
                new PlayerMatchInfo
                {
                    RecentMatches = new()
                    {
                        new()
                        {
                            Kills = 12,
                            Deaths = 5,
                            Assists = 18,
                        },
                    },
                    WinHistory = new() { true },
                }
            );
            if (slot > 0)
            {
                var host = new Panel
                {
                    Tag = slot < 5,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(5),
                };
                host.Controls.Add(card);
                teamTable.Controls.Add(host, slot % 5, slot / 5);
            }
        }
    }

    private static void QueueFilters()
    {
        foreach (var queue in new[] { "400", "420", "430", "440", "450", "480", "490", "2400" })
        {
            Check(
                League.Parsers.PlayerMatchDataFetcher.GetQueueFilter(queue) == "q_" + queue,
                "Room history filters supported queue " + queue
            );
        }

        foreach (var queue in new[] { "", "0", "900", "1700", "700", "99999" })
        {
            Check(
                League.Parsers.PlayerMatchDataFetcher.GetQueueFilter(queue) == "",
                "Other room modes use global history " + queue
            );
        }
    }

    private static void DetailHeaders()
    {
        using (var headerMatch = new MatchInfo { AllParticipants = new(), RawGameData = new JObject() })
        using (var detailLayout = new MatchDetailView(headerMatch))
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var scoreboard = (Panel)typeof(MatchDetailView).GetField("_scoreboard", flags)!.GetValue(detailLayout)!;
            var section = new Panel();
            var playerHeader = new Button { Text = "玩家", Bounds = new Rectangle(58, 32, 180, 30) };
            var itemsHeader = new Button { Text = "技能 / 装备", Bounds = new Rectangle(242, 32, 182, 30) };
            section.Controls.Add(playerHeader);
            section.Controls.Add(itemsHeader);
            scoreboard.Controls.Add(section);
            var reflow = typeof(MatchDetailView).GetMethod("ReflowScoreboard", flags)!;
            foreach (var width in new[] { 900, 1300, 900 })
            {
                detailLayout.Size = new Size(width, 600);
                reflow.Invoke(detailLayout, null);
                Check(
                    !playerHeader.Bounds.IntersectsWith(itemsHeader.Bounds),
                    "Player and equipment headers do not overlap when resizing detail view"
                );
                Check(itemsHeader.Text == "技能 / 装备", "Equipment heading retains its full text");
            }
        }
    }

    private static void TeamObjectives()
    {
        using (
            var summaryMatch = new MatchInfo
            {
                QueueId = "420",
                AllParticipants = new()
                {
                    JObject.Parse("{\"teamId\":100,\"kills\":3,\"deaths\":2,\"assists\":8,\"goldEarned\":12345}"),
                },
                RawGameData = JObject.Parse(
                    "{\"teams\":[{\"teamId\":100,\"towerKills\":2,\"objectives\":{\"tower\":{\"kills\":3},\"dragon\":{\"kills\":0}}},{\"teamId\":200,\"towerKills\":9}]}"
                ),
            }
        )
        {
            var summary = TeamSummary.From(summaryMatch, 100);
            Check(
                summary.Kills == 3
                    && summary.Gold == 12345
                    && summary.Objectives.Single(o => o.Key == "tower").Count == 3,
                "Team summary uses correct team and modern objective fields"
            );
            Check(
                summary.Objectives.Single(o => o.Key == "dragon").Count == 0
                    && summary.Objectives.Single(o => o.Key == "baron").Count == null,
                "Team objectives distinguish missing values from zero"
            );
            var headerType = typeof(PlayerCardControl).Assembly.GetType("League.Controls.TeamSummaryHeader")!;
            using var tips = new ToolTip();
            using var header = (Control)
                Activator.CreateInstance(headerType, new object[] { "胜方 · 蓝队", Color.SeaGreen, summary, tips })!;
            header.Size = new Size(700, 32);
            using var screenshot = new Bitmap(700, 32);
            header.DrawToBitmap(screenshot, new Rectangle(0, 0, 700, 32));
            screenshot.Save(Path.Combine(AppContext.BaseDirectory, "team-summary.png"));
            summaryMatch.QueueId = "2400";
            Check(
                TeamSummary.From(summaryMatch, 100).Objectives.Length == 2,
                "Hextech ARAM hides inapplicable neutral objectives"
            );
            summaryMatch.AllParticipants[0].Remove("goldEarned");
            Check(
                TeamSummary.From(summaryMatch, 100).Gold == null,
                "Incomplete team gold is not presented as a complete sum"
            );
        }
    }

    private static void RecentRoomStatistics()
    {
        var roomStatisticsFixture = new PlayerMatchInfo
        {
            RecentMatches = new()
            {
                new()
                {
                    Kills = 2,
                    Deaths = 1,
                    Assists = 2,
                },
                new()
                {
                    Kills = 4,
                    Deaths = 3,
                    Assists = 4,
                },
            },
            WinHistory = new() { true, false },
        };
        var roomStatistics = RoomRecentStatistics.From(roomStatisticsFixture);
        Check(
            roomStatistics.Games == 2 && roomStatistics.Kda == 9 && roomStatistics.WinRate == 50,
            "Room statistics aggregate kills/deaths instead of averaging individual ratios"
        );
        Check(
            RoomRecentStatistics.From(new PlayerMatchInfo()).Kda == null
                && RoomRecentStatistics.From(new PlayerMatchInfo()).WinRate == null,
            "Empty room history has no fabricated statistics"
        );
        roomStatisticsFixture.RecentMatches = new()
        {
            new()
            {
                Kills = 1,
                Assists = 2,
                Deaths = 0,
            },
        };
        Check(RoomRecentStatistics.From(roomStatisticsFixture).Kda == 9, "Room KDA handles zero total deaths");
    }

    private static void RoomHistoryCache()
    {
        var roomCache = new League.Parsers.PlayerMatchCacheManager();
        var aramHistory = new League.Models.PlayerMatchInfo();
        roomCache.CachePlayerMatch(123, "q_450", aramHistory);
        Check(
            roomCache.TryGetPlayerMatch(123, "q_450", out var cachedHistory)
                && ReferenceEquals(cachedHistory, aramHistory),
            "Room cache reuses history only for the same mode"
        );
        Check(
            !roomCache.TryGetPlayerMatch(123, "q_2400", out _) && !roomCache.TryGetPlayerMatch(123, "", out _),
            "Switching to Hextech or global history cannot reuse ARAM history"
        );
        roomCache.ClearCache();
        Check(!roomCache.TryGetPlayerMatch(123, "q_450", out _), "Leaving room clears history cache");
    }
}
