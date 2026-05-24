using Microsoft.Win32;

namespace IUnlocker;

public sealed record AppSession(string DriveRoot, string? WindowsPath, bool IsWinPe)
{
    public string EnvironmentName => IsWinPe ? "WinPE" : "Windows";

    public static bool DetectWinPe()
    {
        try
        {
            using var miniNt = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\MiniNT");
            if (miniNt is not null)
            {
                return true;
            }
        }
        catch
        {
            // If the registry probe fails, fall back to the common WinPE drive convention.
        }

        return Environment.GetEnvironmentVariable("SystemDrive")?.Equals("X:", StringComparison.OrdinalIgnoreCase) == true;
    }
}

public sealed record DiskCandidate(
    string Root,
    string DisplayName,
    string? WindowsPath,
    string WindowsStatus,
    string? WindowsVersion,
    string? WindowsError,
    bool IsReady,
    long? TotalSize,
    long? FreeSpace);
