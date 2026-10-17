using System.Reflection;
using League.UIState;
using static StatisticsCheckSupport;

/// <summary>
/// The updater's progress window: its initial download hint, centring on the owner it was given, the status and
/// percentage it publishes (including from a background thread) and the delayed close that follows completion. The
/// window stays off screen and drives no update, so nothing is downloaded and no process is restarted.
/// </summary>
internal static class ProgressFormChecks
{
    public static void Run()
    {
        Centering();
        Updating();
        Completing();
        Console.WriteLine(
            "PASS: the update progress window starts with its download hint, centres on its owner and stays on "
                + "screen, publishes status lines and percentages with and without a value, marshals a background "
                + "report onto the interface thread and closes itself after completion."
        );
    }

    private static void Centering()
    {
        using var host = HiddenForm(new Size(300, 200));
        host.Show();
        using var loose = Window();
        Invoke(loose, "OnLoad", EventArgs.Empty);
        Check(loose.Location == new Point(-30000, -30000), "A window without an owner keeps its own place");

        using var owned = Window();
        owned.Owner = host;
        Invoke(owned, "OnLoad", EventArgs.Empty);
        var area = Screen.FromControl(host).WorkingArea;
        Check(
            owned.Left >= area.Left
                && owned.Right <= area.Right
                && owned.Top >= area.Top
                && owned.Bottom <= area.Bottom,
            "A window with an owner is centred and kept inside the screen: " + owned.Bounds
        );
        Check(owned.Left != -30000, "The window moved next to its owner");
    }

    private static void Updating()
    {
        using var form = Window();
        form.Show();
        Application.DoEvents();
        var status = Field<Label>(form, "lblStatus");
        var progress = Field<ProgressBar>(form, "pbProgress");
        Check(
            status.Text.Contains("下载更新包") && progress.Style == ProgressBarStyle.Marquee,
            "The window opens with the download hint and a moving bar"
        );

        form.UpdateStatus("正在下载更新包", 40);
        Check(
            status.Text.StartsWith("从 GitHub ", StringComparison.Ordinal)
                && progress.Style == ProgressBarStyle.Blocks
                && progress.Value == 40,
            "A status without a source is prefixed and sets the bar"
        );
        form.UpdateStatus("从 GitHub 下载很慢，请等待", null);
        Check(
            status.Text == "从 GitHub 下载很慢，请等待" && progress.Style == ProgressBarStyle.Marquee,
            "A message that already names the source keeps one prefix and returns to the moving bar"
        );
        form.UpdateStatus("超过上限", 500);
        Check(progress.Value == 100, "A percentage above a full bar is capped");
        form.UpdateStatus("低于下限", -5);
        Check(progress.Value == 0, "A negative percentage stops at zero");

        Task.Run(() => form.UpdateStatus("后台线程的报告", 10));
        PumpUntil(() => status.Text.Contains("后台线程"));
        Check(progress.Value == 10, "A report from another thread is marshalled onto the interface thread");
    }

    private static void Completing()
    {
        using var form = Window();
        form.Show();
        Application.DoEvents();
        form.Complete("下载完成，准备更新");
        Check(
            Field<Label>(form, "lblStatus").Text == "从 GitHub 下载完成，准备更新"
                && Field<ProgressBar>(form, "pbProgress").Value == 100,
            "Completion shows the final line and a full bar"
        );
        PumpUntil(() => form.IsDisposed);
        Check(form.IsDisposed, "Completion closes the window by itself");

        using var abandoned = Window();
        abandoned.Show();
        Application.DoEvents();
        abandoned.Complete();
        abandoned.Dispose();
        Settle();
        Check(abandoned.IsDisposed, "A window that is already closed ignores the delayed completion");
    }

    private static ProgressForm Window()
    {
        var form = new ProgressForm { ShowInTaskbar = false };
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-30000, -30000);
        return form;
    }

    /// <summary>Waits out the delayed close of a window this check already disposed.</summary>
    private static void Settle()
    {
        var until = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static void Invoke(object owner, string name, params object[] args) =>
        owner
            .GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(owner, args);

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
}
