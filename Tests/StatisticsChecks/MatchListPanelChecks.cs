using System.Reflection;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// The match card that paints a whole game row by hand: the full and compact layouts, what a click on each part
/// of the row does, and the tooltips and cursors that follow the pointer.
/// </summary>
internal static class MatchListPanelChecks
{
    public static void Run() => WinFormsCheckHost.Run(Core);

    private static void Core()
    {
        Painting();
        ClickRouting();
        Hovering();
        Cleanup();
        Text();
        Console.WriteLine(
            "PASS: match card painting in both layouts, the augment layout, click routing, hover cursor and "
                + "tooltips, image ownership and the text helper."
        );
    }

    private static void Painting()
    {
        using var host = HiddenForm(new Size(1100, 400));
        using var match = Fixture();
        var card = new MatchListPanel(match) { Width = 1000, Height = 112 };
        host.Controls.Add(card);
        host.Show();

        var bitmap = new Bitmap(1000, 112);
        card.DrawToBitmap(bitmap, card.ClientRectangle);
        bitmap.Dispose();

        Check(DetailsRect(card) != Rectangle.Empty, "The full row lays out its details button");
        Check(ReplayRect(card) != Rectangle.Empty, "The full row lays out its replay button");
        Check(Hints(card).Count > 0, "The full row writes tooltips for the icons it draws");
        Check(PlayerRegions(card).Count >= 2, "The full row lays out a region for every player icon");

        card.Compact = true;
        card.DrawToBitmap(new Bitmap(1000, 112), card.ClientRectangle);
        Check(
            DetailsRect(card) == card.ClientRectangle,
            "The compact row turns the whole card into the details button"
        );
        card.Compact = false;

        using var mayhem = MayhemFixture();
        var augments = new MatchListPanel(mayhem) { Width = 1000, Height = 112 };
        host.Controls.Add(augments);
        augments.DrawToBitmap(new Bitmap(1000, 112), augments.ClientRectangle);
        Check(Hints(augments).Count > 0, "A Mayhem row with augments paints its icons too");

        using var empty = new MatchInfo();
        var bare = new MatchListPanel(empty) { Width = 1000, Height = 112 };
        host.Controls.Add(bare);
        bare.DrawToBitmap(new Bitmap(1000, 112), bare.ClientRectangle);
        Check(DetailsRect(bare) != Rectangle.Empty, "A row without data still lays itself out");
        host.Close();
    }

    private static void ClickRouting()
    {
        using var host = HiddenForm(new Size(1100, 400));
        using var match = Fixture();
        var card = new Probe(match) { Width = 1000, Height = 112 };
        host.Controls.Add(card);
        host.Show();
        card.DrawToBitmap(new Bitmap(1000, 112), card.ClientRectangle);

        MatchInfo? replayed = null;
        MatchInfo? detailed = null;
        string? clickedPlayer = null;
        card.ReplayClicked += info => replayed = info;
        card.DetailsClicked += info => detailed = info;
        card.PlayerIconClicked += id => clickedPlayer = id;

        card.ClickCenterOf(ReplayRect(card));
        Check(ReferenceEquals(replayed, match) && detailed == null, "Clicking the replay button asks for the replay");

        card.ClickCenterOf(DetailsRect(card));
        Check(ReferenceEquals(detailed, match), "Clicking the details button opens the detail view");

        Tuple<Rectangle, PlayerInfo> first = PlayerRegions(card)[0];
        card.ClickCenterOf(first.Item1);
        Check(clickedPlayer == first.Item2.SummonerId.ToString(), "Clicking a player icon reports that player");

        card.ClickCenterOf(new Rectangle(1, 1, 2, 2));
        Check(
            ReferenceEquals(detailed, match) && ReferenceEquals(replayed, match),
            "A click on empty space does nothing"
        );

        replayed = null;
        card.RequestReplay();
        Check(ReferenceEquals(replayed, match), "The replay can also be asked for directly");
        host.Close();
    }

    private static void Hovering()
    {
        using var host = HiddenForm(new Size(1100, 400));
        using var match = Fixture();
        var card = new Probe(match) { Width = 1000, Height = 112 };
        host.Controls.Add(card);
        host.Show();
        card.DrawToBitmap(new Bitmap(1000, 112), card.ClientRectangle);

        card.MoveCenterOf(PlayerRegions(card)[0].Item1);
        Check(card.Cursor == Cursors.Hand, "Hovering a player icon shows the hand cursor");

        card.MoveCenterOf(ReplayRect(card));
        Check(card.Cursor == Cursors.Hand, "Hovering the replay button shows the hand cursor");

        card.MoveTo(new Point(2, 106));
        Check(card.Cursor == Cursors.Default, "Hovering empty space shows the ordinary cursor");

        card.Leave();
        Check(card.Cursor == Cursors.Default, "Leaving the card clears the cursor");
        host.Close();
    }

    private static void Cleanup()
    {
        using var host = HiddenForm(new Size(1100, 400));
        using var match = Fixture();
        var card = new MatchListPanel(match) { Width = 1000, Height = 112 };
        host.Controls.Add(card);
        host.Show();

        var owned = new Bitmap(6, 6);
        card.TrackImage(owned);
        card.DisposeImages();
        Check(IsDisposed(owned), "An image handed to the card is disposed with the card's images");

        card.DisposeAllResources();
        card.DeepClean();
        card.DrawToBitmap(new Bitmap(1000, 112), card.ClientRectangle);
        Check(match.HeroIcon != null, "Cleaning the card does not touch the game it was given");

        card.Dispose(); // The card's own Dispose, not the one inherited from Control.
        host.Controls.Clear();
        host.Close();
    }

    private static void Text()
    {
        Check(MatchListPanel.StripHtmlTags("") == "", "An empty tooltip stays empty");
        Check(MatchListPanel.StripHtmlTags("<b>粗体</b>") == "粗体", "Tags are stripped from a tooltip");
        Check(MatchListPanel.StripHtmlTags("第一行<br>第二行") == "第一行\n第二行", "A line break becomes a newline");
        Check(
            MatchListPanel.StripHtmlTags(" <mainText>说明</mainText> ") == "说明",
            "Tooltips are trimmed and their markup removed"
        );
    }

    private static bool IsDisposed(Image image)
    {
        try
        {
            _ = image.Width;
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static Rectangle DetailsRect(MatchListPanel card) => (Rectangle)DetailsField.GetValue(card)!;

    private static Rectangle ReplayRect(MatchListPanel card) => (Rectangle)ReplayField.GetValue(card)!;

    private static List<(Rectangle Bounds, string Text)> Hints(MatchListPanel card) =>
        (List<(Rectangle, string)>)HintsField.GetValue(card)!;

    private static List<Tuple<Rectangle, PlayerInfo>> PlayerRegions(MatchListPanel card) =>
        (List<Tuple<Rectangle, PlayerInfo>>)RegionsField.GetValue(card)!;

    private static readonly FieldInfo DetailsField = typeof(MatchListPanel).GetField(
        "_detailsRect",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    private static readonly FieldInfo ReplayField = typeof(MatchListPanel).GetField(
        "_replayRect",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    private static readonly FieldInfo HintsField = typeof(MatchListPanel).GetField(
        "_imageHints",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    private static readonly FieldInfo RegionsField = typeof(MatchListPanel).GetField(
        "_playerRegions",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    /// <summary>Reaches the pointer handlers the card keeps to itself.</summary>
    private sealed class Probe(MatchInfo match) : MatchListPanel(match)
    {
        public void ClickCenterOf(Rectangle bounds) =>
            OnMouseClick(
                new MouseEventArgs(MouseButtons.Left, 1, bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2, 0)
            );

        public void MoveCenterOf(Rectangle bounds) =>
            OnMouseMove(
                new MouseEventArgs(MouseButtons.None, 0, bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2, 0)
            );

        public void MoveTo(Point at) => OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, at.X, at.Y, 0));

        public void Leave() => OnMouseLeave(EventArgs.Empty);
    }

    private static MatchInfo Fixture(string queueId = "q_450", string mode = "极地大乱斗") =>
        new()
        {
            Index = 3,
            GameId = 7412345678L,
            ResultText = "胜利",
            Mode = mode,
            QueueId = queueId,
            GameTime = "2026-09-08 01:54",
            DurationText = "22分5秒",
            Kills = 12,
            Deaths = 2,
            Assists = 18,
            GoldText = "12.3K |1",
            DamageText = "20K |1",
            KPPercentage = "75% |1",
            DamageTakenText = "15K |1",
            ChampionName = "拉克丝",
            ChampionDescription = "<b>光辉女郎</b>",
            HeroIcon = new Bitmap(8, 8),
            SpellNames = ["闪现", "治疗术"],
            SpellDescriptions = ["<br>位移", "回复"],
            SummonerSpells = [new Bitmap(4, 4), new Bitmap(4, 4)],
            Items = Items(),
            PrimaryRunes = Runes(4),
            SecondaryRunes = Runes(2),
            StatRunes = Runes(3),
            SelfPlayer = Player(7, "甲", true),
            BlueTeam = [Player(7, "甲", true), Player(9, "丙", false)],
            RedTeam = [Player(8, "乙", false)],
            AllParticipants =
            [
                Participant(100, 7, "甲", 12, 20000, 12345, 15000, 35, 0.75, true),
                Participant(200, 8, "乙", 3, 9000, 8000, 22000, 12, 0.4, false),
            ],
        };

    private static MatchInfo MayhemFixture()
    {
        var match = Fixture("q_2400", "海克斯大乱斗");
        match.Augments = Runes(2);
        return match;
    }

    private static Item[] Items() =>
        [
            new()
            {
                Id = 3157,
                Name = "中娅沙漏",
                Description = "<b>凝滞</b>",
                Icon = new Bitmap(4, 4),
            },
            new()
            {
                Id = 6655,
                Name = "卢登的伙伴",
                Description = "溅射",
                Icon = new Bitmap(4, 4),
            },
        ];

    private static RuneInfo[] Runes(int count) =>
        Enumerable
            .Range(1, count)
            .Select(id => new RuneInfo
            {
                id = id,
                RuneId = id,
                name = $"符文 {id}",
                longDesc = "<b>说明</b>",
                Icon = new Bitmap(4, 4),
            })
            .ToArray();

    private static PlayerInfo Player(long summonerId, string name, bool self) =>
        new()
        {
            SummonerId = summonerId,
            FullName = name,
            GameName = name,
            IsSelf = self,
            Avatar = new Bitmap(4, 4),
        };

    private static JObject Participant(
        int teamId,
        long summonerId,
        string name,
        int kills,
        int damage,
        int gold,
        int taken,
        int minions,
        double participation,
        bool win
    ) =>
        new()
        {
            ["teamId"] = teamId,
            ["summonerId"] = summonerId,
            ["riotIdGameName"] = name,
            ["championId"] = 1,
            ["kills"] = kills,
            ["deaths"] = 2,
            ["assists"] = 18,
            ["totalDamageDealtToChampions"] = damage,
            ["goldEarned"] = gold,
            ["totalDamageTaken"] = taken,
            ["totalMinionsKilled"] = minions,
            ["challenges"] = new JObject { ["killParticipation"] = participation },
            ["win"] = win,
        };
}
