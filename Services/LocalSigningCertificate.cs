using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace League.Services;

/// <summary>
/// The certificate this computer uses to sign the Game Bar package before installing it. It is
/// created in the current user's store with a non-exportable key, so no private key is shared
/// between users or kept by the project; its subject must equal the package Publisher.
/// </summary>
public static class LocalSigningCertificate
{
    public const string FriendlyName = "LeagueHistory Game Bar 本机签名";
    private const string Subject = "CN=LeagueHistory";
    private const string CodeSigning = "1.3.6.1.5.5.7.3.3";
    private const int UserCancelled = 1223;

    public const string CreateScript =
        "New-SelfSignedCertificate -Type Custom -Subject '"
        + Subject
        + "' -FriendlyName '"
        + FriendlyName
        + "' -KeyUsage DigitalSignature -KeyExportPolicy NonExportable -CertStoreLocation Cert:\\CurrentUser\\My"
        + " -NotAfter (Get-Date).AddYears(10) -TextExtension @('2.5.29.37={text}"
        + CodeSigning
        + "', '2.5.29.19={text}') | Out-Null";

    /// <summary>The newest usable local certificate, or null when none exists yet.</summary>
    public static X509Certificate2? Find()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        X509Certificate2? best = null;
        foreach (var certificate in store.Certificates)
        {
            bool usable =
                certificate.FriendlyName == FriendlyName
                && certificate.Subject == Subject
                && certificate.HasPrivateKey
                && certificate.NotAfter > DateTime.Now.AddDays(1)
                && (best == null || certificate.NotAfter > best.NotAfter);
            if (usable)
            {
                best?.Dispose();
                best = certificate;
            }
            else
            {
                certificate.Dispose();
            }
        }

        return best;
    }

    public static bool IsTrusted(X509Certificate2 certificate)
    {
        foreach (var name in new[] { StoreName.TrustedPeople, StoreName.Root })
        {
            using var store = new X509Store(name, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            if (store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false).Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Adds the public certificate to the machine's Trusted People store; one administrator prompt.</summary>
    public static async Task TrustAsync(X509Certificate2 certificate, CancellationToken token)
    {
        string path = Path.Combine(Path.GetTempPath(), $"LeagueHistory-GameBar-{Guid.NewGuid():N}.cer");
        await File.WriteAllBytesAsync(path, certificate.Export(X509ContentType.Cert), token).ConfigureAwait(false);
        try
        {
            var start = new ProcessStartInfo("certutil.exe", $"-addstore TrustedPeople \"{path}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            Process? process;
            try
            {
                process = Process.Start(start);
            }
            catch (Win32Exception e) when (e.NativeErrorCode == UserCancelled)
            {
                throw new InvalidOperationException("已取消管理员授权，组件未安装。", e);
            }

            using (process)
            {
                if (process == null)
                {
                    throw new InvalidOperationException("无法启动证书安装。");
                }

                await process.WaitForExitAsync(token).ConfigureAwait(false);
                if (process.ExitCode != 0 || !IsTrusted(certificate))
                {
                    throw new InvalidOperationException("组件签名证书安装失败，组件未安装。");
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
