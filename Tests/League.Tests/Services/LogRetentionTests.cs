using League.Services;
using League.Tests.Support;

namespace League.Tests.Services;

/// <summary>
/// The rules that keep the log folder from growing without end: nothing older than a week, and no more than twenty
/// megabytes in total once the old ones are gone.
///
/// Two limits in order, and the order matters - age first, because an old file is worth less than a recent one however
/// small it is; size second, oldest first, because that is the same judgement at a finer grain. Only the top level is
/// looked at and only log-shaped names, so a folder the user put something in is left alone.
/// </summary>
public class LogRetentionTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 1, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly TempFolder _folder = new("log-retention");

    public void Dispose()
    {
        _folder.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Directory() => _folder.File("logs-" + Guid.NewGuid().ToString("N"));

    /// <summary>A file of the given size, last written the given number of days ago.</summary>
    private static string Write(string directory, string name, int bytes, double daysAgo = 0)
    {
        System.IO.Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, Now.AddDays(-daysAgo));
        return path;
    }

    // ---- the constants ----

    [Fact]
    public void MaxAge_IsSevenDays()
    {
        Assert.Equal(TimeSpan.FromDays(7), LogRetention.MaxAge);
    }

    [Fact]
    public void MaxTotalBytes_IsTwentyMegabytes()
    {
        Assert.Equal(20L * 1024 * 1024, LogRetention.MaxTotalBytes);
    }

    // ---- nothing to do ----

    [Fact]
    public void Clean_NoDirectory_DeletesNothing()
    {
        // The first run has no log folder yet.
        Assert.Equal(0, LogRetention.Clean(_folder.File("missing"), Now));
    }

    [Fact]
    public void Clean_AnEmptyDirectory_DeletesNothing()
    {
        string directory = Directory();
        System.IO.Directory.CreateDirectory(directory);

        Assert.Equal(0, LogRetention.Clean(directory, Now));
    }

    [Fact]
    public void Clean_OnlyRecentFiles_DeletesNothing()
    {
        string directory = Directory();
        string file = Write(directory, "a.log", bytes: 10, daysAgo: 1);

        Assert.Equal(0, LogRetention.Clean(directory, Now));
        Assert.True(File.Exists(file));
    }

    // ---- the age rule ----

    [Fact]
    public void Clean_AFileOlderThanTheLimit_IsDeleted()
    {
        string directory = Directory();
        string old = Write(directory, "old.log", bytes: 10, daysAgo: 8);

        Assert.Equal(1, LogRetention.Clean(directory, Now));
        Assert.False(File.Exists(old));
    }

    [Fact]
    public void Clean_AFileJustInsideTheLimit_IsKept()
    {
        // Traced: written 6.99 days ago, so it is inside the seven-day window.
        string directory = Directory();
        string recent = Write(directory, "recent.log", bytes: 10, daysAgo: 6.99);

        Assert.Equal(0, LogRetention.Clean(directory, Now));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void Clean_TheAgeLimitCanBeOverridden()
    {
        // The signature takes it so a caller can Clean on a different schedule without editing the constant.
        string directory = Directory();
        string file = Write(directory, "a.log", bytes: 10, daysAgo: 2);

        Assert.Equal(1, LogRetention.Clean(directory, Now, maxAge: TimeSpan.FromDays(1)));
        Assert.False(File.Exists(file));
    }

    // ---- the size rule ----

    [Fact]
    public void Clean_OverTheSizeLimit_DeletesFromTheOldest()
    {
        // Traced: three files of ten bytes make thirty, against a limit of twenty - so exactly one goes, the oldest.
        string directory = Directory();
        string oldest = Write(directory, "one.log", bytes: 10, daysAgo: 3);
        string middle = Write(directory, "two.log", bytes: 10, daysAgo: 2);
        string newest = Write(directory, "three.log", bytes: 10, daysAgo: 1);

        Assert.Equal(1, LogRetention.Clean(directory, Now, maxTotalBytes: 20));
        Assert.False(File.Exists(oldest));
        Assert.True(File.Exists(middle));
        Assert.True(File.Exists(newest));
    }

    [Fact]
    public void Clean_FarOverTheSizeLimit_KeepsDeletingUntilUnderIt()
    {
        // Traced: five of ten bytes make fifty; twenty is the limit, so three go and twenty is left.
        string directory = Directory();
        for (int i = 0; i < 5; i++)
        {
            Write(directory, $"f{i}.log", bytes: 10, daysAgo: 5 - i);
        }

        Assert.Equal(3, LogRetention.Clean(directory, Now, maxTotalBytes: 20));
        Assert.Equal(20, System.IO.Directory.GetFiles(directory, "*.log").Sum(f => new FileInfo(f).Length));
    }

    [Fact]
    public void Clean_ExactlyAtTheSizeLimit_DeletesNothing()
    {
        // The limit is inclusive: at twenty it is not over twenty.
        string directory = Directory();
        Write(directory, "a.log", bytes: 10, daysAgo: 2);
        Write(directory, "b.log", bytes: 10, daysAgo: 1);

        Assert.Equal(0, LogRetention.Clean(directory, Now, maxTotalBytes: 20));
    }

    [Fact]
    public void Clean_BothRulesAtOnce_CountsEveryDeletion()
    {
        // Traced: one file is eleven days old and goes on age; the rest are thirty bytes against a limit of twenty,
        // so one more goes - two deletions in all.
        string directory = Directory();
        Write(directory, "ancient.log", bytes: 5, daysAgo: 11);
        Write(directory, "one.log", bytes: 10, daysAgo: 3);
        Write(directory, "two.log", bytes: 10, daysAgo: 2);
        Write(directory, "three.log", bytes: 10, daysAgo: 1);

        Assert.Equal(2, LogRetention.Clean(directory, Now, maxTotalBytes: 20));
    }

    // ---- what is looked at ----

    [Theory]
    [InlineData("a.log")]
    [InlineData("a.log.previous")]
    [InlineData("a.txt")]
    public void Clean_TheLogShapedNames_AreIncluded(string name)
    {
        string directory = Directory();
        string file = Write(directory, name, bytes: 10, daysAgo: 8);

        Assert.Equal(1, LogRetention.Clean(directory, Now));
        Assert.False(File.Exists(file));
    }

    [Theory]
    [InlineData("a.bin")]
    [InlineData("a.json")]
    [InlineData("a")]
    public void Clean_AnythingElse_IsLeftAlone(string name)
    {
        // Only the log folder's own files are the program's to delete.
        string directory = Directory();
        string file = Write(directory, name, bytes: 10, daysAgo: 30);

        Assert.Equal(0, LogRetention.Clean(directory, Now));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Clean_ASubDirectory_IsNotEntered()
    {
        // The rule is about the top level only, so a folder the user made inside it is not touched.
        string directory = Directory();
        string nested = Path.Combine(directory, "keep");
        string file = Write(nested, "old.log", bytes: 10, daysAgo: 30);

        Assert.Equal(0, LogRetention.Clean(directory, Now));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Clean_TwoPatternsThatWouldMatchOneFile_CountItOnce()
    {
        // The patterns are a union, not a concatenation; a file matched twice must not be deleted twice.
        string directory = Directory();
        Write(directory, "a.log", bytes: 10, daysAgo: 8);

        Assert.Equal(1, LogRetention.Clean(directory, Now));
    }

    // ---- the folder it uses by default ----

    [Fact]
    public void LogsDirectory_IsUnderTheDataRoot()
    {
        // The test setup points the data root at a temporary folder, so this is the temporary one.
        Assert.Contains("Logs", LogRetention.LogsDirectory, StringComparison.Ordinal);
    }
}
