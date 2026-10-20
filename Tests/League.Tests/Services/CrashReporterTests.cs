using League.Services;
using League.Tests.Support;
using Newtonsoft.Json;

namespace League.Tests.Services;

/// <summary>
/// The marker left behind when the process dies, and the one-shot read that reports it on the next start.
///
/// Reading is destructive by design: the marker is deleted once it has been handed over, so the next start does not
/// warn again about the same crash. A marker that cannot be read is treated as absent and left where it is - deleting
/// it would throw away the only record of what happened.
/// </summary>
public class CrashReporterTests : IDisposable
{
    private readonly TempFolder _folder = new("crash");

    public void Dispose()
    {
        _folder.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Marker_ => Path.Combine(_folder.Root, "logs", "last-crash.json");

    // ---- leaving a marker ----

    [Fact]
    public void WriteMarker_CreatesTheDirectoryItNeeds()
    {
        // The marker is written while the process is going away, so it cannot assume anything exists.
        CrashReporter.WriteMarker("fatal", new InvalidOperationException("坏了"), Marker_);

        Assert.True(File.Exists(Marker_));
    }

    [Fact]
    public void WriteMarker_NamesTheErrorByItsTypeAndMessage()
    {
        CrashReporter.WriteMarker("fatal", new InvalidOperationException("坏了"), Marker_);

        CrashMarker? marker = CrashReporter.TakeLastCrash(Marker_);
        Assert.Equal("fatal", marker?.Kind);
        Assert.Equal("InvalidOperationException: 坏了", marker?.Error);
    }

    [Fact]
    public void WriteMarker_RecordsTheBuildAndTheTime()
    {
        // The version and commit are what makes a report actionable, so they are always filled in.
        CrashReporter.WriteMarker("fatal", new InvalidOperationException("坏了"), Marker_);

        CrashMarker? marker = CrashReporter.TakeLastCrash(Marker_);
        Assert.NotNull(marker);
        Assert.NotEqual(default, marker.Time);
        Assert.NotNull(marker.Version);
        Assert.NotNull(marker.Commit);
    }

    [Fact]
    public void WriteMarker_OverwritesAnEarlierMarker()
    {
        // Only the last crash on the way down matters; earlier ones are not accumulated.
        CrashReporter.WriteMarker("first", new InvalidOperationException("第一"), Marker_);
        CrashReporter.WriteMarker("second", new InvalidOperationException("第二"), Marker_);

        CrashMarker? marker = CrashReporter.TakeLastCrash(Marker_);
        Assert.Equal("second", marker?.Kind);
        Assert.Equal("InvalidOperationException: 第二", marker?.Error);
    }

    [Fact]
    public void WriteMarker_APathThatCannotBeWritten_IsSwallowed()
    {
        // The process is going away; failing to leave a marker costs one prompt at the next start, nothing more.
        string blocker = Path.Combine(_folder.Root, "blocker");
        File.WriteAllText(blocker, "");

        CrashReporter.WriteMarker(
            "fatal",
            new InvalidOperationException("坏了"),
            Path.Combine(blocker, "last-crash.json")
        );

        Assert.True(File.Exists(blocker), "The write failed, and the failure was not thrown.");
    }

    // ---- reading it back ----

    [Fact]
    public void TakeLastCrash_NoMarkerAtAll_IsNull()
    {
        Assert.Null(CrashReporter.TakeLastCrash(Marker_));
    }

    [Fact]
    public void TakeLastCrash_AMarker_IsHandedOverOnceAndRemoved()
    {
        // The point of removing it is that the next start does not warn about the same crash again.
        CrashReporter.WriteMarker("fatal", new InvalidOperationException("坏了"), Marker_);

        Assert.NotNull(CrashReporter.TakeLastCrash(Marker_));
        Assert.Null(CrashReporter.TakeLastCrash(Marker_));
        Assert.False(File.Exists(Marker_), "The marker is removed once it has been read.");
    }

    [Fact]
    public void TakeLastCrash_ADamagedMarker_IsNullAndStaysWhereItIs()
    {
        // Deleting it would throw away the only record of what happened; refusing it is enough.
        Directory.CreateDirectory(Path.GetDirectoryName(Marker_)!);
        File.WriteAllText(Marker_, "{ not json ");

        Assert.Null(CrashReporter.TakeLastCrash(Marker_));
        Assert.True(File.Exists(Marker_), "A marker that cannot be read is left for someone to look at.");
    }

    [Fact]
    public void TakeLastCrash_AMarkerThatHoldsNull_IsNull()
    {
        // Deserializing "null" gives nothing back; there is no crash to report.
        Directory.CreateDirectory(Path.GetDirectoryName(Marker_)!);
        File.WriteAllText(Marker_, "null");

        Assert.Null(CrashReporter.TakeLastCrash(Marker_));
    }

    [Fact]
    public void TakeLastCrash_ARoundTrip_PreservesEveryField()
    {
        // The marker is a plain record, so what was written is what comes back.
        CrashMarker written = new(
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local),
            "1.2.3",
            "abc1234",
            "fatal",
            "错误"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(Marker_)!);
        File.WriteAllText(Marker_, JsonConvert.SerializeObject(written));

        CrashMarker? read = CrashReporter.TakeLastCrash(Marker_);

        Assert.NotNull(read);
        Assert.Equal("1.2.3", read.Version);
        Assert.Equal("abc1234", read.Commit);
        Assert.Equal("fatal", read.Kind);
        Assert.Equal("错误", read.Error);
        Assert.Equal(written.Time, read.Time);
    }
}
