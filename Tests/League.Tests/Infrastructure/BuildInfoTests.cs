using League.Infrastructure;

namespace League.Tests.Infrastructure;

/// <summary>
/// Where this build came from: the version from version.txt, the commit the SDK baked into the informational version,
/// and the two facts the project file writes into the assembly metadata.
///
/// The wording is what the settings page and the diagnostic report show, so its branches are pinned: a development
/// build (0.0.0) is named rather than printed, a build without git says the commit is unknown, and "dirty" has three
/// states because "not recorded" is different from "clean".
/// </summary>
public class BuildInfoTests
{
    private static BuildInfo Info(
        string version = "1.1.4",
        string commit = "07688b8abcdef",
        bool? dirty = false,
        DateTime? builtAt = null
    ) => new(version, commit, dirty, builtAt);

    private static Dictionary<string, string> Metadata(params (string Key, string Value)[] entries) =>
        entries.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

    // ---- the short commit ----

    [Theory]
    [InlineData("07688b8abcdef", "07688b8")]
    [InlineData("abcdefgh", "abcdefg")]
    [InlineData("abcdefg", "abcdefg")]
    [InlineData("abcdef", "abcdef")]
    [InlineData("", "")]
    public void ShortCommit_IsTheFirstSevenCharacters(string commit, string expected)
    {
        Assert.Equal(expected, Info(commit: commit).ShortCommit);
    }

    // ---- the line shown to a person ----

    [Fact]
    public void Describe_AnOrdinaryBuild_NamesTheVersionAndTheShortCommit()
    {
        Assert.Equal("v1.1.4 · 提交 07688b8", Info().Describe());
    }

    [Fact]
    public void Describe_ADevelopmentBuild_IsNamedRatherThanPrinted()
    {
        // "v0.0.0" would read as a release; the reader needs to know it is not one.
        Assert.Equal("开发版 · 提交 07688b8", Info(version: "0.0.0").Describe());
    }

    [Fact]
    public void Describe_WithoutACommit_SaysItIsUnknown()
    {
        // A build without git has nothing to name, and saying so is better than an empty space.
        Assert.Equal("v1.1.4 · 提交未知", Info(commit: "").Describe());
    }

    [Fact]
    public void Describe_ADevelopmentBuildWithoutACommit_SaysBoth()
    {
        Assert.Equal("开发版 · 提交未知", Info(version: "0.0.0", commit: "").Describe());
    }

    [Fact]
    public void Describe_UncommittedChanges_AreNamed()
    {
        Assert.Equal("v1.1.4 · 提交 07688b8（含未提交改动）", Info(dirty: true).Describe());
    }

    [Fact]
    public void Describe_ACleanTree_AddsNothing()
    {
        Assert.Equal("v1.1.4 · 提交 07688b8", Info(dirty: false).Describe());
    }

    [Fact]
    public void Describe_AnUnrecordedTree_SaysItCannotBeTold()
    {
        // This is the case the third state exists for: not "clean", but "nobody wrote it down".
        Assert.Equal("v1.1.4 · 提交 07688b8（无法确认是否有未提交改动）", Info(dirty: null).Describe());
    }

    // ---- reading it from what the build wrote down ----

    [Fact]
    public void Parse_TheInformationalVersion_ProvidesTheCommit()
    {
        // The SDK writes "version+commit" into the product version.
        BuildInfo info = BuildInfo.Parse("1.0.0", "1.0.0+abc1234", Metadata());

        Assert.Equal("abc1234", info.Commit);
        Assert.Equal("1.0.0", info.Version);
    }

    [Fact]
    public void Parse_WithoutAPlus_ThereIsNoCommit()
    {
        // A build without git writes only the version.
        Assert.Equal("", BuildInfo.Parse("1.0.0", "1.0.0", Metadata()).Commit);
    }

    [Fact]
    public void Parse_TheCommitIsTrimmed()
    {
        Assert.Equal("abc1234", BuildInfo.Parse("1.0.0", "1.0.0+ abc1234 ", Metadata()).Commit);
    }

    [Fact]
    public void Parse_EverythingAfterTheFirstPlus_IsTheCommit()
    {
        // A commit hash cannot contain a plus, but the field may carry more than the hash.
        Assert.Equal("abc+def", BuildInfo.Parse("1.0.0", "1.0.0+abc+def", Metadata()).Commit);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Parse_TheDirtyFlag_IsReadFromItsMetadata(string stored, bool expected)
    {
        Assert.Equal(expected, BuildInfo.Parse("1.0.0", "1.0.0+x", Metadata(("SourceDirty", stored))).Dirty);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("")]
    [InlineData("1")]
    public void Parse_AnUnreadableDirtyFlag_IsUnknown(string stored)
    {
        // Neither clean nor dirty: the build did not say which.
        Assert.Null(BuildInfo.Parse("1.0.0", "1.0.0+x", Metadata(("SourceDirty", stored))).Dirty);
    }

    [Fact]
    public void Parse_NoDirtyFlagAtAll_IsUnknown()
    {
        Assert.Null(BuildInfo.Parse("1.0.0", "1.0.0+x", Metadata()).Dirty);
    }

    [Fact]
    public void Parse_TheBuildTime_IsReadAsUtc()
    {
        BuildInfo info = BuildInfo.Parse("1.0.0", "1.0.0+x", Metadata(("BuildTimeUtc", "2026-01-02T03:04:05Z")));

        Assert.NotNull(info.BuiltAtUtc);
        Assert.Equal(DateTimeKind.Utc, info.BuiltAtUtc!.Value.Kind);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), info.BuiltAtUtc.Value);
    }

    [Fact]
    public void Parse_AnOffsetBuildTime_IsConvertedToUtc()
    {
        // The stamp carries its own offset, so the stored value does not depend on where the build ran.
        BuildInfo info = BuildInfo.Parse("1.0.0", "1.0.0+x", Metadata(("BuildTimeUtc", "2026-01-02T05:04:05+02:00")));

        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), info.BuiltAtUtc!.Value.ToUniversalTime());
    }

    [Fact]
    public void Parse_AnUnreadableBuildTime_IsUnknown()
    {
        Assert.Null(BuildInfo.Parse("1.0.0", "1.0.0+x", Metadata(("BuildTimeUtc", "yesterday"))).BuiltAtUtc);
    }

    [Fact]
    public void Parse_NoBuildTimeAtAll_IsUnknown()
    {
        Assert.Null(BuildInfo.Parse("1.0.0", "1.0.0+x", Metadata()).BuiltAtUtc);
    }

    // ---- the real assembly ----

    [Fact]
    public void FromAssembly_ThisTestAssembly_ReadsWithoutFailing()
    {
        // Whatever the test build recorded, the reading must not throw - it runs at start-up for the real one.
        BuildInfo info = BuildInfo.FromAssembly(typeof(BuildInfo).Assembly);

        Assert.NotNull(info.Version);
        Assert.NotNull(info.Commit);
        Assert.NotEmpty(info.Describe());
    }

    [Fact]
    public void FromAssembly_TheBuildFromThisRepository_CarriesTheCommitAndTheDirtyFlag()
    {
        // A build-configuration guard rather than a unit test: if League.csproj stops feeding SourceRevisionId and
        // the uncommitted-changes flag into the assembly metadata, the diagnostic report silently loses the two facts
        // that say which build a user is running. This takes over the last two assertions of
        // Tests/StatisticsChecks/BuildInfoChecks.cs, which asserted exactly this and nothing else about the assembly.
        BuildInfo info = BuildInfo.FromAssembly(typeof(BuildInfo).Assembly);

        Assert.True(info.Commit.Length >= 7, $"The assembly records no commit (commit: '{info.Commit}').");
        Assert.NotNull(info.Dirty);
    }
}
