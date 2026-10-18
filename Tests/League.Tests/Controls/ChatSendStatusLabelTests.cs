using League.Controls;
using League.Services;
using League.Tests.Support;

namespace League.Tests.Controls;

/// <summary>
/// The status line next to the quick-send settings: it shows the last stage the chat sender reported, and follows
/// every later report. Reporting goes through the shared diagnostics the application owns, so these tests join the
/// collection for process-wide state; one report appends one line to the application's own diagnostics log, the same
/// file a quick send writes to, and nothing else is touched. The reported stage is left behind because the shared
/// status has no way to be put back.
/// </summary>
[Collection(EnvironmentCollection.Name)]
[Trait("Category", "UI")]
public class ChatSendStatusLabelTests
{
    [Fact]
    public void HandleCreation_Label_ShowsTheLastReportedStage()
    {
        UiHost.Run(host =>
        {
            using var label = new ChatSendStatusLabel();
            host.Controls.Add(label);
            var current = ChatSendDiagnostics.Shared.Current;

            Assert.Equal($"快捷发送 · {current.Time:HH:mm:ss} · {current.Message}", label.Text);
        });
    }

    [Fact]
    public void Report_ANewStage_UpdatesTheLabelToIt()
    {
        UiHost.Run(host =>
        {
            using var label = new ChatSendStatusLabel();
            host.Controls.Add(label);

            ChatSendDiagnostics.Shared.Report("checks", "已发送阵容分析");
            var current = ChatSendDiagnostics.Shared.Current;

            Assert.Equal("已发送阵容分析", current.Message);
            Assert.Equal($"快捷发送 · {current.Time:HH:mm:ss} · 已发送阵容分析", label.Text);
        });
    }

    [Fact]
    public void Report_FromAnotherThread_UpdatesTheLabelOnTheUiThread()
    {
        UiHost.Run(async host =>
        {
            using var label = new ChatSendStatusLabel();
            host.Controls.Add(label);

            await Task.Run(() => ChatSendDiagnostics.Shared.Report("checks", "后台阶段"));
            await WaitFor(() => label.Text.Contains("后台阶段"));

            Assert.Contains("后台阶段", label.Text);
        });
    }

    [Fact]
    public void AutoSize_Label_KeepsItsOwnSize()
    {
        UiHost.Run(host =>
        {
            using var label = new ChatSendStatusLabel();
            host.Controls.Add(label);

            Assert.True(label.AutoSize, "The status line sizes itself to its text.");
        });
    }

    private static async Task WaitFor(Func<bool> ready)
    {
        for (int attempt = 0; attempt < 200 && !ready(); attempt++)
        {
            await Task.Delay(5);
        }
    }
}
