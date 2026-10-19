using League.ChampionSkin;
using League.PrimaryElection;
using League.Tests.Support;

namespace League.Tests.Forms;

/// <summary>
/// The champion alert window: champions are added from the left grid and removed from the right one, and every change
/// hands the whole list back to be saved. Driven through the control tree and window messages with a stub champion
/// source, so no client is involved.
/// </summary>
[Trait("Category", "UI")]
public class ChampionAlertPickerTests
{
    private readonly List<IReadOnlyList<int>> _saves = new();
    private int _previews;

    [Fact]
    public void Window_OnLoad_ShowsTheSavedChampionsOnTheRight()
    {
        UiHost.Run(async _ =>
        {
            using var form = Window(35, 103);

            form.Show();
            await form.Loading;

            Assert.Equal([35, 103], Watched(form).Items.Select(c => c.Id));
            Assert.Equal(3, Grid(form).Items.Count);
        });
    }

    [Fact]
    public void Window_SavedModeVariantId_ShowsTheChampion()
    {
        UiHost.Run(async _ =>
        {
            using var form = Window(60035);

            form.Show();
            await form.Loading;

            Assert.Equal([35], Watched(form).Items.Select(c => c.Id));
        });
    }

    [Fact]
    public void Window_ActivatingAChampionOnTheLeft_AddsAndSavesIt()
    {
        UiHost.Run(async _ =>
        {
            using var form = Window(35);
            form.Show();
            await form.Loading;

            Activate(Grid(form), 103);

            Assert.Equal([35, 103], Assert.Single(_saves));
            Assert.Equal([35, 103], Watched(form).Items.Select(c => c.Id));
        });
    }

    [Fact]
    public void Window_ActivatingAWatchedChampionOnTheLeft_ChangesNothing()
    {
        UiHost.Run(async _ =>
        {
            using var form = Window(35);
            form.Show();
            await form.Loading;

            Activate(Grid(form), 35);

            Assert.Empty(_saves);
            Assert.Equal([35], form.Watched);
        });
    }

    [Fact]
    public void Window_ActivatingAChampionOnTheRight_RemovesAndSavesIt()
    {
        UiHost.Run(async _ =>
        {
            using var form = Window(35, 103);
            form.Show();
            await form.Loading;

            Activate(Watched(form), 35);

            Assert.Equal([103], Assert.Single(_saves));
            Assert.Equal([103], Watched(form).Items.Select(c => c.Id));
        });
    }

    [Fact]
    public void Window_TheGridMark_FlagsOnlyWatchedChampions()
    {
        UiHost.Run(async _ =>
        {
            using var form = Window(35);
            form.Show();
            await form.Loading;

            var marked = Grid(form).Items.Where(Grid(form).Marked).Select(c => c.Id);

            Assert.Equal([35], marked);
        });
    }

    [Fact]
    public void Window_TestButton_PlaysThePreview()
    {
        UiHost.Run(async _ =>
        {
            using var form = Window();
            form.Show();
            await form.Loading;

            Click(Descendants(form).Single(control => control.Text == "试听提示音"));

            Assert.Equal(1, _previews);
        });
    }

    private ChampionAlertPicker Window(params int[] watched) =>
        new(watched, ids => _saves.Add(ids), () => _previews++, new StubSource())
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-30000, -30000),
            ShowInTaskbar = false,
        };

    private static void Activate(ChampionGridView<PrimaryChampion> grid, int championId)
    {
        grid.SelectChampion(championId);
        WindowMessages.Key(grid, Keys.Enter);
    }

    private static ChampionGridView<PrimaryChampion> Grid(Form form) => Grids(form).Panel1;

    private static ChampionGridView<PrimaryChampion> Watched(Form form) => Grids(form).Panel2;

    private static (ChampionGridView<PrimaryChampion> Panel1, ChampionGridView<PrimaryChampion> Panel2) Grids(Form form)
    {
        var split = Descendants(form).OfType<SplitContainer>().Single();
        return (
            Descendants(split.Panel1).OfType<ChampionGridView<PrimaryChampion>>().Single(),
            Descendants(split.Panel2).OfType<ChampionGridView<PrimaryChampion>>().Single()
        );
    }

    private static void Click(Control control) =>
        WindowMessages.Click(control, new Point(control.Width / 2, control.Height / 2));

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private sealed class StubSource : IPreliminarySource
    {
        public Task<List<PrimaryChampion>> LoadChampionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<List<PrimaryChampion>>([
                new()
                {
                    Id = 35,
                    Name = "恶魔小丑",
                    Title = "萨科",
                    Alias = "Shaco",
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
                    Id = 266,
                    Name = "暗裔剑魔",
                    Title = "亚托克斯",
                    Alias = "Aatrox",
                },
            ]);

        // No icons: the grids draw placeholders, and nothing is left to release.
        public Task<Image?> LoadIconAsync(int championId, CancellationToken cancellationToken) =>
            Task.FromResult<Image?>(null);
    }
}
