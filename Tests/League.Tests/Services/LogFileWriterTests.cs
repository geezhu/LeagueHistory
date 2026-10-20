using League.Services;
using League.Tests.Support;

namespace League.Tests.Services;

/// <summary>
/// The file behind the application log: it queues lines so a write never blocks the caller, flattens each line to one
/// so a stray newline cannot forge an entry, and rolls the file over once it grows past its limit.
///
/// A failing write is not allowed to break the caller - the batch is dropped and the reason kept in <c>LastError</c>,
/// which is what the caller shows.
/// </summary>
public class LogFileWriterTests : IDisposable
{
    private readonly TempFolder _folder = new("logfile");

    public void Dispose()
    {
        _folder.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Path_ => _folder.File("app.log");

    private static Task Flush(LogFileWriter writer) => writer.FlushAsync();

    // ---- writing ----

    [Fact]
    public async Task Write_ALine_ReachesTheFileAfterFlushing()
    {
        LogFileWriter writer = new(Path_, 1024 * 1024, 100);

        writer.Write("first");
        await Flush(writer);

        Assert.Equal(["first"], await File.ReadAllLinesAsync(Path_));
    }

    [Fact]
    public async Task Write_SeveralLines_KeepsTheirOrder()
    {
        LogFileWriter writer = new(Path_, 1024 * 1024, 100);

        writer.Write("one");
        writer.Write("two");
        writer.Write("three");
        await Flush(writer);

        Assert.Equal(["one", "two", "three"], await File.ReadAllLinesAsync(Path_));
    }

    [Fact]
    public async Task WriteNow_ALine_IsOnDiskWithoutFlushing()
    {
        // The crash path cannot wait for a background write, so this one is synchronous.
        LogFileWriter writer = new(Path_, 1024 * 1024, 100);

        writer.WriteNow("immediately");

        Assert.Equal(["immediately"], await File.ReadAllLinesAsync(Path_));
    }

    [Fact]
    public async Task WriteNow_AlsoFlushesWhatWasQueued()
    {
        // Whatever was already queued is written first, in order, then this line.
        LogFileWriter writer = new(Path_, 1024 * 1024, 100);
        writer.Write("queued");

        writer.WriteNow("now");

        Assert.Equal(["queued", "now"], await File.ReadAllLinesAsync(Path_));
    }

    [Theory]
    [InlineData("one\r\ntwo", "one  two")]
    [InlineData("one\ntwo", "one two")]
    [InlineData("one\rtwo", "one two")]
    public async Task Write_ALineWithABreakInside_IsFlattenedToOneLine(string line, string expected)
    {
        // Every entry is one line; a break inside would forge a second entry.
        LogFileWriter writer = new(Path_, 1024 * 1024, 100);

        writer.Write(line);
        await Flush(writer);

        Assert.Equal([expected], await File.ReadAllLinesAsync(Path_));
    }

    [Fact]
    public async Task Write_TheDirectoryDoesNotExist_IsCreated()
    {
        string nested = Path.Combine(_folder.Root, "deep", "deeper", "app.log");
        LogFileWriter writer = new(nested, 1024 * 1024, 100);

        writer.Write("line");
        await Flush(writer);

        Assert.Equal(["line"], await File.ReadAllLinesAsync(nested));
    }

    [Fact]
    public async Task Write_NoLinesAtAll_CreatesNoFile()
    {
        // Nothing queued means nothing to do, so an idle log does not litter an empty file.
        LogFileWriter writer = new(Path_, 1024 * 1024, 100);

        await Flush(writer);

        Assert.False(File.Exists(Path_), "Flushing an empty queue writes nothing.");
    }

    [Fact]
    public async Task Write_AppendsToAFileThatIsAlreadyThere()
    {
        await File.WriteAllTextAsync(Path_, "existing" + Environment.NewLine);
        LogFileWriter writer = new(Path_, 1024 * 1024, 100);

        writer.Write("added");
        await Flush(writer);

        Assert.Equal(["existing", "added"], await File.ReadAllLinesAsync(Path_));
    }

    // ---- rolling over ----

    [Fact]
    public async Task Write_AFilePastItsLimit_IsMovedAsideFirst()
    {
        // The old contents go to ".previous" so the current file starts from nothing.
        LogFileWriter writer = new(Path_, maxBytes: 8, maxPending: 100);
        writer.Write("this line is longer than the limit");
        await Flush(writer);

        writer.Write("next");
        await Flush(writer);

        Assert.Equal(["next"], await File.ReadAllLinesAsync(Path_));
        Assert.Equal(["this line is longer than the limit"], await File.ReadAllLinesAsync(Path_ + ".previous"));
    }

    [Fact]
    public async Task Write_AnExistingPreviousFile_IsOverwrittenByTheRollover()
    {
        // Only one generation is kept; the older one is not accumulated.
        await File.WriteAllTextAsync(Path_ + ".previous", "old generation" + Environment.NewLine);
        LogFileWriter writer = new(Path_, maxBytes: 8, maxPending: 100);
        writer.Write("this line is longer than the limit");
        await Flush(writer);

        writer.Write("next");
        await Flush(writer);

        Assert.Equal(["this line is longer than the limit"], await File.ReadAllLinesAsync(Path_ + ".previous"));
    }

    [Fact]
    public async Task Write_AFileUnderItsLimit_IsNotRolledOver()
    {
        LogFileWriter writer = new(Path_, maxBytes: 1024, maxPending: 100);

        writer.Write("one");
        await Flush(writer);
        writer.Write("two");
        await Flush(writer);

        Assert.Equal(["one", "two"], await File.ReadAllLinesAsync(Path_));
        Assert.False(File.Exists(Path_ + ".previous"), "There was no need to roll it over.");
    }

    // ---- the queue ----

    [Fact]
    public async Task Write_MoreLinesThanTheQueueHolds_KeepsTheNewestOnes()
    {
        // The oldest are dropped rather than the newest: a full queue means someone flooded it, and the latest
        // lines are the ones that explain what happened.
        LogFileWriter writer = new(Path_, 1024 * 1024, maxPending: 3);

        writer.Write("one");
        writer.Write("two");
        writer.Write("three");
        writer.Write("four");
        await Flush(writer);

        string[] lines = await File.ReadAllLinesAsync(Path_);
        Assert.Equal(3, lines.Length);
        Assert.Equal("four", lines[^1]);
        Assert.DoesNotContain("one", lines);
    }

    // ---- failures ----

    [Fact]
    public async Task Write_WhenTheDirectoryCannotBeCreated_KeepsTheReason()
    {
        // A path under a file cannot exist as a directory, so the write fails without throwing at the caller.
        string blocker = _folder.File("blocker");
        await File.WriteAllTextAsync(blocker, "");
        LogFileWriter writer = new(Path.Combine(blocker, "app.log"), 1024 * 1024, 100);

        writer.Write("line");
        await Flush(writer);

        Assert.NotNull(writer.LastError);
        Assert.StartsWith("日志保存失败：", writer.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_AfterAFailureAndThenASuccess_ClearsTheReason()
    {
        // LastError describes the most recent attempt, so a later success clears it.
        string blocker = _folder.File("blocker");
        await File.WriteAllTextAsync(blocker, "");
        bool failing = true;
        LogFileWriter writer = new(() => failing ? Path.Combine(blocker, "app.log") : Path_, 1024 * 1024, 100);

        writer.Write("line");
        await Flush(writer);
        Assert.NotNull(writer.LastError);

        failing = false;
        writer.Write("line");
        await Flush(writer);

        Assert.Null(writer.LastError);
    }

    // ---- the path ----

    [Fact]
    public void CurrentPath_IsWhatThePathFunctionSays()
    {
        // The function is consulted per write, so a log that rolls over by day reports the new file.
        LogFileWriter writer = new(() => Path_, 1024 * 1024, 100);

        Assert.Equal(Path_, writer.CurrentPath);
    }
}
