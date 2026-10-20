using League.Services;
using League.Tests.Support;

namespace League.Tests.Services;

/// <summary>
/// The champion select assistant's own log: a bounded file for the exception details, while the panel shows a readable
/// reason of its own.
///
/// A line carries the stage that failed, the exception that was caught and its root cause, because the interesting one
/// is usually at the bottom. It deliberately carries no account names, player ids or chat text.
/// </summary>
public class AssistantDiagnosticsTests : IAsyncLifetime
{
    private readonly TempFolder _folder = new("assistant-diag");
    private AssistantDiagnostics? _diagnostics;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_diagnostics is not null)
        {
            await _diagnostics.FlushAsync();
        }

        _folder.Dispose();
    }

    private AssistantDiagnostics Diagnostics()
    {
        _diagnostics = new AssistantDiagnostics(_folder.File("assistant.log"));
        return _diagnostics;
    }

    private static async Task<string[]> LinesAsync(AssistantDiagnostics diagnostics)
    {
        await diagnostics.FlushAsync();
        return File.Exists(diagnostics.LogPath) ? await File.ReadAllLinesAsync(diagnostics.LogPath) : [];
    }

    [Fact]
    public void LogPath_IsWhatTheCallerGave()
    {
        Assert.Equal(_folder.File("assistant.log"), Diagnostics().LogPath);
    }

    [Fact]
    public async Task Report_NamesTheStageTheErrorAndTheRootCause()
    {
        // The root is what matters when the caught exception is a wrapper.
        AssistantDiagnostics diagnostics = Diagnostics();

        diagnostics.Report("解读英雄", new InvalidOperationException("坏了", new IOException("根因")));
        string line = Assert.Single(await LinesAsync(diagnostics));

        Assert.Contains(" stage=解读英雄 ", line, StringComparison.Ordinal);
        Assert.Contains("error=InvalidOperationException", line, StringComparison.Ordinal);
        Assert.Contains("root=IOException", line, StringComparison.Ordinal);
        Assert.Contains("message=根因", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_AnErrorThatIsItsOwnRoot_NamesItOnce()
    {
        AssistantDiagnostics diagnostics = Diagnostics();

        diagnostics.Report("阶段", new InvalidOperationException("坏了"));
        string line = Assert.Single(await LinesAsync(diagnostics));

        Assert.Contains(
            "error=InvalidOperationException root=InvalidOperationException",
            line,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Report_TheLineCarriesTheProcessId()
    {
        // One file serves every process, so a line has to say which one wrote it.
        AssistantDiagnostics diagnostics = Diagnostics();

        diagnostics.Report("阶段", new InvalidOperationException("坏了"));
        string line = Assert.Single(await LinesAsync(diagnostics));

        Assert.Contains($" pid={Environment.ProcessId} ", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_SeveralStages_KeepTheirOrder()
    {
        AssistantDiagnostics diagnostics = Diagnostics();

        diagnostics.Report("第一", new InvalidOperationException("一"));
        diagnostics.Report("第二", new InvalidOperationException("二"));
        string[] lines = await LinesAsync(diagnostics);

        Assert.Equal(2, lines.Length);
        Assert.Contains("stage=第一", lines[0], StringComparison.Ordinal);
        Assert.Contains("stage=第二", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_NothingYet_WritesNoFile()
    {
        AssistantDiagnostics diagnostics = Diagnostics();

        await diagnostics.FlushAsync();

        Assert.False(File.Exists(diagnostics.LogPath), "An idle log leaves no file behind.");
    }
}
