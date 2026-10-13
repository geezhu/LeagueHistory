using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;

namespace League.Services;

public enum GameBarPackageState
{
    NotBundled,
    NotInstalled,
    Outdated,
    Current,
}

public sealed record GameBarPackageStatus(Version? Bundled, Version? Installed)
{
    public GameBarPackageState State =>
        Bundled == null ? GameBarPackageState.NotBundled
        : Installed == null ? GameBarPackageState.NotInstalled
        : Installed < Bundled ? GameBarPackageState.Outdated
        : GameBarPackageState.Current;

    public string Text =>
        State switch
        {
            GameBarPackageState.NotBundled => Installed != null
                ? $"已安装 {Installed}；本程序未附带组件安装包。"
                : "本程序未附带组件安装包，请使用完整发布包。",
            GameBarPackageState.NotInstalled => $"未安装 · 可安装附带的 {Bundled}",
            GameBarPackageState.Outdated => $"已安装 {Installed} · 可更新到 {Bundled}",
            _ => $"已安装 {Installed}",
        };
}

/// <summary>
/// Installs the unsigned Game Bar MSIX shipped beside League.exe. A copy is signed on this computer
/// with its own certificate (see LocalSigningCertificate), which Windows trusts after one
/// administrator prompt; the package itself installs for the current user.
/// </summary>
public sealed class GameBarPackageInstaller
{
    public const string PackageName = "LeagueHistory.GameBar";
    private readonly string _directory;

    public GameBarPackageInstaller(string? directory = null) =>
        _directory = directory ?? Path.Combine(AppContext.BaseDirectory, "GameBar");

    public string PackagePath => Path.Combine(_directory, PackageName + ".msix");

    public async Task<GameBarPackageStatus> ReadStatusAsync(CancellationToken token)
    {
        var bundled = await Task.Run(ReadBundledVersion, token).ConfigureAwait(false);
        var result = await RunPowerShellAsync($"(Get-AppxPackage -Name {PackageName}).Version", token)
            .ConfigureAwait(false);
        var installed = Version.TryParse(result.Output.Trim(), out var version) ? version : null;
        return new(bundled, installed);
    }

    public async Task InstallAsync(CancellationToken token)
    {
        if (!File.Exists(PackagePath))
        {
            throw new InvalidOperationException("找不到附带的 Game Bar 组件安装包，请使用完整发布包。");
        }

        using var certificate = await EnsureCertificateAsync(token).ConfigureAwait(false);
        if (!LocalSigningCertificate.IsTrusted(certificate))
        {
            await LocalSigningCertificate.TrustAsync(certificate, token).ConfigureAwait(false);
        }

        // Sign a temporary copy; the bundled package stays unsigned so every computer can sign its own.
        string signed = Path.Combine(Path.GetTempPath(), $"{PackageName}-{Guid.NewGuid():N}.msix");
        try
        {
            File.Copy(PackagePath, signed);
            await Task.Run(() => MsixSigner.Sign(signed, certificate), token).ConfigureAwait(false);
            var result = await RunPowerShellAsync(
                    $"Add-AppxPackage -Path '{Quote(signed)}' -ForceApplicationShutdown -ErrorAction Stop",
                    token
                )
                .ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(DescribeFailure(result.Error));
            }
        }
        finally
        {
            File.Delete(signed);
        }
    }

    private static async Task<X509Certificate2> EnsureCertificateAsync(CancellationToken token)
    {
        var certificate = LocalSigningCertificate.Find();
        if (certificate != null)
        {
            return certificate;
        }

        var result = await RunPowerShellAsync(LocalSigningCertificate.CreateScript, token).ConfigureAwait(false);
        return LocalSigningCertificate.Find()
            ?? throw new InvalidOperationException(
                "无法创建本机签名证书：" + (result.Error.Trim().Length > 0 ? result.Error.Trim() : "未知错误")
            );
    }

    public Version? ReadBundledVersion()
    {
        try
        {
            using var archive = ZipFile.OpenRead(PackagePath);
            var entry = archive.GetEntry("AppxManifest.xml");
            if (entry == null)
            {
                return null;
            }

            using var stream = entry.Open();
            var identity = XDocument.Load(stream).Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Identity");
            return Version.TryParse((string?)identity?.Attribute("Version"), out var version) ? version : null;
        }
        catch (Exception e)
            when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            // A missing or damaged bundle shows as "not bundled"; installing reports the real error.
            return null;
        }
    }

    /// <summary>Known deployment errors in plain words; anything else keeps the first line Windows gave.</summary>
    public static string DescribeFailure(string error)
    {
        string code =
            new[] { "0x80073CF3", "0x800B0109", "0x800B010A", "0x80073CFB", "0x80073D06" }.FirstOrDefault(c =>
                error.Contains(c, StringComparison.OrdinalIgnoreCase)
            ) ?? "";
        return code switch
        {
            "0x80073CF3" => "缺少 Game Bar 运行库（Microsoft.VCLibs）。请先在微软商店更新 Xbox Game Bar，再重试。",
            "0x800B0109" or "0x800B010A" => "本机签名证书未被信任，请重新安装并在管理员授权中选择“是”。",
            "0x80073CFB" => "已安装同版本的开发版组件。请先在“设置 → 应用”中卸载 League 对局速览，再重试。",
            "0x80073D06" => "已安装更新版本的组件，无需重新安装。",
            _ => "组件安装失败："
                + (error.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? "未知错误"),
        };
    }

    private static string Quote(string path) => path.Replace("'", "''");

    private static async Task<(int ExitCode, string Output, string Error)> RunPowerShellAsync(
        string script,
        CancellationToken token
    )
    {
        // An encoded command avoids quoting rules for paths with spaces or non-ASCII characters;
        // Windows PowerShell writes the console code page unless told to use UTF-8.
        string encoded = Convert.ToBase64String(
            Encoding.Unicode.GetBytes(
                "[Console]::OutputEncoding = [Text.Encoding]::UTF8; $ProgressPreference = 'SilentlyContinue'; " + script
            )
        );
        var start = new ProcessStartInfo(
            "powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded
        )
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 PowerShell。");
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        // Cancelling stops waiting only; a running deployment finishes or rolls back on its own.
        await process.WaitForExitAsync(token).ConfigureAwait(false);

        return (process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }
}
