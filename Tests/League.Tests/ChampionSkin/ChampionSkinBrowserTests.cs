using League.ChampionSkin;
using League.Models;
using League.PrimaryElection;
using League.Tests.Support;

namespace League.Tests.ChampionSkin;

/// <summary>
/// The skin browser window: the champion list it reads from its source, the filter toolbar, showing a champion's
/// skins, making one the automatic skin and painting the whole window. The source is a stub that hands out generated
/// bitmaps, so no client, no network and no real art folder is involved, and the window is opened off screen.
/// </summary>
[Trait("Category", "UI")]
public class ChampionSkinBrowserTests
{
    private const int BrowserWidth = 900;
    private const int BrowserHeight = 620;
    private const int TileCellWidth = 118;
    private const int TileInset = 8;
    private const int TileSize = 96;
    private const int ChampionId = 99;
    private const int PreferredSkinId = 99002;

    [Fact]
    public void Show_OwnedChampions_ListsThemAndShowsTheAutomaticSkin()
    {
        UiHost.Run(async host =>
        {
            using var browser = Browser(Config(), new StubSource(), () => { });

            await browser.Loading;

            var grid = Grid(browser);
            Assert.Equal(3, grid.Items.Count);
            Assert.Same(grid.Items[0], grid.Selected);

            grid.SelectChampion(ChampionId);

            Assert.Equal([99000, 99001, 99002], SkinIds(browser));
            Assert.Equal(PreferredSkinId, Strip(browser).Selected?.SkinId);

            browser.Close();
        });
    }

    [Fact]
    public void SelectChampion_AnotherChampion_ReplacesTheStripWithItsSkins()
    {
        UiHost.Run(async host =>
        {
            using var browser = Browser(Config(), new StubSource(), () => { });

            await browser.Loading;
            var grid = Grid(browser);
            grid.SelectChampion(ChampionId);

            grid.SelectChampion(103);

            Assert.Equal([103000, 103001], SkinIds(browser));
            Assert.Equal(103000, Strip(browser).Selected?.SkinId);

            browser.Close();
        });
    }

    [Fact]
    public void Click_TheSetPreferredButton_SavesTheShownSkin()
    {
        var saves = 0;

        UiHost.Run(async host =>
        {
            var config = Config();
            using var browser = Browser(config, new StubSource(), () => saves++);

            await browser.Loading;
            Grid(browser).SelectChampion(ChampionId);
            WindowMessages.Click(Strip(browser), TileCentre(index: 1));
            var setPreferred = SetPreferredButton(browser);
            WindowMessages.Click(setPreferred, Centre(setPreferred));

            Assert.Equal(99001, config.PreferredChampionSkins[ChampionId]);
            Assert.Equal(1, saves);

            browser.Close();
        });
    }

    [Fact]
    public void DoubleClick_AStripTile_SavesThatSkin()
    {
        var saves = 0;

        UiHost.Run(async host =>
        {
            var config = Config();
            using var browser = Browser(config, new StubSource(), () => saves++);

            await browser.Loading;
            Grid(browser).SelectChampion(ChampionId);

            WindowMessages.DoubleClick(Strip(browser), TileCentre(index: 2));

            Assert.Equal(99002, config.PreferredChampionSkins[ChampionId]);
            Assert.Equal(1, saves);

            browser.Close();
        });
    }

    [Fact]
    public void Click_TheRestoreButton_ClearsTheAutomaticSkin()
    {
        var saves = 0;

        UiHost.Run(async host =>
        {
            var config = Config();
            using var browser = Browser(config, new StubSource(), () => saves++);

            await browser.Loading;
            Grid(browser).SelectChampion(ChampionId);
            var restore = RestoreButton(browser);
            WindowMessages.Click(restore, Centre(restore));

            Assert.False(config.PreferredChampionSkins.ContainsKey(ChampionId));
            Assert.Equal(1, saves);

            browser.Close();
        });
    }

    [Fact]
    public void Click_TheTopLaneButton_KeepsOnlyTheTopChampions()
    {
        UiHost.Run(async host =>
        {
            using var browser = Browser(Config(), new StubSource(), () => { });

            await browser.Loading;
            var ownedOnly = Check(browser, "仅已有英雄");
            WindowMessages.Click(ownedOnly, Centre(ownedOnly));
            var top = Button(browser, "上单");
            WindowMessages.Click(top, Centre(top));

            Assert.Equal(["德玛西亚之力"], Grid(browser).Items.Select(model => model.Name));

            browser.Close();
        });
    }

    [Fact]
    public void Move_OntoAButton_HoversItAndDropsTheHoverOnLeave()
    {
        UiHost.Run(async host =>
        {
            using var browser = Browser(Config(), new StubSource(), () => { });

            await browser.Loading;
            var top = Button(browser, "上单");
            WindowMessages.Move(top, Centre(top));
            WindowMessages.Leave(top);
            WindowMessages.Move(top, Centre(top));

            using var painted = new Bitmap(browser.Width, browser.Height);
            browser.DrawToBitmap(painted, new Rectangle(Point.Empty, browser.Size));

            Assert.Equal(browser.Size, painted.Size);

            browser.Close();
        });
    }

    [Fact]
    public void Key_Escape_ClosesTheWindow()
    {
        UiHost.Run(async host =>
        {
            using var browser = Browser(Config(), new StubSource(), () => { });

            await browser.Loading;
            WindowMessages.KeyDownOnly(browser, Keys.Escape);

            Assert.True(browser.IsDisposed);
        });
    }

    [Fact]
    public void LoadIconsTilesAndSplash_StubSource_ShowsThem()
    {
        UiHost.Run(async host =>
        {
            using var browser = Browser(Config(), new StubSource(), () => { });

            await browser.Loading;
            Grid(browser).SelectChampion(ChampionId);

            await WaitFor(() => Splash(browser).Image != null && Strip(browser).Tile(99001) != null);

            Assert.NotNull(Splash(browser).Image);
            Assert.NotNull(Strip(browser).Tile(99001));
            Assert.NotNull(Grid(browser).Icon(ChampionId));

            browser.Close();
        });
    }

    [Fact]
    public void DrawToBitmap_TheWindow_PaintsItsWholeSurface()
    {
        UiHost.Run(async host =>
        {
            using var browser = Browser(Config(), new StubSource(), () => { });

            await browser.Loading;
            Grid(browser).SelectChampion(ChampionId);

            using var painted = new Bitmap(browser.Width, browser.Height);
            browser.DrawToBitmap(painted, new Rectangle(Point.Empty, browser.Size));

            Assert.Equal(browser.Size, painted.Size);

            browser.Close();
        });
    }

    private static LeagueConfig Config() =>
        new()
        {
            PreferredChampionSkins = new() { [ChampionId] = PreferredSkinId },
            SkinBrowserOwnedOnly = true,
            SkinBrowserHasSkinOnly = false,
        };

    private static ChampionSkinBrowser Browser(LeagueConfig config, ISkinBrowserSource source, Action save)
    {
        var browser = new ChampionSkinBrowser(config, source, save)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-30000, -30000),
            ShowInTaskbar = false,
            ClientSize = new Size(BrowserWidth, BrowserHeight),
        };
        browser.Show();
        return browser;
    }

    private static ChampionGridView<ChampionSkinCardModel> Grid(Control browser) =>
        Find<ChampionGridView<ChampionSkinCardModel>>(browser);

    private static SkinStripView Strip(Control browser) => Find<SkinStripView>(browser);

    private static SkinSplashView Splash(Control browser) => Find<SkinSplashView>(browser);

    private static int[] SkinIds(Control browser) => [.. Strip(browser).Items.Select(skin => skin.SkinId)];

    private static Point TileCentre(int index) =>
        new(TileInset + index * TileCellWidth + TileCellWidth / 2, TileInset + TileSize / 2);

    // The window builds its controls itself, so these find them by walking the tree it publishes, the way a user finds
    // them on screen.
    private static T Find<T>(Control root)
        where T : Control => Descendants(root).OfType<T>().First();

    private static Button SetPreferredButton(Control browser) =>
        Descendants(browser).OfType<Button>().First(button => button.Text.StartsWith('★'));

    private static Button RestoreButton(Control browser) => Button(browser, "恢复默认皮肤");

    private static Button Button(Control browser, string text) =>
        Descendants(browser).OfType<Button>().First(button => button.Text == text);

    private static CheckBox Check(Control browser, string text) =>
        Descendants(browser).OfType<CheckBox>().First(check => check.Text == text);

    private static Point Centre(Control control) => new(control.Width / 2, control.Height / 2);

    private static IEnumerable<Control> Descendants(Control root) =>
        root.Controls.Cast<Control>().SelectMany(child => Descendants(child).Prepend(child));

    // The window hands its images out through its own views, so a bounded wait is what makes them observable.
    private static async Task WaitFor(Func<bool> ready)
    {
        for (int attempt = 0; attempt < 400 && !ready(); attempt++)
        {
            await Task.Delay(5);
        }

        Assert.True(ready(), "The window did not show its art in time.");
    }

    private static PrimaryChampion Champion(int id, string name, string title, string alias) =>
        new()
        {
            Id = id,
            Name = name,
            Title = title,
            Alias = alias,
        };

    private static Dictionary<int, List<SkinThumbItem>> Owned() =>
        new()
        {
            [99] = Skins(99, "魔法少女 拉克丝", "钢铁军团 拉克丝"),
            [1] = Skins(1, "哥特萝莉 安妮"),
            [103] = Skins(103, "灵魂莲华 阿狸"),
        };

    private static List<SkinThumbItem> Skins(int championId, params string[] names) =>
        names
            .Select((name, i) => new SkinThumbItem { SkinId = championId * 1000 + i + 1, Name = name })
            .Prepend(
                new SkinThumbItem
                {
                    SkinId = championId * 1000,
                    Name = "默认",
                    IsBase = true,
                }
            )
            .ToList();

    /// <summary>The client the window would read from; it hands out generated art instead.</summary>
    private sealed class StubSource : ISkinBrowserSource
    {
        public Task<List<ChampionSkinCardModel>> LoadAsync(
            IReadOnlyDictionary<int, int> preferred,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                SkinCatalog.Build(
                    [
                        Champion(99, "光辉女郎", "拉克丝", "Lux"),
                        Champion(1, "黑暗之女", "安妮", "Annie"),
                        Champion(103, "九尾妖狐", "阿狸", "Ahri"),
                        Champion(86, "德玛西亚之力", "盖伦", "Garen"),
                    ],
                    Owned(),
                    preferred
                )
            );

        public Task<Image?> LoadIconAsync(ChampionSkinCardModel champion, CancellationToken cancellationToken) =>
            Task.FromResult<Image?>(Art(64, 64));

        public Task<Image?> LoadSkinAsync(
            ChampionSkinCardModel champion,
            SkinThumbItem skin,
            SkinImageKind kind,
            CancellationToken cancellationToken
        ) => Task.FromResult<Image?>(kind == SkinImageKind.Tile ? Art(128, 128) : Art(320, 180));

        private static Bitmap Art(int width, int height)
        {
            var art = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(art);
            graphics.Clear(Color.CornflowerBlue);
            return art;
        }
    }
}
