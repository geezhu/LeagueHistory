using System.Reflection;
using League.ChampionSkin;
using League.Models;
using League.PrimaryElection;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// The skin browser: owned-skin parsing, filters, image sources, and the window with a simulated client (selecting a
/// champion and a skin, setting and clearing the automatic skin, late images after switching champions).
/// Set LEAGUE_SKIN_PREVIEW to a folder of {skinId}-splash.jpg, {skinId}-tile.jpg and {championId}-icon.png files to
/// render the preview with real art; otherwise generated images are used.
/// </summary>
internal static class SkinBrowserChecks
{
    public static void Run()
    {
        Catalog();
        Filters();
        Sources();
        Window();
        Console.WriteLine(
            "PASS: skin browser catalog, filters, image sources, selection, automatic skin, late images and preview."
        );
    }

    private static void Catalog()
    {
        var owned = new Dictionary<int, List<SkinThumbItem>>();
        SkinCatalog.AddOwnedSkin(owned, Owned(99001, "暗黑元素", 99));
        SkinCatalog.AddOwnedSkin(owned, Owned(99001, "暗黑元素", 99));
        SkinCatalog.AddOwnedSkin(owned, Owned(99002, "未拥有", 99, owned: false));
        SkinCatalog.AddOwnedSkin(owned, Owned(103005, "无英雄 ID", 0));
        Check(
            owned[99].Count == 1 && owned[103].Single().SkinId == 103005,
            "Owned skins are deduplicated and inferred"
        );
        Check(owned[99][0].SplashPath == "/splash/99001.jpg", "The inventory's splash path is kept");

        var champions = new[] { Champion(99, "光辉女郎"), Champion(1, "黑暗之女") };
        var models = SkinCatalog.Build(champions, owned, new Dictionary<int, int> { [99] = 99001, [1] = 1003 });
        var lux = models.Single(m => m.ChampionId == 99);
        var annie = models.Single(m => m.ChampionId == 1);
        Check(lux.Skins[0].IsDefault && lux.Skins[1].SkinId == 99001, "The default skin comes first");
        Check(lux.PreferredSkinId == 99001 && lux.IsOwned, "An owned preference is kept");
        Check(
            annie.PreferredSkinId == 0 && !annie.IsOwned && annie.Skins.Single().IsDefault,
            "Unowned preferences drop"
        );

        // Samira's skin owned without Samira: the added default skin takes its real paths from the champion data.
        var champion = JObject.Parse(
            """
            {"skins":[{"id":360000,"splashPath":"/lol-game-data/assets/ASSETS/Characters/Samira/Skins/Base/Images/samira_splash_centered_0.jpg","tilePath":"/tile/0.jpg"},
                      {"id":360020,"splashPath":"/other.jpg","tilePath":"/other-tile.jpg"}]}
            """
        );
        var samiraDefault = new SkinThumbItem { SkinId = 360000, IsBase = true };
        SkinCatalog.ApplyChampionPaths(champion, samiraDefault);
        Check(
            samiraDefault.SplashPath!.EndsWith("samira_splash_centered_0.jpg")
                && samiraDefault.TilePath == "/tile/0.jpg",
            "A skin without paths takes them from the champion data"
        );
        var owned360 = new SkinThumbItem { SkinId = 360020, SplashPath = "/inventory.jpg" };
        SkinCatalog.ApplyChampionPaths(champion, owned360);
        Check(
            owned360.SplashPath == "/inventory.jpg" && owned360.TilePath == "/other-tile.jpg",
            "Paths from the inventory are kept"
        );
    }

    private static void Filters()
    {
        var models = new List<ChampionSkinCardModel>
        {
            Model(1, "安妮", "黑暗之女", "Annie", [ChampionPosition.Mid], owned: true, skins: 2),
            Model(2, "盖伦", "德玛西亚之力", "Garen", [ChampionPosition.Top], owned: true, skins: 0),
            Model(
                3,
                "拉克丝",
                "光辉女郎",
                "Lux",
                [ChampionPosition.Mid, ChampionPosition.Support],
                owned: false,
                skins: 0
            ),
        };
        Check(
            Names(ChampionSkinBrowser.Filter(models, ChampionPosition.Mid, false, false, "")) == "安妮,拉克丝",
            "Lane filter"
        );
        Check(
            Names(ChampionSkinBrowser.Filter(models, ChampionPosition.All, true, false, null)) == "安妮,盖伦",
            "Owned filter"
        );
        Check(
            Names(ChampionSkinBrowser.Filter(models, ChampionPosition.All, false, true, "")) == "安妮",
            "Has-skin filter"
        );
        Check(
            Names(ChampionSkinBrowser.Filter(models, ChampionPosition.All, false, false, " lux ")) == "拉克丝",
            "Search alias"
        );
        Check(
            Names(ChampionSkinBrowser.Filter(models, ChampionPosition.All, false, false, "德玛")) == "盖伦",
            "Search title"
        );
    }

    private static void Sources()
    {
        var skin = new SkinThumbItem { SkinId = 99001, SplashPath = "lol-game-data/assets/x.jpg" };
        var splash = SkinImageStore.Sources(99, skin, SkinImageKind.Splash);
        Check(
            splash.SequenceEqual([
                "/lol-game-data/assets/x.jpg",
                "/lol-game-data/assets/v1/champion-splashes/99/99001.jpg",
                "/lol-game-data/assets/v1/champion-splashes/uncentered/99/99001.jpg",
            ]),
            "Splash sources: the inventory path, then centered and uncentered art"
        );
        Check(
            SkinImageStore.Sources(99, new SkinThumbItem { SkinId = 99000 }, SkinImageKind.Tile).Single()
                == "/lol-game-data/assets/v1/champion-tiles/99/99000.jpg",
            "Tiles come from champion-tiles"
        );
    }

    private static void Window()
    {
        var config = new LeagueConfig
        {
            PreferredChampionSkins = new() { [99] = 99007 },
            SkinBrowserOwnedOnly = true,
            SkinBrowserHasSkinOnly = false,
        };
        var source = new FakeSource(Environment.GetEnvironmentVariable("LEAGUE_SKIN_PREVIEW"));
        int saves = 0;
        using var form = new ChampionSkinBrowser(config, source, () => saves++)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-30000, -30000),
            ShowInTaskbar = false,
        };
        form.Show();
        PumpUntil(() => form.Loading.IsCompleted && Splash(form).Image != null);

        var grid = Field<ChampionGridView<ChampionSkinCardModel>>(form, "_grid");
        var strip = Field<SkinStripView>(form, "_strip");
        Check(grid.Items.Count == 4 && grid.Selected == grid.Items[0], "Owned champions listed; the first is selected");
        grid.SelectChampion(99);
        PumpUntil(() => Splash(form).Image != null && Field<Dictionary<int, Image>>(form, "_tiles").Count >= 9);
        Check(strip.Selected?.SkinId == 99007, "The automatic skin is shown first");
        SavePreview(form, "skin-browser.png");

        // Pick another skin and make it automatic.
        Invoke(strip, "SelectIndex", 1, false);
        PumpUntil(() => Splash(form).Image != null);
        Check(strip.Selected?.SkinId == 99001, "Selecting a tile previews it");
        Field<Button>(form, "_setPreferred").PerformClick();
        Check(config.PreferredChampionSkins[99] == 99001 && saves == 1, "The automatic skin is saved");
        Field<Button>(form, "_restoreDefault").PerformClick();
        Check(!config.PreferredChampionSkins.ContainsKey(99) && saves == 2, "Restoring the default clears it");

        // A splash still loading when the champion changes is dropped, not shown on the next champion.
        var delay = new TaskCompletionSource();
        source.Delay = delay;
        Invoke(strip, "SelectIndex", 2, false);
        grid.SelectChampion(1);
        delay.SetResult();
        PumpUntil(() => Splash(form).Image != null);
        Check(Splash(form).Image?.Tag as string == "splash:1000", "Only the current champion's art is shown");

        form.Close();
        Application.DoEvents();
        Check(source.Outstanding == 0, "Every image the source handed out was disposed: " + source.Outstanding);
    }

    private static SkinSplashView Splash(ChampionSkinBrowser form) => Field<SkinSplashView>(form, "_splash");

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    private static void Invoke(object owner, string name, params object[] args) =>
        owner.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(owner, args);

    private static void SavePreview(Form form, string file)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(AppContext.BaseDirectory, file));
    }

    private static void PumpUntil(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!ready())
        {
            Application.DoEvents();
            Thread.Sleep(5);
            Check(DateTime.UtcNow < deadline, "Timed out waiting for the skin browser.");
        }

        Application.DoEvents();
    }

    private static string Names(IEnumerable<ChampionSkinCardModel> models) =>
        string.Join(",", models.Select(m => m.Name));

    private static JObject Owned(int id, string name, int championId, bool owned = true) =>
        new()
        {
            ["id"] = id,
            ["name"] = name,
            ["championId"] = championId,
            ["isBase"] = false,
            ["splashPath"] = "/splash/" + id + ".jpg",
            ["ownership"] = new JObject { ["owned"] = owned },
        };

    private static PrimaryChampion Champion(int id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            Title = "",
            Alias = "",
        };

    private static ChampionSkinCardModel Model(
        int id,
        string name,
        string title,
        string alias,
        List<ChampionPosition> positions,
        bool owned,
        int skins
    ) =>
        new()
        {
            ChampionId = id,
            Name = name,
            Title = title,
            Alias = alias,
            IsOwned = owned,
            DefaultSkinId = id * 1000,
            Positions = positions,
            Skins = Enumerable
                .Range(0, skins + 1)
                .Select(i => new SkinThumbItem
                {
                    SkinId = id * 1000 + i,
                    IsBase = i == 0,
                    Name = "皮肤" + i,
                })
                .ToList(),
        };

    /// <summary>A client with four owned champions; counts images so the test can confirm they are released.</summary>
    private sealed class FakeSource(string? artDirectory) : ISkinBrowserSource
    {
        private readonly List<Image> _handedOut = new();

        // A disposed bitmap throws on Width, so the count is what the browser still holds.
        public int Outstanding => _handedOut.Count(IsAlive);

        private static bool IsAlive(Image image)
        {
            try
            {
                return image.Width > 0;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public TaskCompletionSource? Delay { get; set; }

        public Task<List<ChampionSkinCardModel>> LoadAsync(
            IReadOnlyDictionary<int, int> preferred,
            CancellationToken cancellationToken
        )
        {
            var owned = new Dictionary<int, List<SkinThumbItem>>
            {
                [99] = Skins(
                    99,
                    "魔法少女 拉克丝",
                    "窃法巧手 拉克丝",
                    "突击队员 拉克丝",
                    "皇家守卫 拉克丝",
                    "钢铁军团 拉克丝",
                    "星之守护者 拉克丝",
                    "暗黑元素 拉克丝",
                    "月神 拉克丝"
                ),
                [1] = Skins(1, "哥特萝莉", "小红帽", "仙境奇遇"),
                [103] = Skins(103, "九尾妖狐 古墓丽影", "灵魂莲华"),
                [157] = Skins(157, "西部牛仔"),
            };
            var champions = new[]
            {
                Champion(99, "光辉女郎", "拉克丝", "Lux"),
                Champion(1, "黑暗之女", "安妮", "Annie"),
                Champion(103, "九尾妖狐", "阿狸", "Ahri"),
                Champion(157, "疾风剑豪", "亚索", "Yasuo"),
                Champion(86, "德玛西亚之力", "盖伦", "Garen"),
            };
            return Task.FromResult(SkinCatalog.Build(champions, owned, preferred));
        }

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

        private static PrimaryChampion Champion(int id, string name, string title, string alias) =>
            new()
            {
                Id = id,
                Name = name,
                Title = title,
                Alias = alias,
            };

        public Task<Image?> LoadIconAsync(ChampionSkinCardModel champion, CancellationToken cancellationToken) =>
            Task.FromResult<Image?>(
                Track(
                    Art($"{champion.ChampionId}-icon.png")
                        ?? Generated(64, 64, champion.ChampionId, champion.Title[..1])
                )
            );

        public async Task<Image?> LoadSkinAsync(
            ChampionSkinCardModel champion,
            SkinThumbItem skin,
            SkinImageKind kind,
            CancellationToken cancellationToken
        )
        {
            if (kind == SkinImageKind.Splash && Delay is { } delay)
            {
                Delay = null;
                await delay.Task;
            }

            await Task.Yield();
            var image =
                kind == SkinImageKind.Tile
                    ? Art($"{skin.SkinId}-tile.jpg")
                        ?? Generated(128, 128, skin.SkinId, (skin.SkinId % 1000).ToString())
                    : Art($"{skin.SkinId}-splash.jpg") ?? Generated(1215, 717, skin.SkinId, skin.Name);
            image.Tag = (kind == SkinImageKind.Tile ? "tile:" : "splash:") + skin.SkinId;
            return Track(image);
        }

        private Image? Art(string file)
        {
            string? path = artDirectory == null ? null : Path.Combine(artDirectory, file);
            return path != null && File.Exists(path) ? new Bitmap(Image.FromFile(path)) : null;
        }

        private Image Track(Image image)
        {
            lock (_handedOut)
            {
                _handedOut.Add(image);
            }

            return image;
        }

        private static Image Generated(int width, int height, int seed, string text)
        {
            var bitmap = new Bitmap(width, height);
            using var g = Graphics.FromImage(bitmap);
            var random = new Random(seed);
            var a = Color.FromArgb(random.Next(40, 200), random.Next(40, 200), random.Next(40, 200));
            using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(0, 0, width, height),
                a,
                Color.FromArgb(20, 24, 30),
                45f
            );
            g.FillRectangle(brush, 0, 0, width, height);
            using var font = new Font("Microsoft YaHei UI", height / 6f, FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(
                g,
                text,
                font,
                new Rectangle(0, 0, width, height),
                Color.FromArgb(200, 255, 255, 255),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );
            return bitmap;
        }
    }
}
