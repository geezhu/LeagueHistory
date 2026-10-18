using League.Controls;
using League.Models;
using League.Tests.Support;

namespace League.Tests.Controls;

/// <summary>
/// Team analysis from arammeta's trained statistics: the estimated win rate with its parts, the seven capabilities
/// against their gap thresholds, teammate synergy and the swap suggestions. The statistics come from the captured
/// fixture (no network) and the champion names from a stub, so every line is checkable.
/// </summary>
[Trait("Category", "UI")]
public class ChampionCompositionViewTests
{
    [Fact]
    public void SetRoster_MayhemRoster_LeadsWithTheEstimatedWinRateAndEveryCapability()
    {
        UiHost.Run(async host =>
        {
            using var view = View(host, Index());
            view.SetRoster(Roster());

            await view.Loading;

            Assert.Contains(Text(view), text => text.StartsWith("估计胜率 "));
            Assert.Equal(7, view.Controls.OfType<Panel>().Count());
            Assert.StartsWith("【海克斯大乱斗·阵容分析】", view.ChatMessage);
            Assert.StartsWith("【海克斯大乱斗·阵容能力】", view.GradesMessage);
        });
    }

    [Fact]
    public void DrawToBitmap_CapabilityRows_PaintsEveryBarAgainstItsThreshold()
    {
        UiHost.Run(async host =>
        {
            using var view = View(host, Index());
            view.SetRoster(Roster());
            await view.Loading;

            using var painted = new Bitmap(view.Width, view.Height);
            view.DrawToBitmap(painted, new Rectangle(Point.Empty, view.Size));

            Assert.Equal(view.Size, painted.Size);
            Assert.Equal(7, view.Controls.OfType<Panel>().Count());
        });
    }

    [Fact]
    public void SetRoster_MayhemRoster_ComparesEveryBenchChampionInOrderOfTheWinRateChange()
    {
        UiHost.Run(async host =>
        {
            using var view = View(host, Index());
            var roster = Roster();
            view.SetRoster(roster);

            await view.Loading;

            var swaps = Text(view).Where(text => text.StartsWith("换成 ")).ToArray();
            var expected = roster
                .Bench.Select(id =>
                    AramTeamScore.Evaluate(AramTeamScoreFixture.Index, [id, .. Ids.Skip(1).Take(4)]).EstimatedWinRate
                    - AramTeamScore.Evaluate(AramTeamScoreFixture.Index, [.. Ids.Take(5)]).EstimatedWinRate
                )
                .OrderByDescending(delta => delta)
                .ToArray();
            Assert.Equal(roster.Bench.Length, swaps.Length);
            Assert.Contains(Signed(expected[0]), swaps[0]);
            Assert.Contains(Signed(expected[^1]), swaps[^1]);
        });
    }

    [Fact]
    public void SetRoster_NormalAram_LeavesOutTheMayhemTrainedEstimate()
    {
        UiHost.Run(async host =>
        {
            using var view = View(host, Index(), mayhem: false);
            view.SetRoster(Roster());

            await view.Loading;

            Assert.DoesNotContain(Text(view), text => text.Contains("估计胜率 "));
            Assert.Contains(Text(view), text => text.Contains("不显示估计胜率"));
            Assert.StartsWith("【大乱斗·阵容分析】", view.ChatMessage);
            Assert.DoesNotContain("估计胜率", view.ChatMessage);
        });
    }

    [Fact]
    public void SetRoster_StatisticsUnavailable_OffersARetryAndNothingToSend()
    {
        UiHost.Run(async host =>
        {
            using var view = View(host, Failing());

            view.SetRoster(Roster());
            await view.Loading;

            Assert.Contains(Text(view), text => text.EndsWith("点击重试"));
            Assert.Null(view.ChatMessage);
            Assert.Null(view.GradesMessage);
        });
    }

    [Fact]
    public void Click_RetryLine_LoadsTheStatisticsAgain()
    {
        int attempts = 0;
        UiHost.Run(async host =>
        {
            using var view = View(
                host,
                _ =>
                    ++attempts == 1
                        ? Task.FromException<AramMetaIndex>(new HttpRequestException("offline"))
                        : Task.FromResult(AramTeamScoreFixture.Index)
            );
            view.SetRoster(Roster());
            await view.Loading;
            var retry = view.Controls.OfType<Label>().Single(label => label.Text.EndsWith("点击重试"));

            WindowMessages.Click(retry, new Point(1, 1));
            await view.Loading;

            Assert.Equal(2, attempts);
            Assert.Contains(Text(view), text => text.StartsWith("估计胜率 "));
            Assert.StartsWith("【海克斯大乱斗·阵容分析】", view.ChatMessage);
        });
    }

    [Fact]
    public void SetRoster_StatisticsCancelled_PublishesNoAnalysis()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        UiHost.Run(async host =>
        {
            using var view = View(host, Index(), lifetime: cancelled.Token);

            view.SetRoster(Roster());
            await view.Loading;

            Assert.Null(view.ChatMessage);
            Assert.DoesNotContain(Text(view), text => text.StartsWith("估计胜率 "));
        });
    }

    [Fact]
    public void SetRoster_NoOwnPick_AsksForOneBeforeComparingSwaps()
    {
        UiHost.Run(async host =>
        {
            using var view = View(host, Index());
            var roster = Roster() with { Team = [.. Roster().Team.Select(entry => entry with { IsSelf = false })] };

            view.SetRoster(roster);
            await view.Loading;

            Assert.Contains(Text(view), text => text.Contains("选择英雄后可比较交换池中的英雄"));
            Assert.DoesNotContain(Text(view), text => text.StartsWith("换成 "));
        });
    }

    [Fact]
    public void SetRoster_EmptyBench_SaysTheBenchHasNoChampions()
    {
        UiHost.Run(async host =>
        {
            using var view = View(host, Index());

            view.SetRoster(Roster(bench: []));
            await view.Loading;

            Assert.Contains(Text(view), text => text.Contains("交换池暂无英雄"));
            Assert.DoesNotContain(Text(view), text => text.StartsWith("换成 "));
        });
    }

    [Fact]
    public void SetRoster_TeamWithoutPairHistory_SaysThePairsAreCountedAsZero()
    {
        UiHost.Run(async host =>
        {
            using var view = View(host, Index());

            view.SetRoster(new ChampionSelectRoster([new ChampionSelectRosterEntry(Ids[0], 0, true, false)], [], true));
            await view.Loading;

            Assert.Contains(Text(view), text => text.Contains("暂无这些队友组合的历史数据"));
        });
    }

    [Fact]
    public void SetRoster_ChampionOutsideTheStatistics_NamesWhatIsMissing()
    {
        UiHost.Run(async host =>
        {
            using var view = View(host, Index(), name: id => $"英雄 {id}");
            var roster = new ChampionSelectRoster([new ChampionSelectRosterEntry(999999, 0, true, false)], [], true);

            view.SetRoster(roster);
            await view.Loading;

            Assert.Contains(Text(view), text => text.Contains("缺少统计：英雄 999999"));
        });
    }

    [Fact]
    public void Resize_TheView_LaysTheRowsOutAgain()
    {
        UiHost.Run(async host =>
        {
            using var view = View(host, Index());
            view.SetRoster(Roster());
            await view.Loading;
            var wide = view.Controls.OfType<Label>().First().MaximumSize.Width;

            host.ClientSize = new Size(400, 720);

            // The rows are as wide as the view allows: 400 - 32 pixels of padding - 24.
            Assert.Equal(560, wide);
            Assert.Equal(344, view.Controls.OfType<Label>().First().MaximumSize.Width);
        });
    }

    [Fact]
    public void SetRoster_SwapThatFixesALack_NamesWhatItFixes()
    {
        UiHost.Run(async host =>
        {
            using var view = View(host, Index());

            // A roster whose bench holds a champion that covers a capability the team is missing.
            view.SetRoster(Roster(team: [4, 35, 63, 112, 134], bench: [37, 102, 157, 202, 876]));
            await view.Loading;

            Assert.Contains(Text(view), text => text.Contains("补续航", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void SetRoster_SwapThatIntroducesALack_NamesTheNewLack()
    {
        UiHost.Run(async host =>
        {
            using var view = View(host, Index());

            view.SetRoster(Roster(team: [4, 35, 37, 102, 112], bench: [63, 134, 157, 202, 876]));
            await view.Loading;

            Assert.Contains(Text(view), text => text.Contains("新短板 清线", StringComparison.Ordinal));
        });
    }

    private static int[] Ids => [.. AramTeamScoreFixture.Index.Champions.Select(champion => champion.Id)];

    private static Func<CancellationToken, Task<AramMetaIndex>> Index() =>
        _ => Task.FromResult(AramTeamScoreFixture.Index);

    private static Func<CancellationToken, Task<AramMetaIndex>> Failing() =>
        _ => Task.FromException<AramMetaIndex>(new HttpRequestException("offline"));

    private static ChampionSelectRoster Roster(int[]? bench = null, int[]? team = null) =>
        new(
            [
                .. (team ?? [.. Ids.Take(5)]).Select(
                    (id, slot) => new ChampionSelectRosterEntry(id, slot, slot == 0, false)
                ),
            ],
            bench ?? [.. Ids.Skip(5)],
            true
        );

    private static string[] Text(ChampionCompositionView view) =>
        [.. view.Controls.OfType<Label>().Select(label => label.Text)];

    private static string Signed(double value) =>
        Math.Abs(value) < 0.0005 ? "±0.0%" : (value > 0 ? "+" : "−") + Math.Abs(value).ToString("P1");

    private static ChampionCompositionView View(
        Form host,
        Func<CancellationToken, Task<AramMetaIndex>> load,
        bool mayhem = true,
        Func<int, string>? name = null,
        CancellationToken lifetime = default
    )
    {
        host.ClientSize = new Size(620, 720);
        var view = new ChampionCompositionView(name ?? Name, load, mayhem, lifetime) { Dock = DockStyle.Fill };
        host.Controls.Add(view);
        return view;
    }

    private static string Name(int id) =>
        AramTeamScoreFixture.Index.Champions.First(champion => champion.Id == id).Name;
}
