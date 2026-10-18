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

    [Fact]
    public async Task ReadAsync_ManagedRunningTask_ReportsThePlanFromTheTask()
    {
        var service = await RunningManagedAsync();

        var view = await service.ReadAsync();

        Assert.Equal("23:00", view.Plan.Time);
        Assert.Equal(30, view.Plan.CountdownSeconds);
        Assert.True(view.Plan.Enabled);
    }

    [Fact]
    public async Task ReadAsync_ManagedRunningTask_IsReportedAsOurs()
    {
        var service = await RunningManagedAsync();

        var view = await service.ReadAsync();

        Assert.True(view.Exists);
        Assert.True(view.Managed);
        Assert.True(view.Running);
    }

    [Fact]
    public async Task ReadAsync_RunningWorker_SaysSo()
    {
        var service = await RunningManagedAsync();

        var view = await service.ReadAsync();

        Assert.Contains("正在执行", view.Message);
    }

    [Fact]
    public async Task ReadAsync_FreshStatus_IsShownAsTheCurrentState()
    {
        var service = await RunningManagedAsync();

        var view = await service.ReadAsync();

        Assert.Contains("正在检查对局", view.Message);
    }

    [Fact]
    public async Task ReadAsync_FreshStatus_PassesTheOccurrenceOn()
    {
        var service = await RunningManagedAsync();

        var view = await service.ReadAsync();

        Assert.Equal("2026-09-18T23:00:00", view.Occurrence);
    }

    [Fact]
    public async Task ReadAsync_ManagedTask_NextRunIsTheTriggerTime()
    {
        var service = await RunningManagedAsync();

        var view = await service.ReadAsync();

        Assert.Equal<DateTime?>(new DateTime(2026, 9, 18, 23, 0, 0), view.NextRun);
    }

    [Fact]
    public async Task ReadAsync_SkippedOccurrence_IsAnnounced()
    {
        var service = await RunningManagedAsync();
        await File.WriteAllTextAsync(_folder.File("cancel.json"), "{\"Occurrence\":\"2026-09-18T23:00:00\"}");

        var view = await service.ReadAsync();

        Assert.Contains("下一次计划已跳过", view.Message);
    }

    [Fact]
    public async Task ReadAsync_SkippedOccurrence_MovesTheNextRunADay()
    {
        var service = await RunningManagedAsync();
        await File.WriteAllTextAsync(_folder.File("cancel.json"), "{\"Occurrence\":\"2026-09-18T23:00:00\"}");

        var view = await service.ReadAsync();

        Assert.Equal<DateTime?>(new DateTime(2026, 9, 19, 23, 0, 0), view.NextRun);
    }

    [Fact]
    public async Task ReadAsync_TriggerDaysAway_IsNotMovedBackToTomorrow()
    {
        var service = Service();
        _tasks.Record = new(TaskXml(new(true, "23:00", 30), new DateTime(2026, 9, 20, 22, 0, 0)), true, false);

        var view = await service.ReadAsync();

        Assert.Equal<DateTime?>(new DateTime(2026, 9, 20, 23, 0, 0), view.NextRun);
    }

    [Fact]
    public async Task ReadAsync_ForeignTask_IsNotOurs()
    {
        var service = await ForeignAsync();

        var view = await service.ReadAsync();

        Assert.False(view.Managed);
    }

    [Fact]
    public async Task ReadAsync_ForeignTask_KeepsTheDefaultCountdown()
    {
        var service = await ForeignAsync();

        var view = await service.ReadAsync();

        Assert.Equal(ScheduledShutdownService.DefaultCountdownSeconds, view.Plan.CountdownSeconds);
    }

    [Fact]
    public async Task ReadAsync_ForeignTask_IsAnnouncedAsTheOriginalScript()
    {
        var service = await ForeignAsync();

        var view = await service.ReadAsync();

        Assert.Contains("已发现原有脚本", view.Message);
    }

    [Fact]
    public async Task ReadAsync_ForeignTask_KeepsItsRunningState()
    {
        var service = await ForeignAsync();

        var view = await service.ReadAsync();

        Assert.Contains("正在执行", view.Message);
    }

    [Fact]
    public async Task ReadAsync_ForeignTask_HasNoWorkerStatus()
    {
        var service = await ForeignAsync();

        var view = await service.ReadAsync();

        Assert.Null(view.Occurrence);
        Assert.NotNull(view.NextRun);
    }

    [Fact]
    public async Task ReadAsync_StatusOlderThanAMinute_IsReportedAsTheLatestState()
    {
        var service = Service();
        _tasks.Record = new(TaskXml(new(true, "23:00", 30), Now), true, false);
        await File.WriteAllTextAsync(
            _folder.File("status.json"),
            StatusJson("上次尝试被跳过", DateTimeOffset.UtcNow.AddMinutes(-5))
        );

        var view = await service.ReadAsync();

        Assert.Contains("最近状态：上次尝试被跳过", view.Message);
    }

    [Fact]
    public async Task ReadAsync_StatusOlderThanAMinute_DoesNotClaimAnOccurrence()
    {
        var service = Service();
        _tasks.Record = new(TaskXml(new(true, "23:00", 30), Now), true, false);
        await File.WriteAllTextAsync(
            _folder.File("status.json"),
            StatusJson("上次尝试被跳过", DateTimeOffset.UtcNow.AddMinutes(-5))
        );

        var view = await service.ReadAsync();

        Assert.Null(view.Occurrence);
    }

    [Fact]
    public async Task ReadAsync_UnreadableConfiguration_FallsBackToTheDefaultCountdown()
    {
        var service = Service();
        _tasks.Record = new(TaskXml(new(true, "23:00", 30), Now), true, false);
        await File.WriteAllTextAsync(_folder.File("plan.json"), "not json at all");

        var view = await service.ReadAsync();

        Assert.Equal(ScheduledShutdownService.DefaultCountdownSeconds, view.Plan.CountdownSeconds);
        Assert.Equal("23:00", view.Plan.Time);
    }

    [Fact]
    public async Task ReadAsync_UnreadableStatus_IsLeftOut()
    {
        var service = Service();
        _tasks.Record = new(TaskXml(new(true, "23:00", 30), Now), true, false);
        await File.WriteAllTextAsync(_folder.File("status.json"), "not json either");

        var view = await service.ReadAsync();

        Assert.DoesNotContain("最近状态", view.Message);
    }

    [Fact]
    public async Task ReadAsync_OpeningAndRefreshing_DoesNotMutateTheStoredTask()
    {
        var service = await RunningManagedAsync();
        string task = _tasks.Record!.Xml;

        await service.ReadAsync();
        await service.ReadAsync();

        Assert.Equal(task, _tasks.Record!.Xml);
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

    [Fact]
    public async Task CreateAsync_UnrecognizedSameNamedTask_IsNeverOverwritten()
    {
        var service = Service();
        _tasks.Record = new(UnrelatedTaskXml(), false, false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("23:00", 30));

        Assert.Equal(0, _tasks.Registrations);
    }

    [Theory]
    [InlineData("25:00", 30)]
    [InlineData("9:00", 30)]
    [InlineData("23:00", 301)]
    public async Task CreateAsync_InvalidOptions_AreRefusedBeforeTheTaskIsTouched(string time, int countdownSeconds)
    {
        var service = Service();

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(time, countdownSeconds));

        Assert.Equal(0, _tasks.Registrations);
    }

    [Fact]
    public async Task CreateAsync_FailedRegistration_RestoresTheExistingPlan()
    {
        var service = Service();
        _tasks.Record = new(LegacyTaskXml("old"), false, false);
        await service.CreateAsync("23:00", 30);
        string previous = await File.ReadAllTextAsync(_folder.File("plan.json"));
        _tasks.FailRegistration = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("23:00", 30));

        Assert.Equal(previous, await File.ReadAllTextAsync(_folder.File("plan.json")));
    }

    [Fact]
    public async Task CreateAsync_RunningWorker_IsStoppedBeforeTheReplacement()
    {
        var service = Service();
        _tasks.Record = new(TaskXml(new(true, "23:00", 30), Now), true, true);

        var replacement = await service.CreateAsync("01:15", 45);

        Assert.Equal(1, _tasks.Stops);
        Assert.False(replacement.Running);
    }

    [Fact]
    public async Task CreateAsync_Replacement_StartsAtTheNextFutureOccurrence()
    {
        var service = Service();
        _tasks.Record = new(TaskXml(new(true, "23:00", 30), Now), true, true);

        var replacement = await service.CreateAsync("01:15", 45);

        Assert.Equal("01:15", replacement.Plan.Time);
        Assert.Equal(45, replacement.Plan.CountdownSeconds);
        Assert.Equal<DateTime?>(new DateTime(2026, 9, 18, 1, 15, 0), replacement.NextRun);
    }

    [Fact]
    public async Task CreateAsync_HalfSavedPlan_IsDisarmedInsteadOfLeftEnabled()
    {
        var service = Service();
        Directory.CreateDirectory(_folder.File("cancel.json")); // The atomic write cannot replace a folder.

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("23:00", 30));

        Assert.False(_tasks.LastEnabled);
    }

    [Fact]
    public async Task CreateAsync_HalfSavedPlan_DisarmsTheConfigurationOnDisk()
    {
        var service = Service();
        Directory.CreateDirectory(_folder.File("cancel.json"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("23:00", 30));

        var config = JObject.Parse(await File.ReadAllTextAsync(_folder.File("plan.json")));
        Assert.False((bool?)config["Enabled"]);
    }

    [Fact]
    public async Task CreateAsync_HalfSavedPlan_LeavesNoTemporaryFileBehind()
    {
        var service = Service();
        Directory.CreateDirectory(_folder.File("cancel.json"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("23:00", 30));

        Assert.Empty(Directory.EnumerateFiles(_folder.Root, "*.tmp"));
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

    [Fact]
    public async Task DeleteAsync_FailedDeletion_LeavesTheTaskDisabledAndStopped()
    {
        var service = Service();
        _tasks.Record = new(TaskXml(new(true, "23:00", 30), Now), true, true);
        _tasks.FailDeletion = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync());

        Assert.False(_tasks.Record!.Enabled);
        Assert.False(_tasks.Record!.Running);
    }

    [Fact]
    public async Task DeleteAsync_UnrelatedSameNamedTask_IsNotTouched()
    {
        var service = Service();
        _tasks.Record = new(UnrelatedTaskXml(), false, false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync());

        Assert.Equal(0, _tasks.Deletions);
    }

    [Fact]
    public async Task DeleteAsync_KeepsLogsAndTheOriginalTaskBackup()
    {
        var service = Service();
        string task = TaskXml(new(true, "23:00", 30), Now);
        _tasks.Record = new(task, true, false);
        await File.WriteAllTextAsync(_folder.File("shutdown.log"), "test log");

        await service.DeleteAsync();

        Assert.Equal("test log", await File.ReadAllTextAsync(_folder.File("shutdown.log")));
        Assert.Equal(task, await File.ReadAllTextAsync(_folder.File("original-task.xml")));
    }

    [Fact]
    public async Task DeleteAsync_AfterDeletion_ExplicitCreationEnablesTheNewPlan()
    {
        var service = Service();
        _tasks.Record = new(TaskXml(new(true, "23:00", 30), Now), true, false);
        await service.DeleteAsync();

        var recreated = await service.CreateAsync("23:30", 30);

        Assert.True(recreated.Exists);
        Assert.True(recreated.Plan.Enabled);
    }

    // ---------------------------------------------------------------- fixtures

    private ScheduledShutdownService Service() => new(_folder.Root, _tasks, () => Now);

    private async Task<ScheduledShutdownService> RunningManagedAsync()
    {
        var service = Service();
        _tasks.Record = new(TaskXml(new(true, "23:00", 30), Now), true, true);
        await File.WriteAllTextAsync(_folder.File("plan.json"), PlanJson(30));
        await File.WriteAllTextAsync(
            _folder.File("status.json"),
            FreshStatusJson("正在检查对局", "2026-09-18T23:00:00")
        );
        return service;
    }

    private async Task<ScheduledShutdownService> ForeignAsync()
    {
        var service = Service();
        _tasks.Record = new(LegacyTaskXml("someone else's task"), true, true);
        await File.WriteAllTextAsync(_folder.File("plan.json"), PlanJson(30));
        return service;
    }

    private string TaskXml(ShutdownPlan plan, DateTime now) =>
        ScheduledShutdownService.BuildTaskXml(plan, _folder.Root, Sid(), now);

    private string LegacyTaskXml(string description)
    {
        var xml = XDocument.Parse(TaskXml(new(true, "23:00", 30), Now));
        xml.Descendants(Ns + "Description").Single().Value = description;
        return xml.ToString();
    }

    private string UnrelatedTaskXml()
    {
        var xml = XDocument.Parse(LegacyTaskXml("old"));
        xml.Descendants(Ns + "Arguments").Single().Value = "-Command Write-Output ShutdownAfterLoL.ps1";
        return xml.ToString();
    }

    private static string Sid() => WindowsIdentity.GetCurrent().User!.Value;

    private static string PlanJson(int countdownSeconds) =>
        "{\"Version\":1,\"Enabled\":true,\"Time\":\"23:00\",\"CountdownSeconds\":" + countdownSeconds + "}";

    private static string FreshStatusJson(string message, string occurrence) =>
        "{\"UpdatedAt\":\""
        + DateTimeOffset.UtcNow.ToString("O")
        + "\",\"Message\":\""
        + message
        + "\",\"Occurrence\":\""
        + occurrence
        + "\"}";

    private static string StatusJson(string message, DateTimeOffset updatedAt) =>
        "{\"UpdatedAt\":\"" + updatedAt.ToString("O") + "\",\"Message\":\"" + message + "\"}";

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
