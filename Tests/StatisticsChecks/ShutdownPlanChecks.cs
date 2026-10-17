using System.Reflection;
using System.Security.Principal;
using System.Xml.Linq;
using League.Services;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// Reading and maintaining the daily shutdown plan: the task view's running, status, skipped and original-script
/// variants, the guards that refuse a task the application does not own, the rollback when a save fails halfway and
/// the deletion that backs the original task up first. Every scheduler call goes to a fake store and every file into a
/// temporary folder, so no system task is read, created or run and no shutdown is armed.
/// </summary>
internal static class ShutdownPlanChecks
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private static readonly DateTime Now = new(2026, 9, 17, 23, 20, 0);

    public static void Run() => Task.Run(RunAsync).GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        string root = Temporary();
        try
        {
            Paths(root);
            await RunningTaskAsync(root);
            await SkippedAndUnmanagedAsync(root);
            await LatestStatusAsync(root);
            await UnreadableFilesAsync(root);
            await CreateGuardsAsync(root);
            await FailedSaveAsync(root);
            await DeletionAsync(root);
            Ownership();
        }
        finally
        {
            Cleanup(root);
        }

        Console.WriteLine(
            "PASS: the shutdown plan reads a running, skipped, unmanaged and unreadable task, refuses a foreign task, "
                + "rolls a failed save back to a disabled plan and backs the original task up before deleting it. No "
                + "system task was read, created or run."
        );
    }

    private static void Paths(string root)
    {
        string folder = Folder(root, "paths");
        var service = new ScheduledShutdownService(folder, new Fake(), () => Now);
        Check(service.DirectoryPath == folder, "The plan folder is reported");
        Check(service.LogPath == Path.Combine(folder, "shutdown.log"), "The plan log sits in that folder");
    }

    // ---------------------------------------------------------------- reading the task

    private static async Task RunningTaskAsync(string root)
    {
        string folder = Folder(root, "running");
        var service = new ScheduledShutdownService(folder, new Fake(), () => Now);
        var tasks = (Fake)Store(service);
        tasks.Record = new(TaskXml(folder, Now), true, true);
        await File.WriteAllTextAsync(
            Path.Combine(folder, "plan.json"),
            "{\"Version\":1,\"Enabled\":true,\"Time\":\"23:00\",\"CountdownSeconds\":30}"
        );
        await File.WriteAllTextAsync(
            Path.Combine(folder, "status.json"),
            "{\"UpdatedAt\":\""
                + DateTimeOffset.UtcNow.ToString("O")
                + "\",\"Message\":\"正在检查对局\",\"Occurrence\":\"2026-09-18T23:00:00\"}"
        );

        var view = await service.ReadAsync();
        Check(view is { Exists: true, Managed: true, Running: true }, "A running managed task is reported as ours");
        Check(view.Plan is { Enabled: true, Time: "23:00", CountdownSeconds: 30 }, "The plan comes from the task");
        Check(view.Message.Contains("正在执行", StringComparison.Ordinal), "A running task says so");
        Check(view.Message.Contains("正在检查对局", StringComparison.Ordinal), "A fresh status is shown");
        Check(view.Occurrence == "2026-09-18T23:00:00", "The occurrence the worker reported is passed on");
        Check(view.NextRun == new DateTime(2026, 9, 18, 23, 0, 0), "The next run is the trigger's time");

        await File.WriteAllTextAsync(Path.Combine(folder, "cancel.json"), "{\"Occurrence\":\"2026-09-18T23:00:00\"}");
        var skipped = await service.ReadAsync();
        Check(
            skipped.Message.Contains("下一次计划已跳过", StringComparison.Ordinal)
                && skipped.NextRun == new DateTime(2026, 9, 19, 23, 0, 0),
            "A skipped occurrence is announced and the next run moves a day"
        );
    }

    private static async Task SkippedAndUnmanagedAsync(string root)
    {
        // A trigger further away than the next daily occurrence wins.
        string far = Folder(root, "far");
        var service = new ScheduledShutdownService(far, new Fake(), () => Now);
        ((Fake)Store(service)).Record = new(TaskXml(far, new DateTime(2026, 9, 20, 22, 0, 0)), true, false);
        var view = await service.ReadAsync();
        Check(view.NextRun == new DateTime(2026, 9, 20, 23, 0, 0), "A trigger days away is not moved back to tomorrow");

        // Someone else's task with that name: adopted only by name, never enabled by us.
        string foreign = Folder(root, "foreign");
        var other = new ScheduledShutdownService(foreign, new Fake(), () => Now);
        ((Fake)Store(other)).Record = new(ForeignXml(foreign), true, true);
        var unmanaged = await other.ReadAsync();
        Check(!unmanaged.Managed, "A task without our marker is not ours");
        Check(
            unmanaged.Plan.CountdownSeconds == ScheduledShutdownService.DefaultCountdownSeconds,
            "A foreign task keeps the default countdown"
        );
        Check(
            unmanaged.Message.Contains("已发现原有脚本", StringComparison.Ordinal)
                && unmanaged.Message.Contains("正在执行", StringComparison.Ordinal),
            "A foreign task is announced and its running state is kept"
        );
        Check(unmanaged.Occurrence == null && unmanaged.NextRun != null, "A foreign task has no worker status");
    }

    private static async Task LatestStatusAsync(string root)
    {
        string folder = Folder(root, "status");
        var service = new ScheduledShutdownService(folder, new Fake(), () => Now);
        ((Fake)Store(service)).Record = new(TaskXml(folder, Now), true, false);
        await File.WriteAllTextAsync(
            Path.Combine(folder, "status.json"),
            "{\"UpdatedAt\":\""
                + DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O")
                + "\",\"Message\":\"上次尝试被跳过\"}"
        );

        var view = await service.ReadAsync();
        Check(
            view.Message.Contains("最近状态：上次尝试被跳过", StringComparison.Ordinal),
            "A status older than a minute is reported as the latest state, not as live"
        );
        Check(view.Occurrence == null, "An old status does not claim a current occurrence");
    }

    private static async Task UnreadableFilesAsync(string root)
    {
        string folder = Folder(root, "broken");
        var service = new ScheduledShutdownService(folder, new Fake(), () => Now);
        ((Fake)Store(service)).Record = new(TaskXml(folder, Now), true, false);
        await File.WriteAllTextAsync(Path.Combine(folder, "plan.json"), "not json at all");
        await File.WriteAllTextAsync(Path.Combine(folder, "status.json"), "not json either");

        var view = await service.ReadAsync();
        Check(
            view.Plan.CountdownSeconds == ScheduledShutdownService.DefaultCountdownSeconds,
            "A configuration that cannot be read falls back to the default countdown"
        );
        Check(
            view.Plan.Time == "23:00" && !view.Message.Contains("最近状态", StringComparison.Ordinal),
            "A status that cannot be read is left out"
        );
    }

    // ---------------------------------------------------------------- creating the task

    private static async Task CreateGuardsAsync(string root)
    {
        string folder = Folder(root, "refuse");
        var service = new ScheduledShutdownService(folder, new Fake(), () => Now);
        var tasks = (Fake)Store(service);
        tasks.Record = new("<Task xmlns=\"" + Ns + "\" />", false, false);
        Check(
            await Refuses(() => service.CreateAsync("23:00", 30)),
            "A same-named task the application does not recognize is never overwritten"
        );
        Check(tasks.Registrations == 0, "Nothing is registered for a task that cannot be adopted");

        Check(
            await Refuses(() => service.CreateAsync("25:00", 30)),
            "A time the client cannot express is refused before the task is touched"
        );
        Check(
            await Refuses(() => service.CreateAsync("23:00", 301)),
            "A countdown outside the allowed range is refused"
        );
    }

    private static async Task FailedSaveAsync(string root)
    {
        string folder = Folder(root, "rollback");
        var service = new ScheduledShutdownService(folder, new Fake(), () => Now);
        var tasks = (Fake)Store(service);
        Directory.CreateDirectory(Path.Combine(folder, "cancel.json")); // The atomic write cannot replace a folder.

        Check(await Refuses(() => service.CreateAsync("23:00", 30)), "A save that cannot finish reports a retry");
        Check(tasks.LastEnabled == false, "A half-saved plan is disarmed instead of left enabled");
        Check(
            (bool?)JObject.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "plan.json")))["Enabled"] == false,
            "The configuration on disk is disarmed as well"
        );
        Check(
            Directory.EnumerateFiles(folder, "*.tmp").Any() == false,
            "A failed atomic write leaves no temporary file behind"
        );
    }

    // ---------------------------------------------------------------- deleting the task

    private static async Task DeletionAsync(string root)
    {
        string folder = Folder(root, "delete");
        var service = new ScheduledShutdownService(folder, new Fake(), () => Now);
        var tasks = (Fake)Store(service);
        var nothing = await service.DeleteAsync();
        Check(!nothing.Exists && tasks.Deletions == 0, "Deleting without a task removes nothing");

        tasks.Record = new(TaskXml(folder, Now), false, false);
        var deleted = await service.DeleteAsync();
        Check(!deleted.Exists, "Deleting removes our task");
        Check(tasks.Deletions == 1 && tasks.Stops == 0, "A stopped task is not stopped again");
        Check(File.Exists(Path.Combine(folder, "original-task.xml")), "The task is backed up before it is removed");
        Check(
            (bool?)JObject.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "plan.json")))["Enabled"] == false,
            "The worker is disarmed before its trigger is removed"
        );
    }

    private static void Ownership()
    {
        Check(
            !ScheduledShutdownService.CanAdopt("<Task xmlns=\"" + Ns + "\" />"),
            "A task without an action is foreign"
        );
        Check(
            !ScheduledShutdownService.CanAdopt(
                "<Task xmlns=\""
                    + Ns
                    + "\"><Actions><Exec><Command>powershell.exe</Command><Arguments>-File a.ps1</Arguments></Exec>"
                    + "<Exec><Command>powershell.exe</Command><Arguments>-File b.ps1</Arguments></Exec></Actions></Task>"
            ),
            "A task with two actions is not recognized"
        );
        Check(
            !ScheduledShutdownService.CanAdopt(
                "<Task xmlns=\""
                    + Ns
                    + "\"><Actions><Exec><Command>cmd.exe</Command><Arguments>-File ShutdownAfterLoL.ps1</Arguments>"
                    + "</Exec></Actions></Task>"
            ),
            "A task that does not run PowerShell is not recognized"
        );
        Check(
            ScheduledShutdownService.CanAdopt(
                "<Task xmlns=\""
                    + Ns
                    + "\"><Actions><Exec><Command>PowerShell.exe</Command>"
                    + "<Arguments>-NoProfile -File \"C:\\a\\ShutdownAfterLoL.ps1\" -Run</Arguments>"
                    + "</Exec></Actions></Task>"
            ),
            "The original script is recognized whoever installed it"
        );
    }

    // ---------------------------------------------------------------- fixtures

    private static string TaskXml(string folder, DateTime clock) =>
        ScheduledShutdownService.BuildTaskXml(new(true, "23:00", 30), folder, Sid(), clock);

    private static string ForeignXml(string folder)
    {
        var xml = XDocument.Parse(TaskXml(folder, Now));
        xml.Descendants(Ns + "Description").Single().Value = "someone else's task";
        return xml.ToString();
    }

    private static string Sid() => WindowsIdentity.GetCurrent().User!.Value;

    private static object Store(ScheduledShutdownService service) =>
        typeof(ScheduledShutdownService)
            .GetField("_tasks", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service)!;

    private static async Task<bool> Refuses(Func<Task> action)
    {
        try
        {
            await action();
            return false;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return true;
        }
    }

    private static string Folder(string root, string name)
    {
        string folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string Temporary()
    {
        string path = Path.Combine(Path.GetTempPath(), "league-shutdown-plan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Cleanup(string path)
    {
        try
        {
            Directory.Delete(path, true);
        }
        catch (IOException e)
        {
            Console.WriteLine("Test folder kept: " + path + " (" + e.GetType().Name + ")");
        }
    }

    /// <summary>A scheduler that only remembers what it was asked to do.</summary>
    private sealed class Fake : IShutdownTaskStore
    {
        public ShutdownTaskRecord? Record;
        public bool? LastEnabled;
        public int Registrations,
            Stops,
            Deletions;

        public ShutdownTaskRecord? Read() => Record;

        public void Register(string xml)
        {
            Record = new(xml, (bool)XDocument.Parse(xml).Descendants(Ns + "Enabled").First(), false);
            Registrations++;
        }

        public void SetEnabled(bool enabled)
        {
            LastEnabled = enabled;
            if (Record != null)
            {
                Record = Record with { Enabled = enabled };
            }
        }

        public void Stop()
        {
            Stops++;
            if (Record != null)
            {
                Record = Record with { Running = false };
            }
        }

        public void Delete()
        {
            Deletions++;
            Record = null;
        }
    }
}
