using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using League.Services;
using Newtonsoft.Json.Linq;

internal static class ShutdownSettingsChecks
{
    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new Exception(message);
        }
    }

    public static void Run() => Task.Run(Core).GetAwaiter().GetResult();

    public static void ValidateWindows()
    {
        var service = new ScheduledShutdownService();
        var view = service.ReadAsync().GetAwaiter().GetResult();
        Console.WriteLine(
            $"Existing task: exists={view.Exists}, enabled={view.Plan.Enabled}, time={view.Plan.Time}, managed={view.Managed}"
        );
        string xml = ScheduledShutdownService.BuildTaskXml(
            view.Plan with
            {
                Enabled = false,
            },
            service.DirectoryPath,
            System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value,
            DateTime.Now
        );
        dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        object? folder = null,
            validated = null;
        try
        {
            scheduler.Connect();
            folder = scheduler.GetFolder("\\");
            // TASK_VALIDATE_ONLY = 1: validate XML with Windows, never register or start it.
            validated = ((dynamic)folder).RegisterTask(ScheduledShutdownService.TaskName, xml, 1, null, null, 3, null);
            Console.WriteLine("PASS: Windows validated the disabled task XML; existing task was not modified or run.");
            Console.WriteLine(service.ProbeAsync().GetAwaiter().GetResult());
        }
        finally
        {
            if (validated != null && Marshal.IsComObject(validated))
            {
                Marshal.FinalReleaseComObject(validated);
            }

            if (folder != null)
            {
                Marshal.FinalReleaseComObject(folder);
            }

            Marshal.FinalReleaseComObject(scheduler);
        }
    }

    private static async Task Core()
    {
        LookupChecks();
        var now = new DateTime(2026, 9, 17, 23, 20, 0);
        var plan = new ShutdownPlan(false, "23:00", 30);
        PlanValidation(now, plan);
        string root = Path.Combine(Path.GetTempPath(), "League-shutdown-check-" + Guid.NewGuid().ToString("N"));
        var tasks = new FakeTasks();
        var service = new ScheduledShutdownService(root, tasks, () => now);
        try
        {
            var before = await service.ReadAsync();
            Check(
                !before.Exists && !before.Plan.Enabled && tasks.Writes == 0,
                "Reading settings never creates or enables a task"
            );
            var (legacy, unrelated) = TaskOwnership(now, plan, root);
            await CreationAsync(service, tasks, root, legacy);
            await RegistrationFailureAndReplacementAsync(service, tasks, root);
            await DeletionAndRecreationAsync(service, tasks, root, unrelated);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
        Console.WriteLine(
            "PASS: daily shutdown create/replace/delete uses a fake scheduler; migration, timing, worker cancellation, retained logs and failure handling checked. No system task changed."
        );
    }

    private static void PlanValidation(DateTime now, ShutdownPlan plan)
    {
        Check(
            plan.NextOccurrence(now) == new DateTime(2026, 9, 18, 23, 0, 0),
            "Saving after today's deadline starts tomorrow"
        );
        Check(
            (plan with { Time = "00:05" }).NextOccurrence(now) == new DateTime(2026, 9, 18, 0, 5, 0),
            "Midnight schedule is explicit"
        );
        foreach (
            var invalid in new[]
            {
                plan with
                {
                    Time = "24:00",
                },
                plan with
                {
                    Time = "9:00",
                },
                plan with
                {
                    CountdownSeconds = -1,
                },
                plan with
                {
                    CountdownSeconds = 301,
                },
            }
        )
        {
            bool rejected = false;
            try
            {
                invalid.Validate();
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            Check(rejected, "Invalid plan rejected");
        }
    }

    private static (XDocument Legacy, XDocument Unrelated) TaskOwnership(DateTime now, ShutdownPlan plan, string root)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        string xml = ScheduledShutdownService.BuildTaskXml(plan, root + " & 中文", "S-1-5-21-1", now);
        var parsed = XDocument.Parse(xml);
        Check(
            (string?)parsed.Descendants(ns + "StartWhenAvailable").Single() == "false",
            "Missed deadlines are not replayed"
        );
        Check(
            (string?)parsed.Descendants(ns + "MultipleInstancesPolicy").Single() == "IgnoreNew",
            "Only one worker can run"
        );
        Check(
            parsed.Descendants(ns + "Arguments").Single().Value.Contains("-WindowStyle Hidden")
                && parsed.Descendants(ns + "Arguments").Single().Value.Contains("-Run"),
            "Background worker is explicit and hidden"
        );
        Check(
            !xml.Contains("shutdown.exe"),
            "The task runs a guarded worker instead of issuing an unconditional OS shutdown"
        );
        Check(ScheduledShutdownService.CanAdopt(xml), "Owned tasks can be upgraded");
        var legacy = new XDocument(parsed);
        legacy.Descendants(ns + "Description").Single().Value = "old";
        Check(ScheduledShutdownService.CanAdopt(legacy.ToString()), "Existing ShutdownAfterLoL script is recognized");
        var unrelated = new XDocument(legacy);
        unrelated.Descendants(ns + "Arguments").Single().Value = "-Command Write-Output ShutdownAfterLoL.ps1";
        Check(
            !ScheduledShutdownService.CanAdopt(unrelated.ToString()),
            "An unrelated command mentioning the filename is not overwritten"
        );
        return (legacy, unrelated);
    }

    private static async Task CreationAsync(
        ScheduledShutdownService service,
        FakeTasks tasks,
        string root,
        XDocument legacy
    )
    {
        tasks.Record = new(legacy.ToString(), false, false);
        var saved = await service.CreateAsync("23:00", 30);
        Check(
            saved.Managed && saved.Plan.Enabled && tasks.Writes == 1,
            "Explicit create adopts and enables the existing daily task"
        );
        Check(
            (await File.ReadAllTextAsync(Path.Combine(root, "original-task.xml"))) == legacy.ToString(),
            "Original task is backed up"
        );
        var script = await File.ReadAllBytesAsync(Path.Combine(root, "ShutdownAfterLoL.ps1"));
        Check(script.Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "Windows PowerShell script uses UTF-8 BOM");
        Check(
            (bool?)JObject.Parse((await File.ReadAllTextAsync(Path.Combine(root, "plan.json"))))["Enabled"] == true,
            "Created schedule has an enabled worker configuration"
        );
        var enabled = await service.CreateAsync("22:30", 60);
        Check(
            enabled.Plan.Enabled && enabled.Plan.Time == "22:30" && enabled.Plan.CountdownSeconds == 60,
            "Recreate replaces the same task with the requested options"
        );
        Check(
            enabled.NextRun == new DateTime(2026, 9, 18, 22, 30, 0),
            "Enabled plan shows the next scheduled occurrence"
        );
    }

    private static async Task RegistrationFailureAndReplacementAsync(
        ScheduledShutdownService service,
        FakeTasks tasks,
        string root
    )
    {
        string previous = (await File.ReadAllTextAsync(Path.Combine(root, "plan.json")));
        tasks.Fail = true;
        bool failed = false;
        try
        {
            await service.CreateAsync("23:00", 30);
        }
        catch (InvalidOperationException)
        {
            failed = true;
        }
        Check(
            failed && (await File.ReadAllTextAsync(Path.Combine(root, "plan.json"))) == previous,
            "Failed registration restores the existing plan"
        );
        tasks.Fail = false;
        tasks.Record = tasks.Record! with { Running = true };
        var replacement = await service.CreateAsync("01:15", 45);
        Check(
            replacement.Plan.Enabled && !replacement.Running && tasks.Stops == 1,
            "Recreate stops the old worker before installing its replacement"
        );
        Check(
            replacement.Plan.Time == "01:15"
                && replacement.Plan.CountdownSeconds == 45
                && replacement.NextRun == new DateTime(2026, 9, 18, 1, 15, 0),
            "Replacement starts at the next future occurrence"
        );
    }

    private static async Task DeletionAndRecreationAsync(
        ScheduledShutdownService service,
        FakeTasks tasks,
        string root,
        XDocument unrelated
    )
    {
        await File.WriteAllTextAsync(Path.Combine(root, "shutdown.log"), "test log");
        tasks.Record = tasks.Record! with { Running = true };
        tasks.FailDelete = true;
        bool failed = false;
        try
        {
            await service.DeleteAsync();
        }
        catch (InvalidOperationException)
        {
            failed = true;
        }
        Check(
            failed && tasks.Record is { Enabled: false, Running: false },
            "Failed deletion leaves the task disabled and stopped"
        );
        Check(
            (bool?)JObject.Parse((await File.ReadAllTextAsync(Path.Combine(root, "plan.json"))))["Enabled"] == false,
            "Delete disarms the script before removing its task"
        );
        tasks.FailDelete = false;
        var deleted = await service.DeleteAsync();
        Check(!deleted.Exists && !deleted.Plan.Enabled && deleted.NextRun == null, "Delete removes the one task");
        Check(
            File.Exists(Path.Combine(root, "shutdown.log")) && File.Exists(Path.Combine(root, "original-task.xml")),
            "Delete retains logs and original task backup"
        );
        var recreated = await service.CreateAsync("23:30", 30);
        Check(recreated.Exists && recreated.Plan.Enabled, "Explicit creation after deletion enables the new plan");
        tasks.Record = new(unrelated.ToString(), false, false);
        int deletes = tasks.Deletes;
        failed = false;
        try
        {
            await service.DeleteAsync();
        }
        catch (InvalidOperationException)
        {
            failed = true;
        }
        Check(failed && tasks.Deletes == deletes, "Deletion does not touch an unrelated same-name task");
    }

    private static void LookupChecks()
    {
        var type = typeof(ScheduledShutdownService).Assembly.GetType("League.Services.WindowsShutdownTaskStore")!;
        var lookup = type.GetMethod("LookupTask", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (
            var error in new Exception[]
            {
                new FileNotFoundException(),
                new DirectoryNotFoundException(),
                new COMException("missing task", unchecked((int)0x80070002)),
                new COMException("missing task path", unchecked((int)0x80070003)),
            }
        )
        {
            Check(
                lookup.Invoke(null, new object[] { (Func<object>)(() => throw error) }) == null,
                "Absent task is recognized for mapped .NET exceptions as well as COM exceptions"
            );
        }
        var existing = new object();
        Check(
            ReferenceEquals(existing, lookup.Invoke(null, new object[] { (Func<object>)(() => existing) })),
            "A found task is preserved"
        );
        foreach (
            var error in new Exception[]
            {
                new UnauthorizedAccessException(),
                new COMException("scheduler unavailable", unchecked((int)0x800706BA)),
                new IOException("unrelated read failure"),
            }
        )
        {
            bool failed = false;
            try
            {
                lookup.Invoke(null, new object[] { (Func<object>)(() => throw error) });
            }
            catch (TargetInvocationException e)
            {
                failed = ReferenceEquals(e.InnerException, error);
            }
            Check(failed, "Permission and scheduler failures cannot be mistaken for a missing task");
        }
    }

    private sealed class FakeTasks : IShutdownTaskStore
    {
        public ShutdownTaskRecord? Record;
        public int Writes,
            Stops,
            Deletes;
        public bool Fail,
            FailDelete;

        public ShutdownTaskRecord? Read() => Record;

        public void Register(string xml)
        {
            if (Fail)
            {
                throw new InvalidOperationException("Simulated registration failure");
            }

            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            Record = new(
                xml,
                (bool)XDocument.Parse(xml).Root!.Element(ns + "Settings")!.Element(ns + "Enabled")!,
                false
            );
            Writes++;
        }

        public void Stop()
        {
            if (Record != null)
            {
                Record = Record with { Running = false };
            }

            Stops++;
        }

        public void SetEnabled(bool enabled)
        {
            if (Record != null)
            {
                Record = Record with { Enabled = enabled };
            }
        }

        public void Delete()
        {
            if (FailDelete)
            {
                throw new InvalidOperationException("Simulated delete failure");
            }

            Record = null;
            Deletes++;
        }
    }
}
