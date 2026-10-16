using System.Reflection;
using League;
using League.ChampionSkin;
using League.Controls;
using League.Models;
using League.PrimaryElection;
using static StatisticsCheckSupport;

/// <summary>
/// Preselection: which list champion select uses (assigned lane, bench modes, fallback to the general list), the
/// champion filter, and the window with a simulated champion list (adding, reordering, removing, switching lists).
/// Set LEAGUE_SKIN_PREVIEW to a folder with {championId}-icon.png files to render the preview with real icons.
/// </summary>
internal static class PreliminaryChecks
{
    public static void Run()
    {
        Resolve();
        Filter();
        Window();
        Console.WriteLine(
            "PASS: preselection lists by lane and mode, general fallback, filter, window editing and preview."
        );
    }

    private static PreliminaryHero Hero(int id, int priority) => new() { ChampionId = id, Priority = priority };

    private static void Resolve()
    {
        var config = new PreliminaryConfig { Heroes = [Hero(1, 2), Hero(2, 1)] };
        config.PositionHeroes["middle"] = [Hero(103, 1)];
        config.PositionHeroes["aram"] = [];

        var (key, heroes) = PreliminaryLists.Resolve(config, 420, "middle");
        Check(key == "middle" && heroes.Single().ChampionId == 103, "Ranked uses the assigned lane's list");
        (key, heroes) = PreliminaryLists.Resolve(config, 420, "UTILITY".ToLowerInvariant());
        Check(
            key == "general" && heroes.Select(h => h.ChampionId).SequenceEqual([2, 1]),
            "An empty lane falls back, sorted"
        );
        (key, _) = PreliminaryLists.Resolve(config, 430, "");
        Check(key == "general", "Blind pick has no lane");
        (key, _) = PreliminaryLists.Resolve(config, 450, "middle");
        Check(key == "general", "An empty ARAM list falls back to the general list");
        config.PositionHeroes["aram"] = [Hero(157, 1)];
        (key, heroes) = PreliminaryLists.Resolve(config, 2400, "");
        Check(key == "aram" && heroes.Single().ChampionId == 157, "Bench modes use the ARAM list");
        Check(PreliminaryLists.Resolve(null, 420, "top").Heroes.Count == 0, "No configuration, no list");

        var legacy = Newtonsoft.Json.JsonConvert.DeserializeObject<PreliminaryConfig>(
            """{"Heroes":[{"ChampionId":5,"Priority":1}],"EnableAutoPreliminary":true}"""
        )!;
        Check(
            legacy.PositionHeroes.Count == 0
                && PreliminaryLists.Resolve(legacy, 420, "top").Heroes.Single().ChampionId == 5,
            "A list saved before the split becomes the general list"
        );
        Check(PreliminaryLists.AnyHeroes(legacy) && !PreliminaryLists.AnyHeroes(new PreliminaryConfig()), "Any heroes");
    }

    private static void Filter()
    {
        var champions = Champions();
        Check(
            string.Join(",", Preliminary.Filter(champions, ChampionPosition.All, "").Select(c => c.Id).OrderBy(i => i))
                == "1,86,99,103,157",
            "All lanes list every champion"
        );
        Check(
            Preliminary.Filter(champions, ChampionPosition.Top, "ahri").Single().Id == 103,
            "Search ignores the lane"
        );
    }

    private static void Window()
    {
        var config = new LeagueConfig { Preliminary = new PreliminaryConfig { Heroes = [Hero(86, 1)] } };
        var source = new FakeSource(Environment.GetEnvironmentVariable("LEAGUE_SKIN_PREVIEW"));
        int saves = 0;
        using var form = new Preliminary(config, source, () => saves++)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-30000, -30000),
            ShowInTaskbar = false,
        };
        form.Show();
        PumpUntil(() => form.Loading.IsCompleted && Field<Dictionary<int, Image>>(form, "_icons").Count == 5);

        var grid = Field<ChampionGridView<PrimaryChampion>>(form, "_grid");
        var list = Field<PreliminaryListView>(form, "_list");
        Check(
            list.Heroes.Single().ChampionId == 86 && grid.Items.Count == 5,
            "The general list and every champion show"
        );

        // The middle list starts empty and narrows the grid to mid laners.
        Invoke(form, "SelectList", "middle");
        Check(
            list.Heroes.Count == 0 && grid.Items.All(c => c.GetPositions().Contains(ChampionPosition.Mid)),
            "Mid filter"
        );
        foreach (int id in new[] { 99, 103, 1 })
        {
            grid.SelectChampion(id);
            Invoke(form, "Add", grid.Selected!);
        }

        Invoke(form, "Add", grid.Selected!);
        Check(
            config.Preliminary.PositionHeroes["middle"].Select(h => h.ChampionId).SequenceEqual([99, 103, 1])
                && saves == 3,
            "Champions are added once, in order, and saved"
        );
        list.Move(2, 0);
        Check(
            config
                .Preliminary.PositionHeroes["middle"]
                .Select(h => (h.ChampionId, h.Priority))
                .SequenceEqual([(1, 1), (99, 2), (103, 3)]),
            "Moving renumbers the priorities"
        );
        list.RemoveAt(1);
        Check(
            config.Preliminary.PositionHeroes["middle"].Select(h => h.ChampionId).SequenceEqual([1, 103]) && saves == 5,
            "Removing saves the shorter list"
        );
        Check(
            ((Button)Field<System.Collections.IDictionary>(form, "_tabs")["middle"]!).Text == "中单 2",
            "Tabs show counts"
        );
        Check(config.Preliminary.Heroes.Single().ChampionId == 86, "Other lists are untouched");
        list.Highlight(103);
        Application.DoEvents();
        SavePreview(form, "preliminary.png");

        form.Close();
        Application.DoEvents();
        Check(source.Outstanding == 0, "Every icon was released: " + source.Outstanding);
    }

    private static List<PrimaryChampion> Champions() =>
        [
            new()
            {
                Id = 99,
                Name = "光辉女郎",
                Title = "拉克丝",
                Alias = "Lux",
            },
            new()
            {
                Id = 1,
                Name = "黑暗之女",
                Title = "安妮",
                Alias = "Annie",
            },
            new()
            {
                Id = 103,
                Name = "九尾妖狐",
                Title = "阿狸",
                Alias = "Ahri",
            },
            new()
            {
                Id = 157,
                Name = "疾风剑豪",
                Title = "亚索",
                Alias = "Yasuo",
            },
            new()
            {
                Id = 86,
                Name = "德玛西亚之力",
                Title = "盖伦",
                Alias = "Garen",
            },
        ];

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
            Check(DateTime.UtcNow < deadline, "Timed out waiting for the preselection window.");
        }

        Application.DoEvents();
    }

    private sealed class FakeSource(string? artDirectory) : IPreliminarySource
    {
        private readonly List<Image> _handedOut = new();

        public int Outstanding => _handedOut.Count(IsAlive);

        // A disposed bitmap throws on Width.
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

        public Task<List<PrimaryChampion>> LoadChampionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Champions());

        public async Task<Image?> LoadIconAsync(int championId, CancellationToken cancellationToken)
        {
            await Task.Yield();
            string? path = artDirectory == null ? null : Path.Combine(artDirectory, $"{championId}-icon.png");
            Image image = path != null && File.Exists(path) ? new Bitmap(path) : Generated(championId);
            _handedOut.Add(image);
            return image;
        }

        private static Image Generated(int seed)
        {
            var bitmap = new Bitmap(64, 64);
            using var g = Graphics.FromImage(bitmap);
            var random = new Random(seed);
            g.Clear(Color.FromArgb(random.Next(40, 200), random.Next(40, 200), random.Next(40, 200)));
            return bitmap;
        }
    }
}
