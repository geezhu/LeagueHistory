using System.Net;
using League.Clients;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;
using static StatisticsFixtures;

internal static class ProfileViewChecks
{
    public static void Run()
    {
        var masteryData = Masteries();
        var extendedMastery = MasterySelection(masteryData);
        MasterySurface(masteryData);
        PosterGradients();
        RecentOverview();
        RecentActivity();
        BackgroundSelection(masteryData);
        MasteryGallery(extendedMastery);
    }

    private static JArray MasterySelection(JArray masteryData)
    {
        Check(
            ProfileMasteryView
                .SelectTop(masteryData)
                .Select(p => (int)p["championId"]!)
                .SequenceEqual(new[] { 2, 1, 3 }),
            "Mastery ordering/deduplication/missing scores"
        );
        var sixMasteries = new JArray(
            Enumerable.Range(1, 6).Select(id => new JObject { ["championId"] = id, ["championPoints"] = id * 100 })
        );
        Check(ProfileMasteryView.SelectTop(sixMasteries).Length == 5, "Homepage previews five champions");
        Check(
            ProfileMasteryView
                .StandardChampions(JArray.Parse("[{\"id\":-1},{\"id\":1},{\"id\":1},{\"id\":2},{\"id\":60001}]"))
                .Length == 2,
            "Ownership roster excludes placeholder, duplicates and classic variants"
        );
        var extendedMastery = (JArray)masteryData.DeepClone();
        extendedMastery.Add(JObject.Parse("{\"championId\":5,\"championPoints\":0,\"championLevel\":0}"));
        Check(
            ProfileMasteryView.SelectAll(extendedMastery).Length == 4
                && ProfileMasteryView.SelectTop(extendedMastery).Length == 3,
            "Gallery includes zero-point records beyond homepage preview"
        );
        return extendedMastery;
    }

    private static void MasterySurface(JArray masteryData)
    {
        using var form = HiddenForm(new Size(850, 650));
        form.Show();
        var masteryPaths = new List<string>();
        using var masteryClient = new HttpClient(
            new Handler(path =>
            {
                masteryPaths.Add(path);
                return path == "/lol-champion-mastery/v1/self/champion-mastery"
                    ? new(HttpStatusCode.OK, masteryData.ToString())
                    : new(HttpStatusCode.NotFound, "");
            })
        )
        {
            BaseAddress = new Uri("https://localhost/"),
        };
        using var masteryView = new ProfileMasteryView(
            new PlayerStatisticsService(() => masteryClient),
            id => Task.FromResult<(Image?, string, string)>((new Bitmap(32, 32), "英雄 " + id, ""))
        )
        {
            Dock = DockStyle.Fill,
        };
        form.Controls.Add(masteryView);
        masteryView.LoadPlayer("self");
        PumpUntil(() => Children(masteryView).OfType<PictureBox>().Count() == 3);
        Check(Children(masteryView).OfType<PictureBox>().Count() == 3, "Three mastery champions rendered");
        Check(Children(masteryView).OfType<Label>().Any(l => l.Text.Contains("80,000")), "Mastery points displayed");
        Check(
            masteryView.OwnershipText.StartsWith("成就英雄 3 /"),
            "Unavailable inventory uses clearly labelled mastery count"
        );
        PrivacyBadge();
        JObject[]? allMastery = null;
        masteryView.AllRequested += records => allMastery = records;
        Children(masteryView).OfType<Button>().Single(b => b.Text == "全部 ›").PerformClick();
        Check(allMastery?.Length == 3, "All mastery navigation reuses loaded records");
        Check(
            masteryPaths.Contains("/lol-champion-mastery/v1/self/champion-mastery")
                && !masteryPaths.Any(p => p.Contains("lol-collections")),
            "Mastery uses PUUID endpoint despite unavailable profile"
        );
        masteryView.Dispose();
        var work = (Task)
            typeof(ProfileMasteryView)
                .GetField(
                    "_cleanupWork",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                )!
                .GetValue(masteryView)!;
        PumpUntil(() => work.IsCompleted);
        work.GetAwaiter().GetResult();
    }

    private static void PrivacyBadge()
    {
        using var font = new Font("Segoe UI", 15f);
        using (var largeName = new LinkLabel { Size = new Size(320, 36), Font = font })
        {
            League.UIHelpers.PlayerNamePresentation.Apply(largeName, "Player#123", false);
            var privacyBadge = largeName.Controls.OfType<Label>().Single();
            Check(
                privacyBadge.Font.SizeInPoints == largeName.Font.SizeInPoints
                    && largeName.Padding.Right >= largeName.Font.Height,
                "Profile privacy badge scales with player name"
            );
        }
    }

    private static void PosterGradients()
    {
        var posterType = typeof(ProfileMasteryView).Assembly.GetType("League.Controls.MasteryPoster")!;
        foreach (int height in new[] { 270, 280, 350 })
        {
            using var poster = (Control)Activator.CreateInstance(posterType, new object?[] { 4, null })!;
            poster.Size = new Size(160, height);
            poster.BackColor = Color.FromArgb(180, 180, 180);
            using var rendered = new Bitmap(160, height);
            poster.DrawToBitmap(rendered, new Rectangle(0, 0, 160, height));
            for (int y = height / 3 - 2; y <= height / 3 + 2; y++)
            {
                Check(
                    Math.Abs(rendered.GetPixel(80, y).R - rendered.GetPixel(80, y - 1).R) <= 3,
                    "Poster gradient starts without a dark seam"
                );
            }
        }
    }

    private static void RecentOverview()
    {
        var overviewGames = new[]
        {
            OverviewGame(1, 430, 4, 2, 4),
            OverviewGame(2, 420, 2, 0, 4),
            OverviewGame(3, 2400, 90, 1, 90),
        };
        var overviewStats = League.Models.RecentOverview.Build(overviewGames);
        Check(
            overviewStats.Kda == 21 && overviewStats.KdaGames == 2,
            "Overview aggregates rift KDA with multiplier three and excludes ARAM"
        );
        Check(
            League.Models.RecentOverview.Build(new[] { OverviewGame(1, 440, 2, 0, 4) }).Kda == 18,
            "Overview zero total deaths uses denominator one"
        );
        var aramKda = League.Models.RecentOverview.Build(
            new[] { OverviewGame(1, 450, 2, 1, 4), OverviewGame(2, 2400, 4, 2, 2) }
        );
        Check(
            aramKda.Kda == 12 && aramKda.UsesAramKda && aramKda.KdaGames == 2,
            "Overview falls back to both ARAM modes when rift sample is absent"
        );
        Check(!overviewStats.UsesAramKda, "Rift sample takes priority over ARAM");
        Check(
            League.Models.RecentOverview.Build(new[] { OverviewGame(1, 1700, 2, 1, 4) }).Kda == null,
            "Other modes do not enter KDA fallback"
        );
        Check(
            League.Models.RecentOverview.Build(new[] { OverviewGame(1, 430, 2, 1, 4) with { Kills = null } }).Kda
                == null,
            "Overview missing KDA fields are not treated as zero"
        );
        OverviewModesAndSample(overviewStats);
        var overviewType = typeof(ProfileMasteryView).Assembly.GetType("League.Controls.RecentOverviewView")!;
        using (var overviewControl = (Control)Activator.CreateInstance(overviewType)!)
        {
            overviewControl.Width = 800;
            overviewType.GetMethod("SetGames")!.Invoke(overviewControl, new object[] { overviewGames, "最近 3 场" });
            using var bitmap = new Bitmap(800, 170);
            overviewControl.DrawToBitmap(bitmap, new Rectangle(0, 0, 800, 170));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "recent-overview.png"));
        }
    }

    private static void OverviewModesAndSample(RecentOverview overviewStats)
    {
        Check(
            overviewStats.Modes.Length == 4 && overviewStats.Modes.Any(m => m.Queue == 2400 && m.Games == 1),
            "Hextech ARAM replaces an empty mode without changing slot count"
        );
        var hexOnly = League.Models.RecentOverview.Build(new[] { OverviewGame(1, 2400, 1, 1, 1) });
        Check(
            hexOnly.Modes[1].Queue == 2400 && hexOnly.Modes[0].Queue == 430,
            "Hextech ARAM prefers replacing empty standard ARAM over other empty modes"
        );
        var allModes = League.Models.RecentOverview.Build(
            new[] { 430, 450, 420, 440, 2400 }.Select((queue, i) => OverviewGame(i + 1, queue, 1, 1, 1))
        );
        Check(
            allModes.Modes.Length == 4 && allModes.Modes.Any(m => m.Queue == 2400),
            "Hextech ARAM is visible even with all default modes played"
        );
        Check(
            League
                .Models.RecentOverview.Build(Enumerable.Range(1, 60).Select(i => OverviewGame(i, 430, 1, 1, 1)))
                .Sample == 50,
            "Overview bounds sample to fifty games"
        );
    }

    private static League.Models.CareerGame OverviewGame(int id, int queue, int kills, int deaths, int assists) =>
        new(id, id, queue, 1, id % 2 == 0, kills, deaths, assists, null, null, null);

    private static void RecentActivity()
    {
        var activityType = typeof(ProfileMasteryView).Assembly.GetType("League.Controls.RecentActivityView")!;
        var roleGames = new[]
        {
            new League.Models.CareerGame(1, 1, 450, 1, true, null, null, null, null, null, null),
            new League.Models.CareerGame(2, 2, 450, 2, true, null, null, null, null, null, null),
            new League.Models.CareerGame(3, 3, 450, 3, true, null, null, null, null, null, null),
        };
        object?[] roleArgs =
        {
            roleGames,
            JArray.Parse("[{\"id\":1,\"roles\":[\"mage\",\"support\"]},{\"id\":2,\"roles\":[\"mage\",\"mage\"]}]"),
            0,
        };
        var roleWeights = (double[])activityType.GetMethod("CountRoles")!.Invoke(null, roleArgs)!;
        Check(
            (int)roleArgs[2]! == 2 && roleWeights.Sum() == 2 && roleWeights[2] == 1.5 && roleWeights[5] == .5,
            "Recent roles split dual classes, deduplicate roles and exclude missing classifications"
        );
        using (var activity = (Control)Activator.CreateInstance(activityType)!)
        {
            activity.Width = 900;
            Check(activity.Height == 86, "Wide recent activity uses compact strip");
            activity.Width = 500;
            Check(activity.Height == 150, "Narrow recent activity wraps compact strip");
            using var preview = new Bitmap(500, 150);
            activity.DrawToBitmap(preview, new Rectangle(0, 0, 500, 150));
            preview.Save(Path.Combine(AppContext.BaseDirectory, "recent-activity.png"));
        }
    }

    private static void BackgroundSelection(JArray masteryData)
    {
        var backgroundType = typeof(ProfileMasteryView).Assembly.GetType("League.Controls.ProfileBackgroundPanel")!;
        var findSplash = backgroundType.GetMethod("FindSplash")!;
        var selectSkin = backgroundType.GetMethod("SelectSkin")!;
        Check(
            (int)selectSkin.Invoke(null, new object?[] { new JObject(), masteryData })! == 2000,
            "Missing profile background uses highest mastery champion base skin"
        );
        Check(
            (int)selectSkin.Invoke(null, new object?[] { JObject.Parse("{\"backgroundSkinId\":103015}"), masteryData })!
                == 103015,
            "Explicit client background takes priority over mastery"
        );
        Check(
            (int)selectSkin.Invoke(null, new object?[] { new JObject(), new JArray() })! == 0,
            "No profile or mastery keeps default background"
        );
        var skinFixture = JObject.Parse(
            "{\"skins\":[{\"id\":103000,\"splashPath\":\"/lol-game-data/assets/center.jpg\",\"uncenteredSplashPath\":\"/lol-game-data/assets/full.jpg\"}]}"
        );
        Check(
            (string?)findSplash.Invoke(null, new object[] { skinFixture, 103000 }) == "/lol-game-data/assets/full.jpg",
            "Profile background resolves exact skin artwork"
        );
        Check(
            findSplash.Invoke(null, new object[] { skinFixture, 103001 }) == null,
            "Missing background skin does not substitute another skin"
        );
        using (var background = (Control)Activator.CreateInstance(backgroundType)!)
        {
            background.Size = new Size(800, 200);
            var name = new LinkLabel
            {
                Text = "Player",
                LinkColor = Color.Blue,
                BackColor = Color.LightBlue,
            };
            background.Controls.Add(name);
            var setArtwork = backgroundType.GetMethod(
                "SetArtwork",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            )!;
            setArtwork.Invoke(background, new object[] { new Bitmap(100, 100) });
            Check(
                name.LinkColor == Color.White && name.BackColor == Color.Transparent,
                "Artwork uses readable transparent identity controls"
            );
            setArtwork.Invoke(background, new object?[] { null });
            Check(
                name.LinkColor == Color.Blue && name.BackColor == Color.LightBlue,
                "Missing background restores original profile colors"
            );
        }
    }

    private static void MasteryGallery(JArray extendedMastery)
    {
        var galleryType = typeof(ProfileMasteryView).Assembly.GetType("League.Controls.MasteryGalleryView")!;
        using var form = HiddenForm(new Size(850, 650));
        form.Show();
        using var gallery = (Control)
            Activator.CreateInstance(
                galleryType,
                new object[] { ProfileMasteryView.SelectAll(extendedMastery), "— / —" }
            )!;
        gallery.Dock = DockStyle.Fill;
        form.Controls.Add(gallery);
        gallery.BringToFront();
        Application.DoEvents();
        Check(Children(gallery).OfType<PictureBox>().Count() == 4, "Gallery renders every deduplicated mastery record");
        using (var screenshot = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
        {
            gallery.DrawToBitmap(screenshot, new Rectangle(Point.Empty, gallery.Size));
            screenshot.Save(Path.Combine(AppContext.BaseDirectory, "mastery-gallery.png"));
        }
    }
}
