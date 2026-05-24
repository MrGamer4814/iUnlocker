using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace IUnlocker;

internal static class FileSignatureVerifier
{
    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
    private static readonly ConcurrentDictionary<string, SignatureInfo> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static SignatureInfo Verify(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return SignatureInfo.Empty;
        }

        try
        {
            var file = new FileInfo(filePath);
            var key = $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
            return Cache.GetOrAdd(key, _ => VerifyCore(file.FullName));
        }
        catch
        {
            return new SignatureInfo("Ошибка проверки", string.Empty, false);
        }
    }

    private static SignatureInfo VerifyCore(string filePath)
    {
        var publisher = ReadPublisher(filePath);
        var status = WinVerifyFile(filePath);

        return status switch
        {
            0 => new SignatureInfo("Действительна", publisher, true),
            WinTrustErrors.TrustENoSignature => new SignatureInfo("Нет подписи", string.Empty, false),
            WinTrustErrors.CertEExpired => new SignatureInfo("Сертификат истёк", publisher, false),
            WinTrustErrors.TrustEBadDigest => new SignatureInfo("Подпись повреждена", publisher, false),
            WinTrustErrors.CertEUntrustedRoot => new SignatureInfo("Недоверенная", publisher, false),
            WinTrustErrors.TrustEExplicitDistrust => new SignatureInfo("Запрещена", publisher, false),
            _ => new SignatureInfo($"Ошибка 0x{status:X8}", publisher, false),
        };
    }

    private static string ReadPublisher(string filePath)
    {
        try
        {
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
            return certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static int WinVerifyFile(string filePath)
    {
        var fileInfo = new WintrustFileInfo(filePath);
        var data = new WintrustData(fileInfo);
        IntPtr fileInfoPtr = IntPtr.Zero;
        IntPtr dataPtr = IntPtr.Zero;

        try
        {
            fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WintrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, fDeleteOld: false);
            data.File = fileInfoPtr;

            dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WintrustData>());
            Marshal.StructureToPtr(data, dataPtr, fDeleteOld: false);

            var result = WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, dataPtr);

            data.StateAction = WintrustDataStateAction.Close;
            Marshal.StructureToPtr(data, dataPtr, fDeleteOld: true);
            WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, dataPtr);

            return result;
        }
        catch (Win32Exception ex)
        {
            return ex.NativeErrorCode;
        }
        finally
        {
            if (dataPtr != IntPtr.Zero)
            {
                Marshal.DestroyStructure<WintrustData>(dataPtr);
                Marshal.FreeHGlobal(dataPtr);
            }

            if (fileInfoPtr != IntPtr.Zero)
            {
                Marshal.DestroyStructure<WintrustFileInfo>(fileInfoPtr);
                Marshal.FreeHGlobal(fileInfoPtr);
            }
        }
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern int WinVerifyTrust(
        IntPtr windowHandle,
        [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        IntPtr data);

    private static class WinTrustErrors
    {
        public const int TrustENoSignature = unchecked((int)0x800B0100);
        public const int CertEExpired = unchecked((int)0x800B0101);
        public const int CertEUntrustedRoot = unchecked((int)0x800B0109);
        public const int TrustEExplicitDistrust = unchecked((int)0x800B0111);
        public const int TrustEBadDigest = unchecked((int)0x80096010);
    }

    private enum WintrustDataChoice : uint
    {
        File = 1,
    }

    private enum WintrustDataRevocationChecks : uint
    {
        None = 0,
    }

    private enum WintrustDataStateAction : uint
    {
        Ignore = 0,
        Verify = 1,
        Close = 2,
    }

    private enum WintrustDataUiChoice : uint
    {
        None = 2,
    }

    private enum WintrustDataUnionChoice : uint
    {
        File = 1,
    }

    [Flags]
    private enum WintrustDataProvFlags : uint
    {
        RevocationCheckNone = 0x00000010,
        Safer = 0x00000100,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WintrustFileInfo
    {
        public uint StructSize = (uint)Marshal.SizeOf<WintrustFileInfo>();
        public string FilePath;
        public IntPtr FileHandle = IntPtr.Zero;
        public IntPtr KnownSubject = IntPtr.Zero;

        public WintrustFileInfo(string filePath)
        {
            FilePath = filePath;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WintrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public WintrustDataUiChoice UiChoice;
        public WintrustDataRevocationChecks RevocationChecks;
        public WintrustDataUnionChoice UnionChoice;
        public IntPtr File;
        public WintrustDataStateAction StateAction;
        public IntPtr StateData;
        public string? UrlReference;
        public WintrustDataProvFlags ProvFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;

        public WintrustData(WintrustFileInfo fileInfo)
        {
            StructSize = (uint)Marshal.SizeOf<WintrustData>();
            PolicyCallbackData = IntPtr.Zero;
            SipClientData = IntPtr.Zero;
            UiChoice = WintrustDataUiChoice.None;
            RevocationChecks = WintrustDataRevocationChecks.None;
            UnionChoice = WintrustDataUnionChoice.File;
            File = IntPtr.Zero;
            StateAction = WintrustDataStateAction.Verify;
            StateData = IntPtr.Zero;
            UrlReference = null;
            ProvFlags = WintrustDataProvFlags.RevocationCheckNone | WintrustDataProvFlags.Safer;
            UiContext = 0;
            SignatureSettings = IntPtr.Zero;
        }
    }
}

internal sealed record SignatureInfo(string Status, string Publisher, bool IsValid)
{
    public static SignatureInfo Empty { get; } = new(string.Empty, string.Empty, false);
}
