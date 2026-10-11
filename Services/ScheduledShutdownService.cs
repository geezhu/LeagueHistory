using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json;

namespace League.Services;

public sealed record ShutdownPlan(bool Enabled, string Time, int CountdownSeconds)
{
    public void Validate()
    {
        if (
            !TimeOnly.TryParseExact(Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            || CountdownSeconds is < 0 or > 300
        )
        {
            throw new ArgumentException("请选择有效的每日时间，关机倒计时范围为 0～300 秒。");
        }
    }

    public DateTime NextOccurrence(DateTime now)
    {
        Validate();
        var today = now.Date.Add(TimeOnly.ParseExact(Time, "HH:mm", CultureInfo.InvariantCulture).ToTimeSpan());
        return today > now ? today : today.AddDays(1);
    }
}

public sealed record ShutdownTaskRecord(string Xml, bool Enabled, bool Running);

public sealed record ShutdownTaskView(
    ShutdownPlan Plan,
    bool Exists,
    bool Managed,
    bool Running,
    string Message,
    string? Occurrence
)
{
    public DateTime? NextRun { get; init; }
}

public interface IShutdownTaskStore
{
    ShutdownTaskRecord? Read();
    void Register(string xml);
    void Stop();
    void Delete();
    void SetEnabled(bool enabled);
}

public sealed class ScheduledShutdownService
{
    public const string TaskName = "ShutdownAfterLoL_2300"; // Adopt the existing task instead of creating a second schedule.
    public const string Marker = "LeagueHistory.ScheduledShutdown.v1";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private readonly IShutdownTaskStore _tasks;
    private readonly string _directory;
    private readonly Func<DateTime> _now;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string DirectoryPath => _directory;
    public string LogPath => Path.Combine(_directory, "shutdown.log");

    public ScheduledShutdownService(
        string? directory = null,
        IShutdownTaskStore? tasks = null,
        Func<DateTime>? now = null
    )
    {
        _directory =
            directory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LeagueHistory",
                "ScheduledShutdown"
            );
        _tasks = tasks ?? new WindowsShutdownTaskStore();
        _now = now ?? (() => DateTime.Now);
    }

    public Task<ShutdownTaskView> ReadAsync() => Exclusive(Read);

    private ShutdownTaskView Read()
    {
        var task = _tasks.Read();
        if (task == null)
        {
            return new(new(false, "23:00", 30), false, false, false, "设置时间后点击“创建计划”即可生效。", null);
        }

        var xml = XDocument.Parse(task.Xml);
        bool managed = (string?)xml.Root?.Element(Ns + "RegistrationInfo")?.Element(Ns + "Description") == Marker;
        string time = DateTime.TryParse((string?)xml.Descendants(Ns + "StartBoundary").FirstOrDefault(), out var start)
            ? start.ToString("HH:mm")
            : "23:00";
        var config = ReadJson(Path.Combine(_directory, "plan.json"));
        int delay = managed ? Math.Clamp((int?)config?["CountdownSeconds"] ?? 30, 0, 300) : 30;
        string message = managed ? "到点后自动检查对局状态。" : "已发现原有脚本，点击“创建计划”可替换并启用。";
        string? occurrence = null;
        var status = managed ? ReadJson(Path.Combine(_directory, "status.json")) : null;
        if (task.Running)
        {
            message += " · 正在执行";
            if (
                DateTimeOffset.TryParse((string?)status?["UpdatedAt"], out var updated)
                && DateTimeOffset.UtcNow - updated < TimeSpan.FromSeconds(60)
            )
            {
                message += "\n" + (string?)status?["Message"];
                occurrence = (string?)status?["Occurrence"];
            }
        }
        else if (managed && status != null)
        {
            message += "\n最近状态：" + (string?)status["Message"];
        }

        var cancel = managed ? ReadJson(Path.Combine(_directory, "cancel.json")) : null;
        var nextRun = new ShutdownPlan(task.Enabled, time, delay).NextOccurrence(_now());
        if (start > nextRun)
        {
            nextRun = start;
        }

        string next = nextRun.ToString("yyyy-MM-ddTHH:mm:ss");
        if ((string?)cancel?["Occurrence"] == next)
        {
            message += "\n下一次计划已跳过（" + next.Replace('T', ' ') + "）；重新创建可恢复。";
        }

        if ((string?)cancel?["Occurrence"] == next)
        {
            nextRun = nextRun.AddDays(1);
        }

        return new(new(task.Enabled, time, delay), true, managed, task.Running, message, occurrence)
        {
            NextRun = task.Enabled ? nextRun : null,
        };
    }

    public Task<ShutdownTaskView> CreateAsync(string time, int countdownSeconds) =>
        Exclusive(() => Save(new(true, time, countdownSeconds)));

    private ShutdownTaskView Save(ShutdownPlan plan)
    {
        plan.Validate();
        var existing = _tasks.Read();
        if (existing != null && !CanAdopt(existing.Xml))
        {
            throw new InvalidOperationException("同名计划任务的内容无法识别，未覆盖；请先在任务计划程序中核对。");
        }

        Directory.CreateDirectory(_directory);
        if (existing != null && !File.Exists(Path.Combine(_directory, "original-task.xml")))
        {
            AtomicWrite(Path.Combine(_directory, "original-task.xml"), existing.Xml);
        }

        string configPath = Path.Combine(_directory, "plan.json");
        string? previous = File.Exists(configPath) ? File.ReadAllText(configPath) : null;
        var config = new
        {
            Version = 1,
            Enabled = false,
            plan.Time,
            plan.CountdownSeconds,
            Generation = Guid.NewGuid().ToString("N"),
        };
        AtomicWrite(configPath, JsonConvert.SerializeObject(config)); // Invalidate any worker before replacing its files or trigger.
        bool registered = false;
        try
        {
            if (existing?.Running == true)
            {
                _tasks.Stop();
            }

            using var resource =
                typeof(ScheduledShutdownService).Assembly.GetManifestResourceStream("League.ScheduledShutdown.ps1")
                ?? throw new InvalidOperationException("关机脚本资源缺失。");
            using var reader = new StreamReader(resource, Encoding.UTF8);
            AtomicWrite(Path.Combine(_directory, "ShutdownAfterLoL.ps1"), reader.ReadToEnd(), bom: true);
            var sid =
                WindowsIdentity.GetCurrent().User?.Value
                ?? throw new InvalidOperationException("无法读取当前 Windows 用户。");
            _tasks.Register(BuildTaskXml(plan, _directory, sid, _now()));
            registered = true;
            AtomicWrite(Path.Combine(_directory, "cancel.json"), "{\"Occurrence\":\"\"}");
            AtomicWrite(
                configPath,
                JsonConvert.SerializeObject(
                    new
                    {
                        config.Version,
                        plan.Enabled,
                        plan.Time,
                        plan.CountdownSeconds,
                        config.Generation,
                    }
                )
            );
        }
        catch (Exception ex)
        {
            if (registered)
            {
                // Never pair the new trigger with an old enabled configuration after a partial save.
                AtomicWrite(configPath, JsonConvert.SerializeObject(config));
                _tasks.SetEnabled(false);
                throw new InvalidOperationException("保存未完成，计划已停用；请重试保存。", ex);
            }
            if (previous != null)
            {
                AtomicWrite(configPath, previous);
            }

            throw;
        }
        return Read();
    }

    public Task<ShutdownTaskView> DeleteAsync() =>
        Exclusive(() =>
        {
            var existing = _tasks.Read();
            if (existing == null)
            {
                return Read();
            }

            if (!CanAdopt(existing.Xml))
            {
                throw new InvalidOperationException("同名计划任务的内容无法识别，未删除。");
            }

            Directory.CreateDirectory(_directory);
            string backup = Path.Combine(_directory, "original-task.xml");
            if (!File.Exists(backup))
            {
                AtomicWrite(backup, existing.Xml);
            }
            // Disarm before stopping/removing the trigger. Keep the script and logs for diagnosis.
            AtomicWrite(
                Path.Combine(_directory, "plan.json"),
                JsonConvert.SerializeObject(new { Version = 1, Enabled = false })
            );
            _tasks.SetEnabled(false);
            if (existing.Running)
            {
                _tasks.Stop();
            }

            _tasks.Delete();
            return Read();
        });

    public Task<string> ProbeAsync() =>
        Exclusive(() =>
        {
            // This command only reads state. It cannot arm a schedule or reach the shutdown entry point.
            string temporary = Path.Combine(
                Path.GetTempPath(),
                "League-shutdown-probe-" + Guid.NewGuid().ToString("N") + ".ps1"
            );
            try
            {
                using var resource = typeof(ScheduledShutdownService).Assembly.GetManifestResourceStream(
                    "League.ScheduledShutdown.ps1"
                )!;
                using (var reader = new StreamReader(resource))
                {
                    File.WriteAllText(temporary, reader.ReadToEnd(), new UTF8Encoding(true));
                }

                var start = new System.Diagnostics.ProcessStartInfo(PowerShellPath())
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                foreach (
                    var arg in new[]
                    {
                        "-NoProfile",
                        "-NonInteractive",
                        "-ExecutionPolicy",
                        "Bypass",
                        "-File",
                        temporary,
                        "-Probe",
                    }
                )
                {
                    start.ArgumentList.Add(arg);
                }

                using var process =
                    System.Diagnostics.Process.Start(start)
                    ?? throw new InvalidOperationException("无法启动状态检测。");
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(20000))
                {
                    process.Kill();
                    throw new TimeoutException("检测超时，请稍后重试。");
                }
                string text = output.GetAwaiter().GetResult();
                _ = error.GetAwaiter().GetResult();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("无法检测游戏状态，请查看权限或稍后重试。");
                }

                var state = Newtonsoft.Json.Linq.JObject.Parse(text);
                return "检测结果：" + (string?)state["Reason"] + "。此操作仅检测，不会关机。";
            }
            finally
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (IOException)
                {
                    // Optional temporary script cleanup must not replace the scheduler result.
                }
            }
        });

    private async Task<T> Exclusive<T>(Func<T> action)
    {
        await _gate.WaitAsync();
        try
        {
            return await Task.Run(action);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Newtonsoft.Json.Linq.JObject? ReadJson(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length >= 16384)
            {
                return null;
            }

            using var text = new StringReader(File.ReadAllText(path));
            using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None };
            return Newtonsoft.Json.Linq.JObject.Load(reader);
        }
        catch
        {
            return null;
        }
    }

    private static void AtomicWrite(string path, string text, bool bom = false)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(bom));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public static string PowerShellPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe"
        );

    public static bool CanAdopt(string text)
    {
        var xml = XDocument.Parse(text);
        if ((string?)xml.Root?.Element(Ns + "RegistrationInfo")?.Element(Ns + "Description") == Marker)
        {
            return true;
        }

        var actions = xml.Descendants(Ns + "Exec").ToArray();
        if (
            actions.Length != 1
            || !string.Equals(
                Path.GetFileName((string?)actions[0].Element(Ns + "Command")),
                "powershell.exe",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return false;
        }

        var file = System.Text.RegularExpressions.Regex.Match(
            (string?)actions[0].Element(Ns + "Arguments") ?? "",
            "(?:^|\\s)-File\\s+(?:\"(?<path>[^\"]+)\"|(?<path>\\S+))(?:\\s|$)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        return file.Success
            && string.Equals(
                Path.GetFileName(file.Groups["path"].Value),
                "ShutdownAfterLoL.ps1",
                StringComparison.OrdinalIgnoreCase
            );
    }

    public static string BuildTaskXml(ShutdownPlan plan, string directory, string sid, DateTime now)
    {
        plan.Validate();
        XElement E(string name, object? value) => new(Ns + name, value);
        return new XDocument(
            new XElement(
                Ns + "Task",
                new XAttribute("version", "1.2"),
                E("RegistrationInfo", E("Description", Marker)),
                E(
                    "Triggers",
                    new XElement(
                        Ns + "CalendarTrigger",
                        E("StartBoundary", plan.NextOccurrence(now).ToString("yyyy-MM-ddTHH:mm:ss")),
                        E("Enabled", true),
                        E("ScheduleByDay", E("DaysInterval", 1))
                    )
                ),
                E(
                    "Principals",
                    new XElement(
                        Ns + "Principal",
                        new XAttribute("id", "Author"),
                        E("UserId", sid),
                        E("LogonType", "InteractiveToken"),
                        E("RunLevel", "HighestAvailable")
                    )
                ),
                E(
                    "Settings",
                    new object[]
                    {
                        E("MultipleInstancesPolicy", "IgnoreNew"),
                        E("DisallowStartIfOnBatteries", false),
                        E("StopIfGoingOnBatteries", false),
                        E("StartWhenAvailable", false),
                        E("Enabled", plan.Enabled),
                        E("WakeToRun", false),
                        E("ExecutionTimeLimit", "PT7H"),
                    }
                ),
                new XElement(
                    Ns + "Actions",
                    new XAttribute("Context", "Author"),
                    E(
                        "Exec",
                        new object[]
                        {
                            E("Command", PowerShellPath()),
                            E(
                                "Arguments",
                                "-NoProfile -NonInteractive -Sta -WindowStyle Hidden -ExecutionPolicy Bypass -File \""
                                    + Path.Combine(directory, "ShutdownAfterLoL.ps1")
                                    + "\" -Run"
                            ),
                            E("WorkingDirectory", directory),
                        }
                    )
                )
            )
        ).ToString();
    }
}

internal sealed class WindowsShutdownTaskStore : IShutdownTaskStore
{
    private T Access<T>(Func<dynamic, T> action)
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        object? folder = null;
        try
        {
            service.Connect();
            folder = service.GetFolder("\\");
            return action(folder);
        }
        finally
        {
            if (folder != null)
            {
                Marshal.FinalReleaseComObject(folder);
            }

            Marshal.FinalReleaseComObject(service);
        }
    }

    public ShutdownTaskRecord? Read() =>
        Access<ShutdownTaskRecord?>(folder =>
        {
            dynamic? task = null;
            try
            {
                task = LookupTask(() => folder.GetTask(ScheduledShutdownService.TaskName));
                return task == null ? null : new((string)task.Xml, (bool)task.Enabled, (int)task.State == 4);
            }
            finally
            {
                if (task != null)
                {
                    Marshal.FinalReleaseComObject(task);
                }
            }
        });

    private static object? LookupTask(Func<object> lookup)
    {
        try
        {
            return lookup();
        }
        // COM interop maps known HRESULTs to FileNotFoundException / DirectoryNotFoundException too.
        // Only task lookup may mean "not created"; errors reading a found task must still propagate.
        catch (Exception e) when (e.HResult is unchecked((int)0x80070002) or unchecked((int)0x80070003))
        {
            return null;
        }
    }

    public void Register(string xml) =>
        Access(folder =>
        {
            dynamic registered = folder.RegisterTask(ScheduledShutdownService.TaskName, xml, 6, null, null, 3, null);
            Marshal.FinalReleaseComObject(registered);
            return true;
        });

    public void Stop() =>
        Access(folder =>
        {
            dynamic task = folder.GetTask(ScheduledShutdownService.TaskName);
            try
            {
                task.Stop(0);
                return true;
            }
            finally
            {
                Marshal.FinalReleaseComObject(task);
            }
        });

    public void Delete() =>
        Access(folder =>
        {
            folder.DeleteTask(ScheduledShutdownService.TaskName, 0);
            return true;
        });

    public void SetEnabled(bool enabled) =>
        Access(folder =>
        {
            dynamic task = folder.GetTask(ScheduledShutdownService.TaskName);
            try
            {
                task.Enabled = enabled;
                return true;
            }
            finally
            {
                Marshal.FinalReleaseComObject(task);
            }
        });
}
