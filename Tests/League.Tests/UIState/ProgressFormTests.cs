using League.Tests.Support;
using League.UIState;

namespace League.Tests.UIState;

/// <summary>
/// The update window: a status line and a progress bar, plus the wording it opens with.
///
/// The members are read back through the public surface - the label and the bar are reachable as the form's controls -
/// and every case runs on the UI host so the form's own thread affinity is honoured.
/// </summary>
[Trait("Category", "UI")]
public class ProgressFormTests
{
    private static Label Status(ProgressForm form) => form.Controls.OfType<Label>().Single();

    private static ProgressBar Bar(ProgressForm form) => form.Controls.OfType<ProgressBar>().Single();

    // ---- how it opens ----

    [Fact]
    public void Constructor_OpensWithTheDownloadNotice()
    {
        UiHost.Run(_ =>
        {
            using ProgressForm form = new();

            Assert.Equal(
                "正在从 GitHub 下载更新包...\r\n(网络环境不同，下载速度可能较慢，请耐心等待)\r\n请勿关闭此窗口",
                Status(form).Text
            );
        });
    }

    [Fact]
    public void Constructor_TheNoticeIsLaidOutForThreeLines()
    {
        UiHost.Run(_ =>
        {
            using ProgressForm form = new();

            Assert.False(Status(form).AutoSize, "The notice wraps onto three lines, so it cannot size itself.");
            Assert.Equal(new Size(520, 120), Status(form).Size);
            Assert.Equal(ContentAlignment.MiddleLeft, Status(form).TextAlign);
        });
    }

    [Fact]
    public void Constructor_TheWindowIsAFixedDialogThatCannotBeMaximisedOrMinimised()
    {
        UiHost.Run(_ =>
        {
            using ProgressForm form = new();

            Assert.Equal("更新中...", form.Text);
            Assert.Equal(FormBorderStyle.FixedDialog, form.FormBorderStyle);
            Assert.False(form.MaximizeBox);
            Assert.False(form.MinimizeBox);
        });
    }

    [Fact]
    public void Constructor_TheBarMarqueesUntilAProportionIsKnown()
    {
        UiHost.Run(_ =>
        {
            using ProgressForm form = new();

            Assert.Equal(ProgressBarStyle.Marquee, Bar(form).Style);
        });
    }

    // ---- the status line ----

    [Fact]
    public void UpdateStatus_AMessageWithoutTheSource_GetsTheSourcePrefixed()
    {
        UiHost.Run(_ =>
        {
            using ProgressForm form = new();

            form.UpdateStatus("正在校验更新包...");

            Assert.Equal("从 GitHub 正在校验更新包...", Status(form).Text);
        });
    }

    [Fact]
    public void UpdateStatus_AMessageThatAlreadyNamesTheSource_IsLeftAlone()
    {
        // The prefix exists so the source is never lost; adding it twice would read badly.
        UiHost.Run(_ =>
        {
            using ProgressForm form = new();

            form.UpdateStatus("已从 GitHub 下载完成");

            Assert.Equal("已从 GitHub 下载完成", Status(form).Text);
        });
    }

    [Fact]
    public void UpdateStatus_AMessageNamingTheSourceElsewhere_IsAlsoLeftAlone()
    {
        UiHost.Run(_ =>
        {
            using ProgressForm form = new();

            form.UpdateStatus("校验 GitHub 上的版本号");

            Assert.Equal("校验 GitHub 上的版本号", Status(form).Text);
        });
    }

    // ---- the bar ----

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void UpdateStatus_APercent_ShowsBlocksAtThatValue(int percent)
    {
        UiHost.Run(_ =>
        {
            using ProgressForm form = new();

            form.UpdateStatus("下载中", percent);

            Assert.Equal(ProgressBarStyle.Blocks, Bar(form).Style);
            Assert.Equal(percent, Bar(form).Value);
        });
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(150, 100)]
    [InlineData(int.MinValue, 0)]
    [InlineData(int.MaxValue, 100)]
    public void UpdateStatus_APercentOutsideTheRange_IsClamped(int percent, int expected)
    {
        UiHost.Run(_ =>
        {
            using ProgressForm form = new();

            form.UpdateStatus("下载中", percent);

            Assert.Equal(expected, Bar(form).Value);
        });
    }

    [Fact]
    public void UpdateStatus_WithoutAPercent_GoesBackToMarqueing()
    {
        // An unknown proportion is shown as movement; the bar returns to that after a numbered step.
        UiHost.Run(_ =>
        {
            using ProgressForm form = new();
            form.UpdateStatus("下载中", 40);

            form.UpdateStatus("正在解压");

            Assert.Equal(ProgressBarStyle.Marquee, Bar(form).Style);
        });
    }

    [Fact]
    public void UpdateStatus_WithoutAPercent_LeavesTheValueWhereItWas()
    {
        UiHost.Run(_ =>
        {
            using ProgressForm form = new();
            form.UpdateStatus("下载中", 40);

            form.UpdateStatus("正在解压");

            Assert.Equal(40, Bar(form).Value);
        });
    }

    // ---- completing ----

    [Fact]
    public void Complete_ShowsTheWholeBarAndAFinalMessage()
    {
        // The close that follows a second and a half later is a side effect this does not wait for; it is guarded by
        // an IsDisposed check, so disposing the form here is safe.
        UiHost.Run(_ =>
        {
            using ProgressForm form = new();

            form.Complete();

            Assert.Equal(ProgressBarStyle.Blocks, Bar(form).Style);
            Assert.Equal(100, Bar(form).Value);
            Assert.Equal("从 GitHub 更新完成，正在重启...", Status(form).Text);
        });
    }

    [Fact]
    public void Complete_WithAMessageOfItsOwn_UsesThatMessage()
    {
        UiHost.Run(_ =>
        {
            using ProgressForm form = new();

            form.Complete("更新完成");

            Assert.Equal("从 GitHub 更新完成", Status(form).Text);
        });
    }
}
