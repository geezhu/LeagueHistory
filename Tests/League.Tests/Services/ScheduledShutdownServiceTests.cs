using System.Security.Principal;
using System.Xml.Linq;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The daily shutdown plan: the task XML that is written, which existing tasks may be adopted, how the plan and the
/// worker status are read back, and how the store is driven when a plan is created, replaced or deleted. Every
/// scheduler call goes to a stub store and every file into a temporary folder, so no system task is read, created or
/// run and no shutdown is armed. This class replaces Tests/StatisticsChecks/ShutdownPlanChecks.cs and the non-UI half
/// of ShutdownSettingsChecks.cs.
/// </summary>
public class ScheduledShutdownServiceTests : IDisposable
{
    private const string NsText = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private const string EmptyTask = "<Task xmlns=\"" + NsText + "\" />";
    private const string ActionsHead = "<Task xmlns=\"" + NsText + "\"><Actions>";
    private const string ExecHead = ActionsHead + "<Exec>";
    private static readonly XNamespace Ns = NsText;
    private static readonly DateTime Now = new(2026, 9, 17, 23, 20, 0);
    private readonly TempFolder _folder = new("league-shutdown");
    private readonly StubShutdownTaskStore _tasks = new();

    public void Dispose() => _folder.Dispose();

    // ---------------------------------------------------------------- the plan the user can change

    [Fact]
    public void DirectoryPath_ANewService_ReportsThePlanFolder()
    {
        var service = Service();

        Assert.Equal(_folder.Root, service.DirectoryPath);
    }

    [Fact]
    public void LogPath_ANewService_SitsInThePlanFolder()
    {
        var service = Service();

        Assert.Equal(Path.Combine(_folder.Root, "shutdown.log"), service.LogPath);
    }

    [Theory]
    [InlineData("24:00", 30)]
    [InlineData("9:00", 30)]
    [InlineData("23:00", -1)]
    [InlineData("23:00", 301)]
    public void Validate_InvalidPlan_IsRejected(string time, int countdownSeconds)
    {
        var plan = new ShutdownPlan(true, time, countdownSeconds);

        Assert.Throws<ArgumentException>(plan.Validate);
    }

    [Theory]
    [InlineData("00:00", 0)]
    [InlineData("23:59", 300)]
    public void Validate_AllowedBoundaries_AreAccepted(string time, int countdownSeconds)
    {
        var plan = new ShutdownPlan(true, time, countdownSeconds);

        Assert.Null(Record.Exception(plan.Validate));
    }

    [Fact]
    public void NextOccurrence_TimeAlreadyPassedToday_MovesToTomorrow()
    {
        var plan = new ShutdownPlan(true, "23:00", 30);

        Assert.Equal(new DateTime(2026, 9, 18, 23, 0, 0), plan.NextOccurrence(Now));
    }

    [Fact]
    public void NextOccurrence_TimeStillAheadToday_StaysToday()
    {
        var plan = new ShutdownPlan(true, "23:40", 30);

        Assert.Equal(new DateTime(2026, 9, 17, 23, 40, 0), plan.NextOccurrence(Now));
    }

    [Fact]
    public void NextOccurrence_ExactlyTheScheduledMinute_MovesToTomorrow()
    {
        var plan = new ShutdownPlan(true, "23:20", 30);

        Assert.Equal(new DateTime(2026, 9, 18, 23, 20, 0), plan.NextOccurrence(Now));
    }

    [Fact]
    public void NextOccurrence_MidnightSchedule_IsTomorrow()
    {
        var plan = new ShutdownPlan(true, "00:05", 30);

        Assert.Equal(new DateTime(2026, 9, 18, 0, 5, 0), plan.NextOccurrence(Now));
    }

    // ---------------------------------------------------------------- the task that is registered

    [Theory]
    [InlineData("StartWhenAvailable", "false")]
    [InlineData("MultipleInstancesPolicy", "IgnoreNew")]
    [InlineData("DisallowStartIfOnBatteries", "false")]
    [InlineData("StopIfGoingOnBatteries", "false")]
    [InlineData("WakeToRun", "false")]
    [InlineData("ExecutionTimeLimit", "PT7H")]
    public void BuildTaskXml_Settings_AreWrittenAsRequired(string name, string expected)
    {
        Assert.Equal(expected, Setting(TaskXml(new(true, "23:00", 30), Now), name));
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void BuildTaskXml_PlanEnabled_ControlsTheTrigger(bool enabled, string expected)
    {
        Assert.Equal(expected, Setting(TaskXml(new(enabled, "23:00", 30), Now), "Enabled"));
    }

    [Fact]
    public void BuildTaskXml_StartBoundary_IsTheNextOccurrence()
    {
        Assert.Equal("2026-09-18T23:00:00", Value(TaskXml(new(true, "23:00", 30), Now), "StartBoundary"));
    }

    [Fact]
    public void BuildTaskXml_CalendarTrigger_RepeatsEveryDay()
    {
        Assert.Equal("1", Value(TaskXml(new(true, "23:00", 30), Now), "DaysInterval"));
    }

    [Fact]
    public void BuildTaskXml_Description_CarriesOurMarker()
    {
        Assert.Equal(ScheduledShutdownService.Marker, Value(TaskXml(new(true, "23:00", 30), Now), "Description"));
    }

    [Fact]
    public void BuildTaskXml_Arguments_RunTheWorkerWithoutAWindow()
    {
        Assert.Contains("-WindowStyle Hidden", Value(TaskXml(new(true, "23:00", 30), Now), "Arguments"));
    }

    [Fact]
    public void BuildTaskXml_Arguments_PointAtTheScriptInThePlanFolder()
    {
        string arguments = Value(TaskXml(new(true, "23:00", 30), Now), "Arguments");

        Assert.Contains(Path.Combine(_folder.Root, "ShutdownAfterLoL.ps1"), arguments);
    }

    [Fact]
    public void BuildTaskXml_Command_IsWindowsPowerShell()
    {
        Assert.Equal(ScheduledShutdownService.PowerShellPath(), Value(TaskXml(new(true, "23:00", 30), Now), "Command"));
    }

    [Fact]
    public void BuildTaskXml_WorkingDirectory_IsThePlanFolder()
    {
        Assert.Equal(_folder.Root, Value(TaskXml(new(true, "23:00", 30), Now), "WorkingDirectory"));
    }

    [Fact]
    public void BuildTaskXml_Principal_IsTheCurrentUser()
    {
        Assert.Equal(Sid(), Value(TaskXml(new(true, "23:00", 30), Now), "UserId"));
    }

    [Theory]
    [InlineData("LogonType", "InteractiveToken")]
    [InlineData("RunLevel", "HighestAvailable")]
    public void BuildTaskXml_Principal_StaysInteractive(string name, string expected)
    {
        Assert.Equal(expected, Value(TaskXml(new(true, "23:00", 30), Now), name));
    }

    [Fact]
    public void BuildTaskXml_Plan_IsNeverAnUnconditionalShutdown()
    {
        string xml = ScheduledShutdownService.BuildTaskXml(new(true, "23:00", 30), _folder.Root, Sid(), Now);

        Assert.DoesNotContain("shutdown.exe", xml);
    }

    // ---------------------------------------------------------------- adopting an existing task

    [Theory]
    [InlineData(EmptyTask)]
    [InlineData(
        ExecHead
            + "<Command>powershell.exe</Command><Arguments>-File a.ps1</Arguments></Exec>"
            + "<Exec><Command>powershell.exe</Command><Arguments>-File b.ps1</Arguments></Exec></Actions></Task>"
    )]
    [InlineData(
        ExecHead + "<Command>cmd.exe</Command><Arguments>-File ShutdownAfterLoL.ps1</Arguments></Exec></Actions></Task>"
    )]
    [InlineData(
        ExecHead
            + "<Command>powershell.exe</Command><Arguments>-Command Write-Output ShutdownAfterLoL.ps1</Arguments>"
            + "</Exec></Actions></Task>"
    )]
    public void CanAdopt_TaskWeDoNotOwn_ReturnsFalse(string xml)
    {
        Assert.False(ScheduledShutdownService.CanAdopt(xml));
    }

    [Fact]
    public void CanAdopt_OurOwnTask_ReturnsTrue()
    {
        Assert.True(ScheduledShutdownService.CanAdopt(TaskXml(new(true, "23:00", 30), Now)));
    }

    [Fact]
    public void CanAdopt_TaskWithoutOurMarker_IsAdoptedByItsScript()
    {
        Assert.True(ScheduledShutdownService.CanAdopt(LegacyTaskXml("old")));
    }

    [Fact]
    public void CanAdopt_ScriptInstalledBySomeoneElse_ReturnsTrue()
    {
        const string xml =
            ExecHead
            + "<Command>PowerShell.exe</Command><Arguments>-NoProfile -File \"C:\\a\\ShutdownAfterLoL.ps1\" -Run"
            + "</Arguments></Exec></Actions></Task>";

        Assert.True(ScheduledShutdownService.CanAdopt(xml));
    }

    // ---------------------------------------------------------------- reading the current state

    [Fact]
    public async Task ReadAsync_NoTask_ReportsNothingScheduled()
    {
        var view = await Service().ReadAsync();

        Assert.False(view.Exists);
        Assert.False(view.Managed);
        Assert.False(view.Plan.Enabled);
        Assert.Null(view.NextRun);
    }

    [Fact]
    public async Task ReadAsync_NoTask_UsesTheDefaultTime()
    {
        var view = await Service().ReadAsync();

        Assert.Equal("23:00", view.Plan.Time);
        Assert.Equal(ScheduledShutdownService.DefaultCountdownSeconds, view.Plan.CountdownSeconds);
    }

    [Fact]
    public async Task ReadAsync_NoTask_DoesNotCreateOrEnableAnything()
    {
        var service = Service();

        await service.ReadAsync();
        await service.ReadAsync();

        Assert.Equal(0, _tasks.Registrations);
        Assert.Equal(0, _tasks.EnableChanges);
        Assert.Equal(0, _tasks.Stops);
        Assert.Equal(0, _tasks.Deletions);
    }

    // ---------------------------------------------------------------- creating the plan

    [Fact]
    public async Task CreateAsync_AdoptableTask_IsAdoptedAndEnabled()
    {
        var service = Service();
        _tasks.Record = new(LegacyTaskXml("old"), false, false);

        var saved = await service.CreateAsync("23:00", 30);

        Assert.True(saved.Managed);
        Assert.True(saved.Plan.Enabled);
        Assert.Equal(1, _tasks.Registrations);
    }

    [Fact]
    public async Task CreateAsync_AdoptableTask_BacksTheOriginalUp()
    {
        var service = Service();
        string legacy = LegacyTaskXml("old");
        _tasks.Record = new(legacy, false, false);

        await service.CreateAsync("23:00", 30);

        Assert.Equal(legacy, await File.ReadAllTextAsync(_folder.File("original-task.xml")));
    }

    [Fact]
    public async Task CreateAsync_AnyPlan_WritesAnEnabledConfiguration()
    {
        await Service().CreateAsync("23:00", 30);

        var config = JObject.Parse(await File.ReadAllTextAsync(_folder.File("plan.json")));

        Assert.True((bool?)config["Enabled"]);
    }

    [Fact]
    public async Task CreateAsync_AnyPlan_WritesTheWorkerScriptWithAUtf8Bom()
    {
        await Service().CreateAsync("23:00", 30);

        byte[] script = await File.ReadAllBytesAsync(_folder.File("ShutdownAfterLoL.ps1"));

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, script[..3]);
    }

    [Fact]
    public async Task CreateAsync_NewOptions_ReplaceTheSameTask()
    {
        var service = Service();
        _tasks.Record = new(LegacyTaskXml("old"), false, false);
        await service.CreateAsync("23:00", 30);

        var replaced = await service.CreateAsync("22:30", 60);

        Assert.Equal("22:30", replaced.Plan.Time);
        Assert.Equal(60, replaced.Plan.CountdownSeconds);
        Assert.True(replaced.Plan.Enabled);
    }

    [Fact]
    public async Task CreateAsync_EnabledPlan_ShowsTheNextOccurrence()
    {
        var saved = await Service().CreateAsync("22:30", 60);

        Assert.Equal<DateTime?>(new DateTime(2026, 9, 18, 22, 30, 0), saved.NextRun);
    }

    // ---------------------------------------------------------------- deleting the plan

    [Fact]
    public async Task DeleteAsync_NoTask_RemovesNothing()
    {
        var view = await Service().DeleteAsync();

        Assert.False(view.Exists);
        Assert.Equal(0, _tasks.Deletions);
    }

    [Fact]
    public async Task DeleteAsync_OurTask_RemovesIt()
    {
        var service = Service();
        _tasks.Record = new(TaskXml(new(true, "23:00", 30), Now), false, false);

        var view = await service.DeleteAsync();

        Assert.False(view.Exists);
        Assert.Equal(1, _tasks.Deletions);
    }

    [Fact]
    public async Task DeleteAsync_StoppedTask_IsNotStoppedAgain()
    {
        var service = Service();
        _tasks.Record = new(TaskXml(new(true, "23:00", 30), Now), false, false);

        await service.DeleteAsync();

        Assert.Equal(0, _tasks.Stops);
    }

    [Fact]
    public async Task DeleteAsync_OurTask_BacksItUpBeforeRemovingIt()
    {
        var service = Service();
        string task = TaskXml(new(true, "23:00", 30), Now);
        _tasks.Record = new(task, false, false);

        await service.DeleteAsync();

        Assert.Equal(task, await File.ReadAllTextAsync(_folder.File("original-task.xml")));
    }

    [Fact]
    public async Task DeleteAsync_OurTask_DisarmsTheConfigurationFirst()
    {
        var service = Service();
        _tasks.Record = new(TaskXml(new(true, "23:00", 30), Now), false, false);

        await service.DeleteAsync();

        var config = JObject.Parse(await File.ReadAllTextAsync(_folder.File("plan.json")));
        Assert.False((bool?)config["Enabled"]);
    }

    // ---------------------------------------------------------------- fixtures

    private ScheduledShutdownService Service() => new(_folder.Root, _tasks, () => Now);

    private string TaskXml(ShutdownPlan plan, DateTime now) =>
        ScheduledShutdownService.BuildTaskXml(plan, _folder.Root, Sid(), now);

    private string LegacyTaskXml(string description)
    {
        var xml = XDocument.Parse(TaskXml(new(true, "23:00", 30), Now));
        xml.Descendants(Ns + "Description").Single().Value = description;
        return xml.ToString();
    }

    private static string Sid() => WindowsIdentity.GetCurrent().User!.Value;

    private static string Value(string xml, string name) =>
        (string?)XDocument.Parse(xml).Descendants(Ns + name).Single() ?? "";

    private static string Setting(string xml, string name) =>
        (string?)XDocument.Parse(xml).Descendants(Ns + "Settings").Single().Element(Ns + name) ?? "";

    /// <summary>A scheduler that only remembers what it was asked to do. No system task is read or changed.</summary>
    private sealed class StubShutdownTaskStore : IShutdownTaskStore
    {
        public ShutdownTaskRecord? Record { get; set; }
        public bool FailRegistration { get; set; }
        public bool FailDeletion { get; set; }
        public int Registrations { get; private set; }
        public int EnableChanges { get; private set; }
        public int Stops { get; private set; }
        public int Deletions { get; private set; }
        public bool? LastEnabled { get; private set; }

        public ShutdownTaskRecord? Read() => Record;

        public void Register(string xml)
        {
            if (FailRegistration)
            {
                throw new InvalidOperationException("Simulated registration failure");
            }

            Registrations++;
            Record = new(
                xml,
                (bool)XDocument.Parse(xml).Descendants(Ns + "Settings").Single().Element(Ns + "Enabled")!,
                false
            );
        }

        public void SetEnabled(bool enabled)
        {
            EnableChanges++;
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
            if (FailDeletion)
            {
                throw new InvalidOperationException("Simulated delete failure");
            }

            Deletions++;
            Record = null;
        }
    }
}
