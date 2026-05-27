using System.Diagnostics;
using Microsoft.Win32;

namespace IUnlocker;

public static class OfflineRegistryEditor
{
    public static void SetValue(
        string hiveFile,
        string mountPrefix,
        string keyPath,
        string valueName,
        object value,
        RegistryValueKind valueKind)
    {
        using var hive = OfflineRegistryHiveMount.Load(hiveFile, mountPrefix);
        using var key = hive.Root.OpenSubKey(keyPath, writable: true)
            ?? throw new InvalidOperationException($"Ключ offline-реестра не найден: {keyPath}");

        key.SetValue(valueName, value, valueKind);
    }

    public static void DeleteValue(
        string hiveFile,
        string mountPrefix,
        string keyPath,
        string valueName)
    {
        using var hive = OfflineRegistryHiveMount.Load(hiveFile, mountPrefix);
        using var key = hive.Root.OpenSubKey(keyPath, writable: true)
            ?? throw new InvalidOperationException($"Ключ offline-реестра не найден: {keyPath}");

        key.DeleteValue(valueName, throwOnMissingValue: true);
    }

    public static void DeleteKey(
        string hiveFile,
        string mountPrefix,
        string keyPath)
    {
        using var hive = OfflineRegistryHiveMount.Load(hiveFile, mountPrefix);
        var normalizedPath = keyPath.Trim('\\');
        var separator = normalizedPath.LastIndexOf('\\');
        if (separator <= 0 || separator >= normalizedPath.Length - 1)
        {
            throw new InvalidOperationException($"Нельзя удалить корневой offline-ключ: {keyPath}");
        }

        var parentPath = normalizedPath[..separator];
        var subKeyName = normalizedPath[(separator + 1)..];
        using var parent = hive.Root.OpenSubKey(parentPath, writable: true)
            ?? throw new InvalidOperationException($"Родительский offline-ключ не найден: {parentPath}");

        parent.DeleteSubKeyTree(subKeyName, throwOnMissingSubKey: true);
    }
}

public sealed class OfflineRegistryHiveMount : IDisposable
{
    private readonly string _mountName;

    private OfflineRegistryHiveMount(string mountName)
    {
        _mountName = mountName;
        Root = Registry.LocalMachine.OpenSubKey(mountName, writable: true)
            ?? throw new InvalidOperationException($"Не удалось открыть HKLM\\{mountName}.");
        DisplayName = $@"HKLM\{mountName}";
    }

    public RegistryKey Root { get; }

    public string DisplayName { get; }

    public string HiveFile { get; private init; } = string.Empty;

    public string MountName => _mountName;

    public static OfflineRegistryHiveMount Load(string hiveFile, string mountPrefix)
    {
        if (!File.Exists(hiveFile))
        {
            throw new FileNotFoundException("Hive-файл не найден.", hiveFile);
        }

        var mountName = $"{mountPrefix}_{Environment.ProcessId}_{Environment.TickCount64}";
        RunReg("unload", $@"HKLM\{mountName}", ignoreErrors: true);

        var result = RunReg("load", $@"HKLM\{mountName} ""{hiveFile}""", ignoreErrors: false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Не удалось загрузить hive {hiveFile}. Код reg.exe: {result.ExitCode}.");
        }

        try
        {
            return new OfflineRegistryHiveMount(mountName)
            {
                HiveFile = hiveFile,
            };
        }
        catch
        {
            RunReg("unload", $@"HKLM\{mountName}", ignoreErrors: true);
            throw;
        }
    }

    public static OfflineRegistryHiveMount? TryLoad(string hiveFile, string mountPrefix, List<string> warnings)
    {
        try
        {
            return Load(hiveFile, mountPrefix);
        }
        catch (Exception ex)
        {
            warnings.Add(ex.Message);
            return null;
        }
    }

    public void Dispose()
    {
        Root.Dispose();
        RunReg("unload", $@"HKLM\{_mountName}", ignoreErrors: true);
    }

    private static CommandResult RunReg(string command, string arguments, bool ignoreErrors)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"{command} {arguments}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process is null)
            {
                return new CommandResult(-1, string.Empty, "Не удалось запустить reg.exe.");
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new CommandResult(process.ExitCode, output, error);
        }
        catch (Exception ex) when (ignoreErrors)
        {
            return new CommandResult(-1, string.Empty, ex.Message);
        }
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);
}
