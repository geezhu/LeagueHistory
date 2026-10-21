using League.Controls;
using League.Services;
using League.Tests.Support;
using League.UIState;

namespace League.Tests.UIState;

/// <summary>
/// The main window's UI state helper: hovering a navigation tab shows its name in a tooltip that hides itself on a
/// 1.5 second timer. The tab control here is a stand-alone one in the host window; the helper only needs a main window
/// to exist, so one is built with everything outside the process switched off and never shown.
/// </summary>
[Trait("Category", "UI")]
[Collection(EnvironmentCollection.Name)]
public class FormUiStateManagerTests
{
    [Fact]
    public void TabHover_TabsDisposedBeforeTheTooltipHides_DoesNotCrashTheMessageLoop()
    {
        UiHost.Run(async host =>
        {
            using var main = Main();
            var state = new FormUiStateManager(main);
            var tabs = new ImageTabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(new TabPage("查看历史"));
            host.Controls.Add(tabs);
            // An off-screen window never really shows its tooltip, so the hide timer is never armed; the host is moved
            // onto the screen for the two seconds this takes.
            host.Location = new Point(0, 0);
            var tab = tabs.GetTabRect(0);
            state.HandleImageTabControlMouseMove(
                tabs,
                new MouseEventArgs(MouseButtons.None, 0, tab.X + 5, tab.Y + 5, 0)
            );

            tabs.Dispose();
            // Nothing signals the hide timer, so the loop is kept pumping past it. Before the fix the timer's message
            // hid the tooltip from the disposed tab control and threw ObjectDisposedException.
            await Task.Delay(TimeSpan.FromSeconds(2));

            Assert.True(tabs.IsDisposed);
        });
    }

    private static FormMain Main() =>
        new(
            new FormMainOptions
            {
                StartBackgroundServices = false,
                StartGameIntegration = false,
                ShutdownTasks = new NoShutdownTask(),
            }
        );

    private sealed class NoShutdownTask : IShutdownTaskStore
    {
        public ShutdownTaskRecord? Read() => null;

        public void Register(string xml) => throw new InvalidOperationException("Tests never register a task.");

        public void Stop() => throw new InvalidOperationException("Tests never stop a task.");

        public void Delete() => throw new InvalidOperationException("Tests never delete a task.");

        public void SetEnabled(bool enabled) => throw new InvalidOperationException("Tests never change a task.");
    }
}
