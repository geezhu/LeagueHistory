using League.Models;

namespace League.Tests.Models;

/// <summary>
/// The version strings the updater compares.
///
/// The file and the release tag both carry a leading "v" sometimes and not other times, so the parser strips it and
/// hands the rest to the framework's own version parser. Anything it cannot read becomes 0.0 rather than an error:
/// a build without a version file must still start, and the updater then sees a very old version and offers an update,
/// which is the safe way round.
///
/// The other two members of this type are not covered here. <c>GetLocalVersion</c> reads version.txt next to the
/// executable, which is the application's own directory, and <c>GetRemoteVersion</c> builds its own HttpClient and
/// calls GitHub - neither has a seam to substitute.
/// </summary>
public class VersionInfoTests
{
    // ---- the forms the tag takes ----

    [Theory]
    [InlineData("1.0.6")]
    [InlineData("v1.0.6")]
    [InlineData("V1.0.6")]
    public void Parse_EachOfTheThreeForms_IsTheSameVersion(string text)
    {
        Assert.Equal(new Version(1, 0, 6), VersionInfo.Parse(text));
    }

    [Fact]
    public void Parse_Padding_IsTrimmed()
    {
        Assert.Equal(new Version(1, 0, 6), VersionInfo.Parse("  v1.0.6  "));
    }

    [Fact]
    public void Parse_OnlyALeadingV_IsStripped()
    {
        // A "v" inside the string is not touched, and the framework cannot read the rest either - so the whole
        // string falls back rather than being partly parsed.
        Assert.Equal(new Version(0, 0), VersionInfo.Parse("1.0v"));
        Assert.Equal(new Version(1, 0), VersionInfo.Parse("v1.0"));
    }

    [Fact]
    public void Parse_SeveralComponents_AreKept()
    {
        Assert.Equal(new Version(1, 2, 3, 4), VersionInfo.Parse("v1.2.3.4"));
    }

    // ---- what cannot be read ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v")]
    [InlineData("dev")]
    [InlineData("1.0.0-beta")]
    public void Parse_SomethingThatIsNotAVersion_IsZero(string text)
    {
        // Zero means "very old", so the updater offers an update rather than failing to start.
        Assert.Equal(new Version(0, 0), VersionInfo.Parse(text));
    }

    [Fact]
    public void Parse_ZeroIsAZeroVersionNotNothing()
    {
        // "0.0.0" reads as a version, so it keeps its third component rather than falling back to 0.0.
        Assert.Equal(new Version(0, 0, 0), VersionInfo.Parse("0.0.0"));
    }

    // ---- what the updater does with it ----

    [Fact]
    public void Parse_AnUpdateIsNewer_WhichIsWhatTheUpdaterChecks()
    {
        // The whole point of parsing: a string comparison would put 1.10 below 1.9.
        Assert.True(VersionInfo.Parse("v1.10.0") > VersionInfo.Parse("v1.9.0"));
    }

    [Fact]
    public void Parse_AnUnreadableVersion_ComparesAsOlderThanAnyRealOne()
    {
        Assert.True(VersionInfo.Parse("v1.0.0") > VersionInfo.Parse("dev"));
    }
}
