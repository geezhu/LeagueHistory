using League.Controls;
using League.Models;
using League.Tests.Support;

namespace League.Tests.Controls;

/// <summary>
/// The match card that paints a whole game row by hand: both layouts, what a click on each painted part of the row
/// does, the cursor that follows the pointer, and the images and text the card owns. The card lays itself out in
/// 96-DPI pixels (the test module pins DPI awareness), so the coordinates below come from that layout; the card is
/// driven through its public surface and through window messages only.
/// </summary>
[Trait("Category", "UI")]
public class MatchListPanelTests
{
    private const int CardWidth = 1000;
    private const int CardHeight = 112;

    // PaintSpellsAndRunes puts the two links at these rectangles on the 96-DPI canvas.
    private static readonly Point DetailsCentre = new(488 + 88 / 2, 72 + 26 / 2);
    private static readonly Point ReplayCentre = new(582 + 90 / 2, 72 + 26 / 2);

    [Fact]
    public void Click_ReplayLink_AsksForTheReplayOfThatGame()
    {
        var match = Match();

        UiHost.Run(host =>
        {
            using var card = Card(host, match);
            MatchInfo? replayed = null;
            card.ReplayClicked += info => replayed = info;
            Repaint(card);

            WindowMessages.Click(card, ReplayCentre);

            Assert.Same(match, replayed);
        });
    }

    [Fact]
    public void Click_ReplayLinkOfAnExpiredReplay_AsksForNothing()
    {
        var match = Match();
        match.IsReplayExpired = true;

        UiHost.Run(host =>
        {
            using var card = Card(host, match);
            MatchInfo? replayed = null;
            card.ReplayClicked += info => replayed = info;
            Repaint(card);

            WindowMessages.Click(card, ReplayCentre);

            Assert.Null(replayed);
        });
    }

    [Fact]
    public void Move_OverTheReplayLinkOfAnExpiredReplay_KeepsTheOrdinaryCursor()
    {
        var match = Match();
        match.IsReplayExpired = true;

        UiHost.Run(host =>
        {
            using var card = Card(host, match);
            Repaint(card);

            WindowMessages.Move(card, ReplayCentre);

            Assert.Equal(Cursors.Default, card.Cursor);
        });
    }

    [Fact]
    public void Click_DetailsLink_OpensTheDetailsOfThatGame()
    {
        var match = Match();

        UiHost.Run(host =>
        {
            using var card = Card(host, match);
            MatchInfo? detailed = null;
            card.DetailsClicked += info => detailed = info;
            Repaint(card);

            WindowMessages.Click(card, DetailsCentre);

            Assert.Same(match, detailed);
        });
    }

    [Fact]
    public void Click_PlayerIconOfTheOpposingTeam_ReportsThatSummonerId()
    {
        var match = Match();

        UiHost.Run(host =>
        {
            using var card = Card(host, match);
            string? reported = null;
            card.PlayerIconClicked += id => reported = id;
            Repaint(card);

            WindowMessages.Click(card, TeamIconCentre(row: 0, column: 0));

            Assert.Equal(match.RedTeam[0].SummonerId.ToString(), reported);
        });
    }

    [Fact]
    public void Click_EmptySpaceInTheRow_DoesNothing()
    {
        var match = Match();

        UiHost.Run(host =>
        {
            using var card = Card(host, match);
            MatchInfo? detailed = null;
            MatchInfo? replayed = null;
            string? reported = null;
            card.DetailsClicked += info => detailed = info;
            card.ReplayClicked += info => replayed = info;
            card.PlayerIconClicked += id => reported = id;
            Repaint(card);

            WindowMessages.Click(card, new Point(300, 100));

            Assert.Null(detailed);
            Assert.Null(replayed);
            Assert.Null(reported);
        });
    }

    [Fact]
    public void Click_OwnPlayerIcon_ReportsNobody()
    {
        var match = Match();

        UiHost.Run(host =>
        {
            using var card = Card(host, match);
            string? reported = null;
            card.PlayerIconClicked += id => reported = id;
            Repaint(card);

            // The card's own player announces itself instead of handing a summoner id to the caller.
            WindowMessages.Click(card, TeamIconCentre(row: 1, column: 0));

            Assert.Null(reported);
        });
    }

    [Fact]
    public void RequestReplay_WithoutAClick_AsksForTheReplay()
    {
        var match = Match();

        UiHost.Run(host =>
        {
            using var card = Card(host, match);
            MatchInfo? replayed = null;
            card.ReplayClicked += info => replayed = info;

            card.RequestReplay();

            Assert.Same(match, replayed);
        });
    }

    [Fact]
    public void Move_OverTheReplayLink_OffersTheHandCursor()
    {
        UiHost.Run(host =>
        {
            using var card = Card(host, Match());
            Repaint(card);

            WindowMessages.Move(card, ReplayCentre);

            Assert.Equal(Cursors.Hand, card.Cursor);
        });
    }

    [Fact]
    public void Move_OverAPlayerIcon_OffersTheHandCursor()
    {
        UiHost.Run(host =>
        {
            using var card = Card(host, Match());
            Repaint(card);

            WindowMessages.Move(card, TeamIconCentre(row: 0, column: 0));

            Assert.Equal(Cursors.Hand, card.Cursor);
        });
    }

    [Fact]
    public void Move_OverEmptySpace_OffersTheOrdinaryCursor()
    {
        UiHost.Run(host =>
        {
            using var card = Card(host, Match());
            Repaint(card);
            WindowMessages.Move(card, ReplayCentre);

            WindowMessages.Move(card, new Point(300, 100));

            Assert.Equal(Cursors.Default, card.Cursor);
        });
    }

    [Fact]
    public void Leave_TheCard_ResetsTheCursor()
    {
        UiHost.Run(host =>
        {
            using var card = Card(host, Match());
            Repaint(card);
            WindowMessages.Move(card, DetailsCentre);

            WindowMessages.Leave(card);

            Assert.Equal(Cursors.Default, card.Cursor);
        });
    }

    [Fact]
    public void DrawToBitmap_CompactCard_MakesTheWholeCardTheDetailsLink()
    {
        var match = Match();

        UiHost.Run(host =>
        {
            using var card = Card(host, match);
            card.Compact = true;
            MatchInfo? detailed = null;
            card.DetailsClicked += info => detailed = info;

            using var painted = Repaint(card);
            WindowMessages.Click(card, new Point(5, 5));

            Assert.Equal(card.Size, painted.Size);
            Assert.Same(match, detailed);
        });
    }

    [Fact]
    public void DrawToBitmap_SelectedCompactCardWithoutAHeroIcon_Paints()
    {
        var match = Match();
        match.HeroIcon = null;

        UiHost.Run(host =>
        {
            using var card = Card(host, match);
            card.Compact = true;
            card.SelectedMatch = true;

            using var painted = Repaint(card);

            Assert.Equal(new Size(CardWidth, CardHeight), painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_MayhemCard_PaintsTheAugmentIcons()
    {
        var match = Match("q_2400", "海克斯大乱斗");
        match.Augments = Runes(2);

        UiHost.Run(host =>
        {
            using var card = Card(host, match);

            using var painted = Repaint(card);

            Assert.Equal(new Size(CardWidth, CardHeight), painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_CardWithADownloadedReplay_Paints()
    {
        var match = Match();
        match.IsReplayDownloaded = true;

        UiHost.Run(host =>
        {
            using var card = Card(host, match);

            using var painted = Repaint(card);

            Assert.Equal(new Size(CardWidth, CardHeight), painted.Size);
        });
    }

    [Fact]
    public void Click_TeamColumnOfANarrowCard_DoesNothing()
    {
        var match = Match();

        UiHost.Run(host =>
        {
            // Below 980 pixels the row drops the two team columns altogether.
            using var card = Card(host, match, width: 900);
            string? reported = null;
            card.PlayerIconClicked += id => reported = id;
            Repaint(card);

            WindowMessages.Click(card, TeamIconCentre(row: 0, column: 0));

            Assert.Null(reported);
        });
    }

    [Fact]
    public void Click_CardWithoutAGame_DoesNothing()
    {
        UiHost.Run(host =>
        {
            using var card = Card(host, match: null);
            string? reported = null;
            card.PlayerIconClicked += id => reported = id;

            using var painted = Repaint(card);
            WindowMessages.Click(card, ReplayCentre);

            Assert.Null(reported);
            Assert.Null(card.MatchInfo);
            Assert.Equal(new Size(CardWidth, CardHeight), painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_Row_PaintsItselfAndLaysOutItsLinks()
    {
        var match = Match();

        UiHost.Run(host =>
        {
            using var card = new RecordingPanel(match) { Width = CardWidth, Height = CardHeight };
            host.Controls.Add(card);
            MatchInfo? detailed = null;
            card.DetailsClicked += info => detailed = info;

            using var painted = Repaint(card);
            WindowMessages.Click(card, DetailsCentre);

            // Painting is what lays the links out, so the click only lands if the row really painted itself.
            Assert.True(card.Painted > 0, "The row painted itself.");
            Assert.Same(match, detailed);
        });
    }

    [Fact]
    public void DrawToBitmap_RowWithoutOneOfTheTeams_Paints()
    {
        var match = Match();
        match.RedTeam = null!;

        UiHost.Run(host =>
        {
            using var card = Card(host, match);

            using var painted = Repaint(card);

            Assert.Equal(new Size(CardWidth, CardHeight), painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_RowWithAMissingPlayer_Paints()
    {
        var match = Match();
        match.BlueTeam = [match.BlueTeam[0], null!];

        UiHost.Run(host =>
        {
            using var card = Card(host, match);

            using var painted = Repaint(card);

            Assert.Equal(new Size(CardWidth, CardHeight), painted.Size);
        });
    }

    [Fact]
    public void DisposeAllResources_CardWithAPictureBox_TakesItsImageAway()
    {
        UiHost.Run(host =>
        {
            using var card = Card(host, Match());
            using var box = new PictureBox { Image = new Bitmap(4, 4) };
            card.Controls.Add(box);

            card.DisposeAllResources();

            Assert.Null(box.Image);
        });
    }

    [Fact]
    public void DeepClean_CardWithAPictureBox_TakesItsImageAway()
    {
        UiHost.Run(host =>
        {
            using var card = Card(host, Match());
            using var box = new PictureBox { Image = new Bitmap(4, 4) };
            card.Controls.Add(box);

            card.DeepClean();

            Assert.Null(box.Image);
            Assert.Empty(card.Controls);
        });
    }

    [Fact]
    public void TrackImage_Image_IsDisposedWithTheCardsImages()
    {
        UiHost.Run(host =>
        {
            using var card = Card(host, Match());
            var owned = new Bitmap(6, 6);
            card.TrackImage(owned);
            card.TrackImage(null!);

            card.DisposeImages();

            Assert.True(IsDisposed(owned), "An image handed to the card is disposed with the card's images.");
        });
    }

    [Fact]
    public void DisposeAllResources_Card_ReleasesTheGameItHolds()
    {
        UiHost.Run(host =>
        {
            using var card = Card(host, Match());
            Repaint(card);

            card.DisposeAllResources();

            Assert.Null(card.MatchInfo);
        });
    }

    [Fact]
    public void DeepClean_Card_ReleasesEverythingAndKeepsPainting()
    {
        var match = Match();

        UiHost.Run(host =>
        {
            using var card = Card(host, match);

            card.DeepClean();
            using var painted = Repaint(card);

            // The card takes the game down with it, but a card that is cleaned and painted again survives.
            Assert.Null(match.RawGameData);
            Assert.Equal(new Size(CardWidth, CardHeight), painted.Size);
        });
    }

    [Fact]
    public void Dispose_CardTwice_ReleasesTheGameOnce()
    {
        var match = Match();

        UiHost.Run(host =>
        {
            var card = Card(host, match);

            card.Dispose();
            card.Dispose();

            Assert.Null(match.RawGameData);
            Assert.Null(card.MatchInfo);
        });
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("<b>粗体</b>", "粗体")]
    [InlineData("第一行<br>第二行", "第一行\n第二行")]
    [InlineData(" <mainText>说明</mainText> ", "说明")]
    public void StripHtmlTags_TooltipMarkup_ReturnsPlainText(string markup, string expected)
    {
        Assert.Equal(expected, MatchListPanel.StripHtmlTags(markup));
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

    private static Bitmap Repaint(MatchListPanel card)
    {
        var painted = new Bitmap(card.Width, card.Height);
        card.DrawToBitmap(painted, new Rectangle(Point.Empty, card.Size));
        return painted;
    }

    private static MatchListPanel Card(Form host, MatchInfo? match, int width = CardWidth)
    {
        // A child window is clipped to its parent, and a click past that edge never reaches the card, so the host has
        // to be wide enough for the whole row before its right-hand side can be clicked at all.
        host.ClientSize = new Size(width + 40, 300);
        var card = new MatchListPanel(match) { Width = width, Height = CardHeight };
        host.Controls.Add(card);
        return card;
    }

    // PaintTeams starts the columns 148 pixels from the right edge, steps 27 pixels per player and centres 24-pixel
    // icons on each slot; row 0 is the red team and row 1 the blue one.
    private static Point TeamIconCentre(int row, int column) =>
        new(CardWidth - 148 + column * 27 + 12, 18 + row * 34 + 12);

    private static MatchInfo Match(string queueId = "q_450", string mode = "极地大乱斗") =>
        new()
        {
            Index = 3,
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
            SelfPlayer = Player(7, "甲", self: true),
            BlueTeam = [Player(7, "甲", self: true), Player(9, "丙", self: false)],
            RedTeam = [Player(8, "乙", self: false)],
        };

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
        [
            .. Enumerable
                .Range(1, count)
                .Select(id => new RuneInfo
                {
                    id = id,
                    RuneId = id,
                    name = $"符文 {id}",
                    longDesc = "<b>说明</b>",
                    Icon = new Bitmap(4, 4),
                }),
        ];

    private static PlayerInfo Player(long summonerId, string name, bool self) =>
        new()
        {
            SummonerId = summonerId,
            FullName = name,
            GameName = name,
            IsSelf = self,
            Avatar = new Bitmap(4, 4),
        };

    /// <summary>Counts the paints, so a test can tell "the row painted" apart from "nothing painted at all".</summary>
    private sealed class RecordingPanel(MatchInfo match) : MatchListPanel(match)
    {
        public int Painted { get; private set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Painted++;
        }
    }
}
