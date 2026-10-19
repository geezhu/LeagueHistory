using System.IO.Compression;
using System.Text;
using League.Services;
using League.Tests.Support;

namespace League.Tests.Services;

/// <summary>
/// The Game Bar component: reading the version bundled beside the executable out of the MSIX, and saying in plain words
/// what the installed one is and what went wrong when a deployment failed.
///
/// The bundle is a zip, so the reading side is exercised against archives built here rather than a real package; the
/// installing side spawns PowerShell and touches the machine, so it is not driven at all.
/// </summary>
public class GameBarPackageInstallerTests : IDisposable
{
    private readonly TempFolder _folder = new("gamebar");

    public void Dispose()
    {
        _folder.Dispose();
        GC.SuppressFinalize(this);
    }

    private GameBarPackageInstaller Installer() => new(_folder.Root);

    /// <summary>Writes an archive at the package path holding the given files.</summary>
    private GameBarPackageInstaller Bundle(string manifest)
    {
        using (ZipArchive archive = ZipFile.Open(Installer().PackagePath, ZipArchiveMode.Create))
        {
            if (manifest.Length > 0)
            {
                using var writer = new StreamWriter(archive.CreateEntry("AppxManifest.xml").Open(), Encoding.UTF8);
                writer.Write(manifest);
            }
        }

        return Installer();
    }

    private static string Manifest(string version) =>
        "<Package><Identity Name=\"LeagueHistory.GameBar\" Version=\"" + version + "\" /></Package>";

    // ---- the package itself ----

    [Fact]
    public void PackagePath_IsThePackageInsideTheGivenDirectory()
    {
        Assert.Equal(Path.Combine(_folder.Root, "LeagueHistory.GameBar.msix"), Installer().PackagePath);
    }

    [Fact]
    public void PackageName_IsTheComponentIdentity()
    {
        Assert.Equal("LeagueHistory.GameBar", GameBarPackageInstaller.PackageName);
    }

    // ---- reading the bundled version ----

    [Fact]
    public void ReadBundledVersion_AManifestWithAVersion_ReadsIt()
    {
        Assert.Equal(new Version(1, 2, 3), Bundle(Manifest("1.2.3")).ReadBundledVersion());
    }

    [Fact]
    public void ReadBundledVersion_NoPackage_IsNull()
    {
        // Nothing bundled reads as "nothing bundled"; installing is what reports the real error.
        Assert.Null(Installer().ReadBundledVersion());
    }

    [Fact]
    public void ReadBundledVersion_AnArchiveWithoutAManifest_IsNull()
    {
        Assert.Null(Bundle("").ReadBundledVersion());
    }

    [Fact]
    public void ReadBundledVersion_AManifestWithoutAnIdentity_IsNull()
    {
        Assert.Null(Bundle("<Package><Other /></Package>").ReadBundledVersion());
    }

    [Fact]
    public void ReadBundledVersion_AnIdentityWithoutAVersion_IsNull()
    {
        Assert.Null(Bundle("<Package><Identity Name=\"LeagueHistory.GameBar\" /></Package>").ReadBundledVersion());
    }

    [Fact]
    public void ReadBundledVersion_AVersionThatIsNotAVersion_IsNull()
    {
        Assert.Null(Bundle(Manifest("not a version")).ReadBundledVersion());
    }

    [Fact]
    public void ReadBundledVersion_AFileThatIsNotAnArchive_IsNull()
    {
        // A damaged bundle must not throw here; the install path is what reports the failure.
        File.WriteAllText(Installer().PackagePath, "this is not a zip");

        Assert.Null(Installer().ReadBundledVersion());
    }

    // ---- the comparison ----

    [Fact]
    public void State_NeitherVersion_IsNotBundled()
    {
        Assert.Equal(GameBarPackageState.NotBundled, new GameBarPackageStatus(null, null).State);
    }

    [Fact]
    public void State_OnlyAnInstalledVersion_IsStillNotBundled()
    {
        // Without a bundle beside the program there is nothing to install from, whether or not one is installed.
        Assert.Equal(GameBarPackageState.NotBundled, new GameBarPackageStatus(null, new Version(1, 0)).State);
    }

    [Fact]
    public void State_BundledButNotInstalled_IsNotInstalled()
    {
        Assert.Equal(GameBarPackageState.NotInstalled, new GameBarPackageStatus(new Version(1, 0), null).State);
    }

    [Fact]
    public void State_AnOlderInstalledVersion_IsOutdated()
    {
        Assert.Equal(
            GameBarPackageState.Outdated,
            new GameBarPackageStatus(new Version(1, 2), new Version(1, 1)).State
        );
    }

    [Theory]
    [InlineData("1.2", "1.2")]
    [InlineData("1.2", "1.3")]
    public void State_TheSameOrANewerInstalledVersion_IsCurrent(string bundled, string installed)
    {
        // A newer one is current rather than a fourth state: there is nothing to do about it.
        Assert.Equal(
            GameBarPackageState.Current,
            new GameBarPackageStatus(Version.Parse(bundled), Version.Parse(installed)).State
        );
    }

    // ---- the wording ----

    [Fact]
    public void Text_NothingBundledAndNothingInstalled_SaysSo()
    {
        Assert.Equal("本程序未附带组件安装包，请使用完整发布包。", new GameBarPackageStatus(null, null).Text);
    }

    [Fact]
    public void Text_NothingBundledButSomethingInstalled_StillNamesTheInstalledVersion()
    {
        Assert.Equal("已安装 1.0；本程序未附带组件安装包。", new GameBarPackageStatus(null, new Version(1, 0)).Text);
    }

    [Fact]
    public void Text_BundledButNotInstalled_OffersTheBundledVersion()
    {
        Assert.Equal("未安装 · 可安装附带的 1.2", new GameBarPackageStatus(new Version(1, 2), null).Text);
    }

    [Fact]
    public void Text_Outdated_NamesBothVersions()
    {
        Assert.Equal("已安装 1.1 · 可更新到 1.2", new GameBarPackageStatus(new Version(1, 2), new Version(1, 1)).Text);
    }

    [Fact]
    public void Text_Current_NamesOnlyTheInstalledVersion()
    {
        Assert.Equal("已安装 1.2", new GameBarPackageStatus(new Version(1, 2), new Version(1, 2)).Text);
    }

    // ---- the failure wording ----

    [Theory]
    [InlineData(
        "Deployment failed with error 0x80073CF3",
        "缺少 Game Bar 运行库（Microsoft.VCLibs）。请先在微软商店更新 Xbox Game Bar，再重试。"
    )]
    [InlineData("0x800B0109 certificate", "本机签名证书未被信任，请重新安装并在管理员授权中选择“是”。")]
    [InlineData("0x800B010A certificate", "本机签名证书未被信任，请重新安装并在管理员授权中选择“是”。")]
    [InlineData("0x80073CFB already", "已安装同版本的开发版组件。请先在“设置 → 应用”中卸载 League 对局速览，再重试。")]
    [InlineData("0x80073D06 newer", "已安装更新版本的组件，无需重新安装。")]
    public void DescribeFailure_AKnownDeploymentError_IsExplained(string error, string expected)
    {
        Assert.Equal(expected, GameBarPackageInstaller.DescribeFailure(error));
    }

    [Fact]
    public void DescribeFailure_AKnownCodeIsFoundWhateverItsCase()
    {
        Assert.Equal(
            "已安装更新版本的组件，无需重新安装。",
            GameBarPackageInstaller.DescribeFailure("ERROR 0x80073d06 happened")
        );
    }

    [Fact]
    public void DescribeFailure_AnythingElse_KeepsTheFirstLineWindowsGave()
    {
        // The raw text is often several lines; the first non-empty one is the part a person can act on.
        Assert.Equal(
            "组件安装失败：Deployment failed",
            GameBarPackageInstaller.DescribeFailure("\n  Deployment failed  \nmore detail here")
        );
    }

    [Fact]
    public void DescribeFailure_EmptyText_SaysTheErrorIsUnknown()
    {
        Assert.Equal("组件安装失败：未知错误", GameBarPackageInstaller.DescribeFailure("   \n  \n"));
    }
}
