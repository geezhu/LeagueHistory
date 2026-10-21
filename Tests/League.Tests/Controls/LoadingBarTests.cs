using League.Controls;
using League.Tests.Support;

namespace League.Tests.Controls;

/// <summary>
/// The thin accent loading bar: it is still a progress bar, it sweeps only while it is on screen, and each sweep step
/// moves the segment along. Steps are awaited through the repaint each one asks for, not through a fixed delay.
/// </summary>
[Trait("Category", "UI")]
public class LoadingBarTests
{
    [Fact]
    public void Show_InAWindow_StartsSweeping()
    {
        UiHost.Run(host =>
        {
            using var bar = Bar(host);

            Assert.True(bar.Sweeping);
        });
    }

    [Fact]
    public void Hide_WhileSweeping_StopsTheSweep()
    {
        UiHost.Run(host =>
        {
            using var bar = Bar(host);

            bar.Visible = false;

            Assert.False(bar.Sweeping);
        });
    }

    [Fact]
    public void New_NotYetShown_DoesNotSweep()
    {
        UiHost.Run(_ =>
        {
            using var bar = new LoadingBar();

            Assert.False(bar.Sweeping);
        });
    }

    [Fact]
    public void Sweep_OneStep_MovesTheSegmentForward()
    {
        UiHost.Run(async host =>
        {
            using var bar = Bar(host);
            float start = bar.Position;
            var stepped = new TaskCompletionSource();
            bar.Invalidated += (_, _) => stepped.TrySetResult();

            // The delay only bounds a sweep that never ticks.
            await Task.WhenAny(stepped.Task, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.NotEqual(start, bar.Position);
        });
    }

    [Fact]
    public void New_Bar_IsAMarqueeProgressBar()
    {
        UiHost.Run(_ =>
        {
            using var bar = new LoadingBar();

            Assert.Equal(ProgressBarStyle.Marquee, bar.Style);
        });
    }

    private static LoadingBar Bar(Form host)
    {
        var bar = new LoadingBar { Width = 200 };
        host.Controls.Add(bar);
        return bar;
    }
}
