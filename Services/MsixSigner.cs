using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace League.Services;

/// <summary>
/// Signs an MSIX with Windows' own signing API (mssign32.dll), so no SDK tools are needed on the
/// user's computer. Follows Microsoft's "sign an app package using SignerSignEx2" sample.
/// </summary>
public static class MsixSigner
{
    private const uint SubjectFile = 1;
    private const uint CertStore = 2;
    private const uint PolicyChainNoRoot = 8;
    private const uint NoAttributes = 0;

    // Must match the hash in the package block map; makeappx uses SHA-256.
    private const uint Sha256 = 0x0000800c;

    public static void Sign(string packagePath, X509Certificate2 certificate)
    {
        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException("签名证书缺少私钥。");
        }

        using var memory = new NativeMemory();
        var (subject, signer, signature, sip) = Describe(memory, packagePath, certificate);
        int result = SignerSignEx2(0, subject, signer, signature, 0, 0, 0, 0, 0, sip, 0, 0, 0);
        IntPtr state = Marshal.PtrToStructure<AppxSipClientData>(sip).SipState;
        if (state != IntPtr.Zero)
        {
            Marshal.Release(state);
        }

        Marshal.ThrowExceptionForHR(result);
    }

    private static (IntPtr Subject, IntPtr Signer, IntPtr Signature, IntPtr Sip) Describe(
        NativeMemory memory,
        string packagePath,
        X509Certificate2 certificate
    )
    {
        IntPtr file = memory.Allocate(new FileInfo { Size = Size<FileInfo>(), FileName = memory.String(packagePath) });
        IntPtr subject = memory.Allocate(
            new SubjectInfo
            {
                Size = Size<SubjectInfo>(),
                Index = memory.Allocate(0),
                Choice = SubjectFile,
                File = file,
            }
        );
        IntPtr store = memory.Allocate(
            new CertStoreInfo
            {
                Size = Size<CertStoreInfo>(),
                SigningCertificate = certificate.Handle,
                Policy = PolicyChainNoRoot,
            }
        );
        IntPtr signer = memory.Allocate(
            new SignerCertificate
            {
                Size = Size<SignerCertificate>(),
                Choice = CertStore,
                Store = store,
            }
        );
        IntPtr signature = memory.Allocate(
            new SignatureInfo
            {
                Size = Size<SignatureInfo>(),
                HashAlgorithm = Sha256,
                AttributeChoice = NoAttributes,
            }
        );
        // The package SIP reads the same parameters back through its client data, so the two
        // blocks point at each other and must stay at fixed addresses during the call.
        IntPtr sip = memory.Allocate(new AppxSipClientData());
        IntPtr parameters = memory.Allocate(
            new SignExParameters
            {
                Subject = subject,
                Certificate = signer,
                Signature = signature,
                SipData = sip,
            }
        );
        Marshal.StructureToPtr(new AppxSipClientData { Parameters = parameters }, sip, false);
        return (subject, signer, signature, sip);
    }

    /// <summary>Unmanaged blocks that live exactly as long as one signing call.</summary>
    private sealed class NativeMemory : IDisposable
    {
        private readonly List<IntPtr> _blocks = new();

        public IntPtr Allocate<T>(T value)
            where T : struct
        {
            IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
            _blocks.Add(pointer);
            Marshal.StructureToPtr(value, pointer, false);
            return pointer;
        }

        public IntPtr String(string value)
        {
            IntPtr pointer = Marshal.StringToHGlobalUni(value);
            _blocks.Add(pointer);
            return pointer;
        }

        public void Dispose()
        {
            foreach (IntPtr pointer in _blocks)
            {
                Marshal.FreeHGlobal(pointer);
            }

            _blocks.Clear();
        }
    }

    private static uint Size<T>() => (uint)Marshal.SizeOf<T>();

    [DllImport("mssign32.dll", ExactSpelling = true)]
    private static extern int SignerSignEx2(
        uint flags,
        IntPtr subject,
        IntPtr certificate,
        IntPtr signature,
        nint provider,
        uint timestampFlags,
        nint timestampAlgorithm,
        nint timestampUrl,
        nint request,
        IntPtr sipData,
        nint signerContext,
        nint cryptoPolicy,
        nint reserved
    );

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public uint Size;
        public IntPtr FileName;
        public IntPtr Handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SubjectInfo
    {
        public uint Size;
        public IntPtr Index;
        public uint Choice;
        public IntPtr File;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CertStoreInfo
    {
        public uint Size;
        public IntPtr SigningCertificate;
        public uint Policy;
        public IntPtr Store;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SignerCertificate
    {
        public uint Size;
        public uint Choice;
        public IntPtr Store;
        public IntPtr Window;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SignatureInfo
    {
        public uint Size;
        public uint HashAlgorithm;
        public uint AttributeChoice;
        public IntPtr Authenticode;
        public IntPtr Authenticated;
        public IntPtr Unauthenticated;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SignExParameters
    {
        public uint Flags;
        public IntPtr Subject;
        public IntPtr Certificate;
        public IntPtr Signature;
        public IntPtr Provider;
        public uint TimestampFlags;
        public IntPtr TimestampAlgorithm;
        public IntPtr TimestampUrl;
        public IntPtr Attributes;
        public IntPtr SipData;
        public IntPtr SignerContext;
        public IntPtr CryptoPolicy;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppxSipClientData
    {
        public IntPtr Parameters;
        public IntPtr SipState;
    }
}
