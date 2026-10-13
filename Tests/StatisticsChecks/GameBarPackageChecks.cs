using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using League.Services;
using static StatisticsCheckSupport;

/// <summary>Bundled package reading, status wording and failure messages. Nothing is installed or trusted.</summary>
internal static class GameBarPackageChecks
{
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "league-gamebar-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var installer = new GameBarPackageInstaller(directory);
            Check(installer.ReadBundledVersion() == null, "A missing bundle reads as not bundled");
            WritePackage(installer.PackagePath, "1.0.0.30");
            Check(
                installer.ReadBundledVersion() == new Version(1, 0, 0, 30),
                "The bundled version comes from the manifest"
            );
            WriteCertificate(installer.CertificatePath);
            Check(!installer.IsCertificateTrusted(), "A fresh certificate is not reported as trusted");
            File.WriteAllText(installer.PackagePath, "not a zip");
            Check(installer.ReadBundledVersion() == null, "A damaged bundle reads as not bundled");
        }
        finally
        {
            Directory.Delete(directory, true);
        }

        CheckStates();
        CheckFailures();
        Console.WriteLine(
            "PASS: Game Bar bundle version, certificate trust check, install states and failure messages."
        );
    }

    private static void CheckStates()
    {
        Version v29 = new(1, 0, 0, 29),
            v30 = new(1, 0, 0, 30);
        Check(
            new GameBarPackageStatus(null, null).State == GameBarPackageState.NotBundled,
            "No bundle, nothing installed"
        );
        Check(
            new GameBarPackageStatus(v30, null).State == GameBarPackageState.NotInstalled,
            "Bundled but not installed"
        );
        Check(new GameBarPackageStatus(v30, v29).State == GameBarPackageState.Outdated, "Older install can update");
        Check(new GameBarPackageStatus(v29, v30).State == GameBarPackageState.Current, "A newer install is current");
        Check(new GameBarPackageStatus(v30, v29).Text.Contains("可更新到 1.0.0.30"), "Update text names the version");
    }

    private static void CheckFailures()
    {
        Check(
            GameBarPackageInstaller.DescribeFailure("Deployment failed with HRESULT: 0x80073CF3").Contains("VCLibs"),
            "Missing framework dependency is explained"
        );
        Check(
            GameBarPackageInstaller.DescribeFailure("error 0x800B0109: root certificate").Contains("证书"),
            "Untrusted signature is explained"
        );
        Check(
            GameBarPackageInstaller.DescribeFailure("0x80073CFB package already installed").Contains("开发版"),
            "A conflicting developer registration is explained"
        );
        Check(
            GameBarPackageInstaller.DescribeFailure("\r\nSomething else\r\nmore") == "组件安装失败：Something else",
            "Unknown errors keep Windows' first line"
        );
    }

    private static void WritePackage(string path, string version)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry("AppxManifest.xml").Open());
        writer.Write(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><Package xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\">"
                + $"<Identity Name=\"LeagueHistory.GameBar\" Publisher=\"CN=LeagueHistory\" Version=\"{version}\" /></Package>"
        );
    }

    private static void WriteCertificate(string path)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=LeagueHistory",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Cert));
    }
}
