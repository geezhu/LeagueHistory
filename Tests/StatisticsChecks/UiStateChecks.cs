using System.Reflection;
using League.Controls;
using League.Infrastructure;
using League.UIState;
using static StatisticsCheckSupport;

/// <summary>
/// The main window's own UI state: the fading loading overlay, the status panels for a missing client or a game
/// that has not started, and the tab bookkeeping. The window is never shown and no client is connected, so every
/// panel here is built from the form's own controls.
/// </summary>
internal static class UiStateChecks
{
    public static void Run() => WinFormsCheckHost.Run(Core);

    private static void Core()
    {
        StaticInvokeChecks();
        using var owner = new League.FormMain();
        using var hotkeys = (IDisposable)Hotkeys.GetValue(owner)!; // The window's global hotkeys are not wanted here.
        var state = new FormUiStateManager(owner);

        LoadingChecks(owner, state);
        StatusChecks(owner, state);
        TabChecks(state);
        HoverChecks(state);
        Check(
            !state.IsGame && !state.LcuReady,
            "A fresh window reports neither a game nor a client, so the game-flow watcher stays idle"
        );
        Console.WriteLine(
            "PASS: guarded cross-thread calls, the loading overlay's fade in and out, the client and waiting "
                + "status panels, tab polling and tab hover."
        );
    }

    private static void StaticInvokeChecks()
    {
        int ran = 0;
        FormUiStateManager.SafeInvoke((Control?)null, () => ran++);
        FormUiStateManager.SafeInvoke((Form?)null!, () => ran++);
        FormUiStateManager.SafeInvoke(new Panel(), null!);
        FormUiStateManager.SafeInvokeSync((Control?)null, () => ran++);
        FormUiStateManager.SafeInvokeSync(new Panel(), null!);
        var disposed = new Panel();
        disposed.Dispose();
        FormUiStateManager.SafeInvoke(disposed, () => ran++);
        Check(ran == 0, "A missing control, a disposed control and a missing action are all ignored");

        using var host = HiddenForm(new Size(400, 300));
        var panel = new Panel();
        host.Controls.Add(panel);
        host.Show();

        FormUiStateManager.SafeInvoke(panel, () => ran++);
        Check(ran == 1, "An action on the control's own thread runs at once");

        bool marshalled = false;
        Task.Run(() => FormUiStateManager.SafeInvoke(panel, () => marshalled = !panel.InvokeRequired))
            .GetAwaiter()
            .GetResult();
        PumpUntil(() => marshalled);
        Check(marshalled, "An action from another thread is posted to the thread that owns the control");

        FormUiStateManager.SafeInvoke(panel, () => throw new InvalidOperationException("检查用异常"));
        FormUiStateManager.SafeInvoke(panel, () => ran++);
        Check(ran == 2, "A throwing action is logged and dropped, and later actions still run");

        FormUiStateManager.SafeInvokeSync(panel, () => ran++);
        Check(ran == 3, "The synchronous call runs an action on the owning thread");

        var bare = new Panel();
        int thread = 0;
        Task.Run(() => FormUiStateManager.SafeInvoke(bare, () => thread = Environment.CurrentManagedThreadId))
            .GetAwaiter()
            .GetResult();
        Check(
            thread != 0 && thread != Environment.CurrentManagedThreadId,
            "A control that was never given a handle is written to on the calling thread"
        );
        host.Close();
    }

    private static void LoadingChecks(League.FormMain owner, FormUiStateManager state)
    {
        state.ShowLoadingIndicator();
        var overlay = Overlay(state);
        Check(overlay != null && owner.Controls.Contains(overlay), "The loading indicator joins the window");
        Check(
            overlay!.Controls.OfType<Label>().Any(label => label.Text == "正在加载，请稍候..."),
            "The loading indicator says what it is waiting for"
        );
        PumpUntil(() => overlay.BackColor.A == 160);
        Check(overlay.BackColor.A == 160, "The loading indicator fades in and stops at its final transparency");

        state.ShowLoadingIndicator();
        Check(ReferenceEquals(Overlay(state), overlay), "Asking for the indicator while it shows changes nothing");

        state.HideLoadingIndicator();
        PumpUntil(() => Overlay(state) == null);
        Check(Overlay(state) == null, "The loading indicator fades out and is removed");
        Check(!owner.Controls.Contains(overlay), "The removed indicator is no longer part of the window");
    }

    private static void StatusChecks(League.FormMain owner, FormUiStateManager state)
    {
        state.SetLcuUiState(connected: false, inGame: false);
        var disconnected = PanelSaying(owner.panelMatchList, "客户端");
        Check(disconnected != null, "A client that is not connected explains itself on the match list");
        Check(disconnected!.Controls.OfType<ProgressBar>().Any(), "The disconnected status waits with an animation");
        Check(PanelSaying(owner.penalGameMatchData, "客户端") != null, "The card area shows the same status");

        state.SetLcuUiState(connected: false, inGame: false);
        Check(
            owner.panelMatchList.Controls.OfType<Panel>().Count() == 1,
            "Asking again replaces the status instead of stacking a second one"
        );

        state.SetLcuUiState(connected: true, inGame: false);
        Check(PanelSaying(owner.panelMatchList, "客户端") == null, "A connected client clears the match list status");
        var waiting = WaitingPanel(state);
        Check(waiting != null, "Waiting for a game shows a panel of its own");
        Check(PanelSaying(owner.penalGameMatchData, "正在等待加入游戏") != null, "The waiting panel explains itself");
        Check(!owner.tableLayoutPanel1.Visible, "Waiting for a game hides the team cards");

        state.SetLcuUiState(connected: true, inGame: false);
        Check(
            !ReferenceEquals(WaitingPanel(state), waiting) && waiting!.IsDisposed,
            "Waiting again replaces and disposes the previous panel"
        );

        state.SetLcuUiState(connected: true, inGame: true);
        Check(
            PanelSaying(owner.penalGameMatchData, "正在等待加入游戏") == null,
            "A running game clears the waiting panel"
        );
    }

    private static void TabChecks(FormUiStateManager state)
    {
        var poller = new Poller();
        state.HandleTabSelectionChanged(1, poller);
        var starting = (CancellationTokenSource?)Cancellation.GetValue(poller);
        Check(
            starting != null && !starting.IsCancellationRequested,
            "The match list tab starts the polling that keeps the status fresh"
        );

        state.HandleTabSelectionChanged(0, poller);
        Check(starting!.IsCancellationRequested, "Leaving the tab stops the polling");

        var untouched = new Poller();
        state.HandleTabSelectionChanged(3, untouched);
        state.HandleTabSelectionChanged(99, untouched);
        Check(
            (CancellationTokenSource?)Cancellation.GetValue(untouched) == null,
            "A tab without polling never starts the poller"
        );
    }

    private static void HoverChecks(FormUiStateManager state)
    {
        using var host = HiddenForm(new Size(400, 300));
        TabControl tabs = new ImageTabControl();
        tabs.TabPages.Add(new TabPage("一"));
        tabs.TabPages.Add(new TabPage("二"));
        host.Controls.Add(tabs);
        host.Show();

        Rectangle first = tabs.GetTabRect(0);
        var over = new MouseEventArgs(MouseButtons.None, 0, first.X + 2, first.Y + 2, 0);
        state.HandleImageTabControlMouseMove(tabs, over);
        Check((int)LastIndex.GetValue(state)! == 0, "Hovering a tab remembers which one it is");
        state.HandleImageTabControlMouseMove(tabs, over);
        Check((int)LastIndex.GetValue(state)! == 0, "Hovering the same tab again is not a change");

        state.HandleImageTabControlMouseMove(
            tabs,
            new MouseEventArgs(MouseButtons.None, 0, 2, host.ClientSize.Height - 4, 0)
        );
        Check((int)LastIndex.GetValue(state)! == -1, "Leaving the tabs forgets the hovered one");

        state.HandleImageTabControlMouseMove(null, over);
        Check((int)LastIndex.GetValue(state)! == -1, "A hover without a tab control is ignored");
        host.Close();
    }

    private static Panel? PanelSaying(Control host, string text) =>
        host
            .Controls.OfType<Panel>()
            .FirstOrDefault(panel =>
                panel.Controls.OfType<Label>().Any(label => label.Text.Contains(text, StringComparison.Ordinal))
            );

    private static Panel? Overlay(FormUiStateManager state) => (Panel?)LoadingOverlay.GetValue(state);

    private static Panel? WaitingPanel(FormUiStateManager state) => (Panel?)Waiting.GetValue(state);

    private static readonly FieldInfo LoadingOverlay = typeof(FormUiStateManager).GetField(
        "_loadingOverlay",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    private static readonly FieldInfo Waiting = typeof(FormUiStateManager).GetField(
        "_waitingPanel",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    private static readonly FieldInfo LastIndex = typeof(FormUiStateManager).GetField(
        "_lastIndex",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    private static readonly FieldInfo Hotkeys = typeof(League.FormMain).GetField(
        "_hotkeyManager",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    private static readonly FieldInfo Cancellation = typeof(Poller).GetField(
        "_cts",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;
}
