using System.Reflection;
using League.Services;

internal static class ShutdownUiChecks
{
    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new Exception(message);
        }
    }

    public static void Run() =>
        WinFormsCheckHost.Run(() =>
        {
            Render();
            RefreshSerializationAndClosure();
        });

    private static void Render()
    {
        using var owner = new League.FormMain();
        using var preview = new Form { ClientSize = new Size(760, 400) };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };
        preview.Controls.Add(flow);
        var store = new ReadOnlyTasks();
        var service = new ScheduledShutdownService(tasks: store);
        typeof(League.FormMain)
            .GetMethod("ConfigureShutdownSettings", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(owner, new object[] { flow, service });
        var summary = (Label)flow.Controls.Find("ShutdownPlanSummary", true).Single();
        var status = (Label)flow.Controls.Find("ShutdownStatus", true).Single();
        var create = (Button)flow.Controls.Find("CreateShutdownPlan", true).Single();
        var delete = (Button)flow.Controls.Find("DeleteShutdownPlan", true).Single();
        var log = (Button)flow.Controls.Find("ShutdownLog", true).Single();
        preview.Show();
        try
        {
            Pump(() => summary.Text == "尚未创建每日关机计划");
            Check(
                create.Enabled && !delete.Enabled && log.Enabled && !status.Text.Contains("失败"),
                "An absent task enables creation without a read error"
            );
            var time = (DateTimePicker)flow.Controls.Find("ShutdownTime", true).Single();
            time.Value = DateTime.Today.AddHours(21);
            store.Error = new UnauthorizedAccessException("read-only test denied");
            Pump(() => summary.Text == "每日关机计划读取失败");
            Check(
                !create.Enabled && !delete.Enabled && log.Enabled && status.Text.Contains("失败"),
                "Read failures stop the loading display and disable task mutations"
            );
            store.Error = null;
            Pump(() => create.Enabled);
            Check(
                time.Value.Hour == 21 && summary.Text == "尚未创建每日关机计划",
                "Recovery keeps the user's unsaved time choice"
            );
            Check(store.Mutations == 0, "Opening and refreshing never create, enable, delete or stop a task");
            Console.WriteLine(
                "PASS: shutdown settings handle missing/read-failed/recovered tasks and retain unsaved input. No system task changed."
            );
        }
        finally
        {
            preview.Close();
        }
    }

    private static void RefreshSerializationAndClosure()
    {
        using var owner = new League.FormMain();
        using var flow = new FlowLayoutPanel();
        using var tasks = new DelayedTasks();
        var type = typeof(League.FormMain).Assembly.GetType("League.Controls.ShutdownSettingsController")!;
        using var controller = (IDisposable)
            Activator.CreateInstance(type, owner, owner.Font, new ScheduledShutdownService(tasks: tasks))!;
        var controls = (Control[])type.GetProperty("Content")!.GetValue(controller)!;
        typeof(League.FormMain)
            .GetMethod("SettingsCard", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(owner, new object[] { flow, "模拟计划", controls });
        type.GetMethod("Attach")!.Invoke(controller, null);
        var work = (Task)
            type.GetMethod("RefreshAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null)!;
        Pump(() => tasks.Started.IsSet);
        var create = controls
            .OfType<FlowLayoutPanel>()
            .SelectMany(row => row.Controls.OfType<Button>())
            .Single(button => button.Name == "CreateShutdownPlan");
        Check(!create.Enabled, "A pending refresh disables mutations so a stale read cannot overwrite a change.");
        typeof(Control)
            .GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(create, new object[] { EventArgs.Empty });
        Check(tasks.Mutations == 0, "An explicit event while reading still cannot mutate the plan.");
        flow.Dispose();
        tasks.Release.Set();
        Pump(() => work.IsCompleted);
        work.GetAwaiter().GetResult();
        Check(
            tasks.Mutations == 0 && controls.All(control => control.IsDisposed),
            "A late read after card disposal cannot resurrect UI or mutate the task."
        );
    }

    private sealed class DelayedTasks : IShutdownTaskStore, IDisposable
    {
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public int Mutations { get; private set; }

        public ShutdownTaskRecord? Read()
        {
            Started.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The simulated read was not released.");
            }
            return null;
        }

        public void Register(string xml) => Mutations++;

        public void Stop() => Mutations++;

        public void Delete() => Mutations++;

        public void SetEnabled(bool enabled) => Mutations++;

        public void Dispose()
        {
            Started.Dispose();
            Release.Dispose();
        }
    }

    private static void Pump(Func<bool> ready)
    {
        var until = DateTime.UtcNow.AddSeconds(9);
        while (!ready() && DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
        Check(ready(), "Shutdown settings update completes");
    }

    private sealed class ReadOnlyTasks : IShutdownTaskStore
    {
        public volatile Exception? Error;
        public int Mutations;

        public ShutdownTaskRecord? Read()
        {
            if (Error is { } error)
            {
                throw error;
            }

            return null;
        }

        public void Register(string xml) => Mutations++;

        public void Stop() => Mutations++;

        public void Delete() => Mutations++;

        public void SetEnabled(bool enabled) => Mutations++;
    }
}
