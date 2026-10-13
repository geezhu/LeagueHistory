using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using League.Services;
using static StatisticsCheckSupport;

/// <summary>
/// Bundled package reading, local signing, status wording and failure messages. Signing uses a
/// throwaway certificate that never enters a certificate store; nothing is installed or trusted.
/// </summary>
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
            File.WriteAllText(installer.PackagePath, "not a zip");
            Check(installer.ReadBundledVersion() == null, "A damaged bundle reads as not bundled");
        }
        finally
        {
            Directory.Delete(directory, true);
        }

        CheckStates();
        CheckFailures();
        string signing = CheckSigning();
        Console.WriteLine("PASS: Game Bar bundle version, " + signing + ", install states and failure messages.");
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

    // Packs a minimal package with the SDK's makeappx when present (developer machines and CI),
    // then signs it the way the installer does and checks the signature names the certificate.
    private static string CheckSigning()
    {
        string? makeappx = FindMakeAppx();
        if (makeappx == null)
        {
            return "local signing skipped (no Windows SDK makeappx)";
        }

        string root = Path.Combine(Path.GetTempPath(), "league-msix-sign-" + Guid.NewGuid().ToString("N"));
        string content = Path.Combine(root, "content"),
            package = Path.Combine(root, "test.msix");
        Directory.CreateDirectory(content);
        try
        {
            WriteManifest(Path.Combine(content, "AppxManifest.xml"));
            // makeappx validates the manifest structure even with /nv; the logo must be an image.
            using (var logo = new Bitmap(4, 4))
            {
                logo.Save(Path.Combine(content, "logo.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            using (
                var pack = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(makeappx, $"pack /nv /o /d \"{content}\" /p \"{package}\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                    }
                )!
            )
            {
                pack.StandardOutput.ReadToEnd();
                pack.WaitForExit();
                Check(pack.ExitCode == 0, "makeappx packs the signing fixture");
            }

            using var certificate = CreateCertificate();
            Check(
                !LocalSigningCertificate.IsTrusted(certificate),
                "A throwaway certificate is not reported as trusted"
            );
            MsixSigner.Sign(package, certificate);
            using var archive = ZipFile.OpenRead(package);
            var entry = archive.GetEntry("AppxSignature.p7x");
            Check(entry != null, "Signing adds the package signature");
            using var stream = new MemoryStream();
            entry!.Open().CopyTo(stream);
            // AppxSignature.p7x is a 4-byte "PKCX" marker followed by PKCS #7 signed data.
            var cms = new System.Security.Cryptography.Pkcs.SignedCms();
            cms.Decode(stream.ToArray()[4..]);
            Check(
                cms.SignerInfos[0].Certificate?.Thumbprint == certificate.Thumbprint,
                "The signature is made by the local certificate"
            );
            return "local MSIX signing";
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string? FindMakeAppx()
    {
        string kits = @"C:\Program Files (x86)\Windows Kits\10\bin";
        return Directory.Exists(kits)
            ? Directory
                .EnumerateDirectories(kits, "10.*")
                .OrderByDescending(path => path, StringComparer.Ordinal)
                .Select(path => Path.Combine(path, "x64", "makeappx.exe"))
                .FirstOrDefault(File.Exists)
            : null;
    }

    private static void WriteManifest(string path) =>
        File.WriteAllText(
            path,
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><Package xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\">"
                + "<Identity Name=\"LeagueHistory.SigningCheck\" Publisher=\"CN=LeagueHistory\" Version=\"1.0.0.0\" ProcessorArchitecture=\"x64\" />"
                + "<Properties><DisplayName>Signing check</DisplayName><PublisherDisplayName>LeagueHistory</PublisherDisplayName><Logo>logo.png</Logo></Properties>"
                + "<Dependencies><TargetDeviceFamily Name=\"Windows.Desktop\" MinVersion=\"10.0.19041.0\" MaxVersionTested=\"10.0.26100.0\" /></Dependencies>"
                + "<Resources><Resource Language=\"zh-CN\" /></Resources></Package>"
        );

    // Same subject, usage and constraints as the certificate the installer creates.
    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=LeagueHistory",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") }, false)
        );
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
    }
}
