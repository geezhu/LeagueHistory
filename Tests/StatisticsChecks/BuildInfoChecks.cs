using League.Infrastructure;

internal static class BuildInfoChecks
{
    public static void Run()
    {
        var empty = new Dictionary<string, string>();
        var clean = BuildInfo.Parse(
            "1.1.4",
            "1.0.0+07688b871dca78e25744eee341d66cb582397657",
            new Dictionary<string, string>
            {
                ["SourceDirty"] = "false",
                ["BuildTimeUtc"] = "2026-10-16T01:40:23.0000000Z",
            }
        );
        Check(clean.Commit == "07688b871dca78e25744eee341d66cb582397657", "Commit comes from the product version");
        Check(clean.ShortCommit == "07688b8", "Short commit has seven characters");
        Check(clean.Describe() == "v1.1.4 · 提交 07688b8", "Clean build shows version and commit only");
        Check(clean.BuiltAtUtc == new DateTime(2026, 10, 16, 1, 40, 23, DateTimeKind.Utc), "Build time is UTC");

        var dirty = BuildInfo.Parse(
            "1.1.4",
            "1.0.0+abcdef0123",
            new Dictionary<string, string> { ["SourceDirty"] = "true" }
        );
        Check(dirty.Describe() == "v1.1.4 · 提交 abcdef0（含未提交改动）", "Dirty build is marked");

        var unknownState = BuildInfo.Parse(
            "1.1.4",
            "1.0.0+abcdef0123",
            new Dictionary<string, string> { ["SourceDirty"] = "unknown" }
        );
        Check(unknownState.Dirty == null && unknownState.Describe().Contains("无法确认"), "Unknown git state is shown");

        var development = BuildInfo.Parse("0.0.0", "1.0.0", empty);
        Check(development.Describe() == "开发版 · 提交未知", "A build without git or version.txt says so");
        Check(development.BuiltAtUtc == null, "Missing build time stays unknown");

        // The real assembly must carry the commit when built from this repository.
        var current = BuildInfo.FromAssembly(typeof(BuildInfo).Assembly);
        Check(current.Commit.Length >= 7, "League.dll records the source commit");
        Check(current.Dirty != null, "League.dll records whether the tree had uncommitted changes");
        Console.WriteLine("PASS: build commit, uncommitted-change marker, build time and settings description.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
