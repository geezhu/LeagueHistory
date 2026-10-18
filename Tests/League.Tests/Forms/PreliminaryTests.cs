using League.ChampionSkin;
using League.Controls;
using League.Infrastructure;
using League.Models;
using League.PrimaryElection;
using League.Tests.Support;
using static League.FormMain;

namespace League.Tests.Forms;

/// <summary>
/// The preselection window: the champion filter it shares with champion select, and editing the current list through
/// the controls a user would touch. Everything is driven through the control tree and window messages — the tab and
/// lane buttons are found by walking the window, the champion is picked with the grid's own selection and Enter, and
/// the icons come from a stub source, so no client, file or network is involved. The expected values come from the
/// checks in Tests/StatisticsChecks/PreliminaryChecks.cs, which this class replaces.
/// </summary>
[Collection(EnvironmentCollection.Name)]
[Trait("Category", "UI")]
public class PreliminaryTests : IDisposable
{
    private readonly ResourceLoading _previousResources = Globals.resLoading;

    public PreliminaryTests()
    {
        Globals.resLoading = new ResourceLoading();
    }

    public void Dispose() => Globals.resLoading = _previousResources;

    [Theory]
    [InlineData(ChampionPosition.All, new[] { 1, 22, 64, 86, 99, 103, 157 })]
    [InlineData(ChampionPosition.Top, new[] { 86, 157 })]
    [InlineData(ChampionPosition.Jungle, new[] { 64 })]
    [InlineData(ChampionPosition.Mid, new[] { 1, 86, 99, 103, 157 })]
    [InlineData(ChampionPosition.ADC, new[] { 22 })]
    [InlineData(ChampionPosition.Support, new[] { 22, 64, 99 })]
    public void Filter_ALane_KeepsTheChampionsPlayedThere(ChampionPosition lane, int[] expected)
    {
        List<PrimaryChampion> filtered = Preliminary.Filter(Champions(), lane, "");

        Assert.Equal(expected, filtered.Select(champion => champion.Id).OrderBy(id => id));
    }

    [Theory]
    [InlineData("ahri", 103)]
    [InlineData("AHRI", 103)]
    [InlineData("阿狸", 103)]
    [InlineData("拉克丝", 99)]
    [InlineData("安妮", 1)]
    [InlineData(" ahri ", 103)]
    public void Filter_ASearch_MatchesNameTitleAndAlias(string keyword, int expected)
    {
        List<PrimaryChampion> filtered = Preliminary.Filter(Champions(), ChampionPosition.All, keyword);

        Assert.Equal(expected, filtered.Single().Id);
    }

    [Fact]
    public void Filter_ASearch_IgnoresTheLane()
    {
        List<PrimaryChampion> filtered = Preliminary.Filter(Champions(), ChampionPosition.Top, "ahri");

        Assert.Equal(103, filtered.Single().Id);
    }

    [Fact]
    public void Filter_ASearchNothingMatches_IsEmpty()
    {
        List<PrimaryChampion> filtered = Preliminary.Filter(Champions(), ChampionPosition.All, "英雄联盟");

        Assert.Empty(filtered);
    }

    [Fact]
    public void Window_OnLoad_ShowsTheConfiguredListAndEveryChampion()
    {
        UiHost.Run(async _ =>
        {
            var config = Config(Hero(86, 1));
            using var form = Window(config, new StubSource(Champions()));

            form.Show();
            await form.Loading;

            Assert.Equal(new[] { 86 }, List(form).Heroes.Select(hero => hero.ChampionId));
            Assert.Equal(7, Grid(form).Items.Count);
        });
    }

    [Fact]
    public void Window_SelectingALaneTab_FiltersTheGridToThatLane()
    {
        UiHost.Run(async _ =>
        {
            using var form = Window(Config(), new StubSource(Champions()));
            form.Show();
            await form.Loading;

            Click(Tab(form, "middle"));

            Assert.Equal(new[] { 1, 86, 99, 103, 157 }, Grid(form).Items.Select(c => c.Id).OrderBy(id => id));
        });
    }

    [Fact]
    public void Window_SelectingAnEmptyLaneTab_ShowsAnEmptyList()
    {
        UiHost.Run(async _ =>
        {
            using var form = Window(Config(), new StubSource(Champions()));
            form.Show();
            await form.Loading;

            Click(Tab(form, "middle"));

            Assert.Empty(List(form).Heroes);
        });
    }

    [Fact]
    public void Window_SelectingTheBenchTab_ShowsTheBenchList()
    {
        UiHost.Run(async _ =>
        {
            var config = Config(Hero(86, 1));
            config.Preliminary.PositionHeroes[PreliminaryLists.Aram] = [Hero(157, 1)];
            using var form = Window(config, new StubSource(Champions()));
            form.Show();
            await form.Loading;

            Click(Tab(form, PreliminaryLists.Aram));

            Assert.Equal(new[] { 157 }, List(form).Heroes.Select(hero => hero.ChampionId));
        });
    }

    [Fact]
    public void Window_TheGridBadge_NumbersTheChampionsOfTheCurrentList()
    {
        UiHost.Run(async _ =>
        {
            using var form = Window(Config(Hero(86, 1)), new StubSource(Champions()));
            form.Show();
            await form.Loading;

            Assert.Equal("1", Grid(form).Badge(Champion(86)));
            Assert.Null(Grid(form).Badge(Champion(103)));
        });
    }

    [Fact]
    public void Window_SelectingALaneButton_FiltersTheGridToThatLane()
    {
        UiHost.Run(async _ =>
        {
            using var form = Window(Config(), new StubSource(Champions()));
            form.Show();
            await form.Loading;

            Click(LaneButton(form, "下路"));

            Assert.Equal(new[] { 22 }, Grid(form).Items.Select(c => c.Id));
        });
    }

    [Fact]
    public void Window_ActivatingAChampion_AddsItToTheCurrentList()
    {
        UiHost.Run(async _ =>
        {
            var config = Config();
            int saves = 0;
            using var form = Window(config, new StubSource(Champions()), () => saves++);
            form.Show();
            await form.Loading;

            Activate(form, 99);

            Assert.Equal(new[] { 99 }, config.Preliminary.Heroes.Select(hero => hero.ChampionId));
            Assert.Equal(1, saves);
        });
    }

    [Fact]
    public void Window_ActivatingTheSameChampionAgain_KeepsOneEntry()
    {
        UiHost.Run(async _ =>
        {
            var config = Config();
            int saves = 0;
            using var form = Window(config, new StubSource(Champions()), () => saves++);
            form.Show();
            await form.Loading;

            Activate(form, 99);
            Activate(form, 99);

            Assert.Equal(new[] { 99 }, config.Preliminary.Heroes.Select(hero => hero.ChampionId));
            Assert.Equal(1, saves);
        });
    }

    [Fact]
    public void Window_ActivatingAChampion_FillsInItsNamePositionAndPriority()
    {
        UiHost.Run(async _ =>
        {
            var config = Config();
            using var form = Window(config, new StubSource(Champions()));
            form.Show();
            await form.Loading;

            Activate(form, 86);

            PreliminaryHero hero = config.Preliminary.Heroes.Single();
            Assert.Equal("德玛西亚之力", hero.ChampionName);
            Assert.Equal("Top", hero.Position);
            Assert.Equal(1, hero.Priority);
        });
    }

    [Fact]
    public void Window_MovingAnEntry_RenumbersThePriorities()
    {
        UiHost.Run(async _ =>
        {
            var config = Config();
            using var form = Window(config, new StubSource(Champions()));
            form.Show();
            await form.Loading;
            Activate(form, 99);
            Activate(form, 103);
            Activate(form, 1);

            List(form).Move(2, 0);

            Assert.Equal(
                new[] { (1, 1), (99, 2), (103, 3) },
                config.Preliminary.Heroes.Select(hero => (hero.ChampionId, hero.Priority))
            );
        });
    }

    [Fact]
    public void Window_RemovingAnEntry_SavesTheShorterList()
    {
        UiHost.Run(async _ =>
        {
            var config = Config();
            int saves = 0;
            using var form = Window(config, new StubSource(Champions()), () => saves++);
            form.Show();
            await form.Loading;
            Activate(form, 99);
            Activate(form, 103);
            Activate(form, 1);

            List(form).RemoveAt(1);

            Assert.Equal(new[] { 99, 1 }, config.Preliminary.Heroes.Select(hero => hero.ChampionId));
            Assert.Equal(4, saves);
        });
    }

    [Fact]
    public void Window_TheTab_ShowsHowManyChampionsItsListHas()
    {
        UiHost.Run(async _ =>
        {
            using var form = Window(Config(), new StubSource(Champions()));
            form.Show();
            await form.Loading;

            Activate(form, 99);
            Activate(form, 103);

            Assert.Equal("通用 2", Tab(form, PreliminaryLists.General).Text);
        });
    }

    [Fact]
    public void Window_AddingToTheCurrentList_LeavesTheOtherListsAlone()
    {
        UiHost.Run(async _ =>
        {
            var config = Config();
            using var form = Window(config, new StubSource(Champions()));
            form.Show();
            await form.Loading;

            Activate(form, 99);

            Assert.Empty(PreliminaryLists.Get(config.Preliminary, "middle"));
        });
    }

    [Fact]
    public void Window_TypingASearch_FiltersTheGridAfterTheDelay()
    {
        UiHost.Run(async _ =>
        {
            using var form = Window(Config(), new StubSource(Champions()));
            form.Show();
            await form.Loading;

            Search(form).Text = "ahri";
            await WaitForAsync(() => Grid(form).Items.Count == 1);

            Assert.Equal(new[] { 103 }, Grid(form).Items.Select(c => c.Id));
            Assert.Equal("1 个英雄", LaneBar(form).Controls.OfType<Label>().Single().Text);
        });
    }

    [Fact]
    public void Window_ClearingAnEmptyList_ChangesNothing()
    {
        UiHost.Run(async _ =>
        {
            var config = Config();
            int saves = 0;
            using var form = Window(config, new StubSource(Champions()), () => saves++);
            form.Show();
            await form.Loading;

            Click(ClearButton(form));

            Assert.Empty(config.Preliminary.Heroes);
            Assert.Equal(0, saves);
        });
    }

    [Fact]
    public void Window_Escape_ClosesTheWindow()
    {
        UiHost.Run(async _ =>
        {
            using var form = Window(Config(), new StubSource(Champions()));
            form.Show();
            await form.Loading;

            WindowMessages.KeyDownOnly(form, Keys.Escape);

            Assert.True(form.IsDisposed);
        });
    }

    [Fact]
    public void Window_Closing_ReleasesEveryIconItWasGiven()
    {
        UiHost.Run(async _ =>
        {
            var source = new StubSource(Champions());
            using var form = Window(Config(), source);
            form.Show();
            await form.Loading;
            await WaitForAsync(() => Grid(form).Icon(103) != null);
            var list = List(form);

            form.Close();

            Assert.Equal(0, source.Outstanding);
            Assert.Null(list.Icon(103));
        });
    }

    [Fact]
    public void Window_AMissingIcon_IsLeftOut()
    {
        UiHost.Run(async _ =>
        {
            var source = new StubSource(Champions());
            source.MissingIcons.Add(103);
            using var form = Window(Config(), source);
            form.Show();
            await form.Loading;

            await WaitForAsync(() => Grid(form).Icon(99) != null);

            Assert.Null(Grid(form).Icon(103));
        });
    }

    [Fact]
    public void Window_AnIconRequestThatComesBackCancelled_StopsTheLoop()
    {
        UiHost.Run(async _ =>
        {
            var source = new StubSource(Champions());
            using var form = Window(Config(), source);
            source.OnIconRequest = () =>
            {
                form.Close();
                throw new OperationCanceledException();
            };

            form.Show();
            await form.Loading;

            Assert.Equal(1, source.IconRequests);
        });
    }

    [Fact]
    public void Window_ThirteenIcons_AllArrive()
    {
        UiHost.Run(async _ =>
        {
            var source = new StubSource(ManyChampions());
            using var form = Window(Config(), source);
            form.Show();
            await form.Loading;

            await WaitForAsync(() => ManyChampions().All(c => Grid(form).Icon(c.Id) != null));

            Assert.Equal(ManyChampions().Count, Grid(form).Items.Count);
        });
    }

    [Fact]
    public void Window_TheChampionReadFails_SaysSoInTheList()
    {
        UiHost.Run(async _ =>
        {
            var source = new StubSource(Champions()) { ReadFailure = new HttpRequestException("boom") };
            using var form = Window(Config(), source);

            form.Show();
            await form.Loading;

            Assert.Equal("读取英雄失败：boom", List(form).EmptyText);
        });
    }

    [Fact]
    public void Window_TheChampionReadIsCancelled_KeepsWaiting()
    {
        UiHost.Run(async _ =>
        {
            var source = new StubSource(Champions())
            {
                ReadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            };
            using var form = Window(Config(), source);
            form.Show();
            var list = List(form);

            form.Close();
            source.ReadGate.SetResult();
            await form.Loading;

            Assert.Equal("正在读取英雄…", list.EmptyText);
        });
    }

    [Fact]
    public void Window_TheSharedSourceWithoutAClient_SaysTheReadFailed()
    {
        UiHost.Run(async _ =>
        {
            using var form = new Preliminary(Config())
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-30000, -30000),
                ShowInTaskbar = false,
            };

            form.Show();
            await form.Loading;

            Assert.Contains("读取英雄失败", List(form).EmptyText, StringComparison.Ordinal);
        });
    }

    private static LeagueConfig Config(params PreliminaryHero[] heroes) =>
        new() { Preliminary = new PreliminaryConfig { Heroes = [.. heroes] } };

    private static PreliminaryHero Hero(int championId, int priority) =>
        new() { ChampionId = championId, Priority = priority };

    private static Preliminary Window(LeagueConfig config, IPreliminarySource source, Action? save = null) =>
        new(config, source, save)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-30000, -30000),
            ShowInTaskbar = false,
        };

    private static void Activate(Form form, int championId)
    {
        Grid(form).SelectChampion(championId);
        WindowMessages.Key(Grid(form), Keys.Enter);
    }

    private static ChampionGridView<PrimaryChampion> Grid(Form form) =>
        Descendants(form).OfType<ChampionGridView<PrimaryChampion>>().Single();

    private static PreliminaryListView List(Form form) => Descendants(form).OfType<PreliminaryListView>().Single();

    private static TextBox Search(Form form) => Descendants(form).OfType<TextBox>().Single();

    private static PrimaryChampion Champion(int championId) =>
        Champions().Single(champion => champion.Id == championId);

    private static Control ClearButton(Form form) =>
        Descendants(form).OfType<Control>().Single(control => control.Text == "清空当前列表");

    /// <summary>The row of lane buttons is the one that also holds the search box.</summary>
    private static FlowLayoutPanel LaneBar(Form form) =>
        Descendants(form).OfType<FlowLayoutPanel>().Single(panel => panel.Controls.OfType<TextBox>().Any());

    /// <summary>The row of list tabs holds one button per preselection list, in the order of that list.</summary>
    private static Control Tab(Form form, string key) =>
        Descendants(form)
            .OfType<FlowLayoutPanel>()
            .Single(panel => panel.Controls.Count == PreliminaryLists.All.Count)
            .Controls[PreliminaryLists.All.ToList().FindIndex(list => list.Key == key)];

    private static Control LaneButton(Form form, string text) =>
        LaneBar(form).Controls.OfType<Control>().Single(control => control.Text == text);

    private static void Click(Control control) =>
        WindowMessages.Click(control, new Point(control.Width / 2, control.Height / 2));

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));

    /// <summary>Waits for a positive signal — an icon that arrived — with a deadline, pumping the message loop.</summary>
    private static async Task WaitForAsync(Func<bool> ready)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ready() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(ready(), "The window finished loading within five seconds.");
    }

    private static List<PrimaryChampion> Champions() =>
        [
            new()
            {
                Id = 1,
                Name = "黑暗之女",
                Title = "安妮",
                Alias = "Annie",
            },
            new()
            {
                Id = 22,
                Name = "寒冰射手",
                Title = "艾希",
                Alias = "Ashe",
            },
            new()
            {
                Id = 64,
                Name = "盲僧",
                Title = "李青",
                Alias = "LeeSin",
            },
            new()
            {
                Id = 86,
                Name = "德玛西亚之力",
                Title = "盖伦",
                Alias = "Garen",
            },
            new()
            {
                Id = 99,
                Name = "光辉女郎",
                Title = "拉克丝",
                Alias = "Lux",
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
        ];

    /// <summary>Enough champions to make the icon loop repaint the grid on its twelfth icon.</summary>
    private static List<PrimaryChampion> ManyChampions() =>
        [
            .. Champions(),
            .. Enumerable
                .Range(1, 6)
                .Select(index => new PrimaryChampion
                {
                    Id = 800 + index,
                    Name = $"备用英雄{index}",
                    Title = $"备用{index}",
                    Alias = $"Backup{index}",
                }),
        ];

    private sealed class StubSource(IReadOnlyList<PrimaryChampion> champions) : IPreliminarySource
    {
        private readonly List<Image> _handedOut = new();

        public IReadOnlyList<PrimaryChampion> Champions { get; } = champions;

        /// <summary>Thrown instead of returning the champion list.</summary>
        public Exception? ReadFailure { get; set; }

        /// <summary>Holds the champion read until the test releases it.</summary>
        public TaskCompletionSource? ReadGate { get; set; }

        public HashSet<int> MissingIcons { get; } = new();

        /// <summary>Runs at the start of an icon request, so a test can cancel the window mid-load.</summary>
        public Action? OnIconRequest { get; set; }

        public int IconRequests { get; private set; }

        public int Outstanding => _handedOut.Count(IsAlive);

        public Task<List<PrimaryChampion>> LoadChampionsAsync(CancellationToken cancellationToken) =>
            ReadAsync(cancellationToken);

        public Task<Image?> LoadIconAsync(int championId, CancellationToken cancellationToken)
        {
            IconRequests++;
            OnIconRequest?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (MissingIcons.Contains(championId))
            {
                return Task.FromResult<Image?>(null);
            }

            Image image = Generated(championId);
            _handedOut.Add(image);
            return Task.FromResult<Image?>(image);
        }

        private async Task<List<PrimaryChampion>> ReadAsync(CancellationToken cancellationToken)
        {
            TaskCompletionSource? gate = ReadGate;
            if (gate != null)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            if (ReadFailure is { } failure)
            {
                throw failure;
            }

            return [.. Champions];
        }

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
