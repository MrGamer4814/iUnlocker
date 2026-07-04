using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Win32;

namespace IUnlocker;

public static class OfflineStartupScanner
{
    private const string CurrentVersion = @"Microsoft\Windows\CurrentVersion";
    private const string CurrentVersionNt = @"Microsoft\Windows NT\CurrentVersion";

    public static StartupScanResult Scan(AppSession session)
    {
        var entries = new List<StartupEntry>();
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scheduledTaskFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (session.WindowsPath is null)
        {
            warnings.Add("На выбранном диске не найдена папка Windows.");
            return new StartupScanResult(entries, warnings);
        }

        ScanOfflineSoftwareHive(session, entries, warnings, seen);
        ScanOfflineSystemHive(session, entries, warnings, seen);
        ScanOfflineUserHives(session, entries, warnings, seen);
        AddOfflineStartupFolders(session, entries, warnings, seen);
        AddOfflineScheduledTasks(session, entries, warnings, seen, scheduledTaskFolders);

        return new StartupScanResult(
            entries
                .OrderBy(entry => entry.Category, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(entry => entry.Source, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            warnings)
        {
            ScheduledTaskFolders = scheduledTaskFolders
                .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    private static void ScanOfflineSoftwareHive(
        AppSession session,
        List<StartupEntry> entries,
        List<string> warnings,
        HashSet<string> seen)
    {
        var hiveFile = Path.Combine(session.WindowsPath!, "System32", "config", "SOFTWARE");
        using var hive = OfflineRegistryHive.TryLoad(hiveFile, "IUnlocker_SOFTWARE", warnings);
        if (hive is null)
        {
            return;
        }

        var runKeys = new[]
        {
            "Run",
            "RunOnce",
            "RunOnceEx",
            "RunServices",
            "RunServicesOnce",
            @"Policies\Explorer\Run",
        };

        foreach (var runKey in runKeys)
        {
            ReadValues(hive, $@"{CurrentVersion}\{runKey}", "Run", "Реестр offline", runKey, "Все пользователи", entries, seen);
        }

        ReadSubKeyValue(hive, $@"{CurrentVersion}\RunOnceEx", "Run", "Реестр offline", "RunOnceEx", "Depend", "Все пользователи", entries, seen);
        ReadSubKeyValue(hive, @"Microsoft\Active Setup\Installed Components", "Active Setup", "Реестр offline", "Active Setup StubPath", "StubPath", "Все пользователи", entries, seen);

        ReadSelectedValues(
            hive,
            $@"{CurrentVersionNt}\Winlogon",
            "Winlogon",
            "Реестр offline",
            "Winlogon",
            ["Shell", "Userinit", "Taskman", "AppSetup", "VmApplet", "GinaDLL", "System"],
            "Все пользователи",
            entries,
            seen);
        ReadSubKeyValue(hive, $@"{CurrentVersionNt}\Winlogon\Notify", "Winlogon", "Реестр offline", "Winlogon Notify", "DLLName", "Все пользователи", entries, seen);

        ReadSelectedValues(hive, @"Microsoft\Command Processor", "CMDLINE", "Реестр offline", "Command Processor AutoRun", ["AutoRun"], "Все пользователи", entries, seen);
        ReadSelectedValues(hive, $@"{CurrentVersionNt}\Windows", "AppInit_DLLs", "Реестр offline", "AppInit DLLs", ["AppInit_DLLs", "LoadAppInit_DLLs", "RequireSignedAppInit_DLLs"], "Все пользователи", entries, seen, includeEmpty: true);
        ReadSubKeyValue(hive, $@"{CurrentVersionNt}\Image File Execution Options", "IFEO", "Реестр offline", "Image File Execution Options", "Debugger", "Все пользователи", entries, seen);
        ReadSubKeyValue(hive, $@"{CurrentVersionNt}\SilentProcessExit", "IFEO", "Реестр offline", "Silent Process Exit", "MonitorProcess", "Все пользователи", entries, seen);
        ReadExplorerKeys(hive, entries, seen);
    }

    private static void ScanOfflineSystemHive(
        AppSession session,
        List<StartupEntry> entries,
        List<string> warnings,
        HashSet<string> seen)
    {
        var hiveFile = Path.Combine(session.WindowsPath!, "System32", "config", "SYSTEM");
        using var hive = OfflineRegistryHive.TryLoad(hiveFile, "IUnlocker_SYSTEM", warnings);
        if (hive is null)
        {
            return;
        }

        var controlSet = GetCurrentControlSet(hive);
        ReadSelectedValues(
            hive,
            "Setup",
            "CMDLINE",
            "Реестр offline",
            "Setup CmdLine",
            ["CmdLine", "SetupType"],
            "Система",
            entries,
            seen,
            includeEmpty: true);

        ReadSelectedValues(
            hive,
            $@"{controlSet}\Control\Session Manager",
            "BootExecute",
            "Реестр offline",
            "Session Manager",
            ["BootExecute", "SetupExecute", "Execute", "S0InitialCommand"],
            "Система",
            entries,
            seen);

        ReadSelectedValues(
            hive,
            $@"{controlSet}\Control\Lsa",
            "LSA",
            "Реестр offline",
            "LSA providers",
            ["Authentication Packages", "Notification Packages", "Security Packages", "OSConfig"],
            "Система",
            entries,
            seen);

        ReadSubKeyValue(hive, $@"{controlSet}\Control\Print\Monitors", "Print Monitor", "Реестр offline", "Print monitors", "Driver", "Система", entries, seen);
        ReadServices(hive, controlSet, entries, seen);
    }

    private static void ScanOfflineUserHives(
        AppSession session,
        List<StartupEntry> entries,
        List<string> warnings,
        HashSet<string> seen)
    {
        var usersRoot = Path.Combine(session.DriveRoot, "Users");
        if (!Directory.Exists(usersRoot))
        {
            return;
        }

        foreach (var profileDirectory in Directory.EnumerateDirectories(usersRoot))
        {
            var userName = Path.GetFileName(profileDirectory);
            if (userName.Equals("All Users", StringComparison.OrdinalIgnoreCase) ||
                userName.Equals("Default User", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var hiveFile = Path.Combine(profileDirectory, "NTUSER.DAT");
            if (!File.Exists(hiveFile))
            {
                continue;
            }

            using var hive = OfflineRegistryHive.TryLoad(hiveFile, $"IUnlocker_USER_{SanitizeHiveName(userName)}", warnings);
            if (hive is null)
            {
                continue;
            }

            foreach (var runKey in new[] { "Run", "RunOnce", @"Policies\Explorer\Run" })
            {
                ReadValues(hive, $@"Software\{CurrentVersion}\{runKey}", "Run", "Реестр offline", runKey, userName, entries, seen);
            }

            ReadSelectedValues(hive, @"Software\Microsoft\Command Processor", "CMDLINE", "Реестр offline", "Command Processor AutoRun", ["AutoRun"], userName, entries, seen);
        }
    }

    private static void AddOfflineStartupFolders(
        AppSession session,
        List<StartupEntry> entries,
        List<string> warnings,
        HashSet<string> seen)
    {
        var folders = new List<(string Path, string Scope)>
        {
            (Path.Combine(session.DriveRoot, "ProgramData", "Microsoft", "Windows", "Start Menu", "Programs", "Startup"), "Все пользователи"),
        };

        var usersRoot = Path.Combine(session.DriveRoot, "Users");
        if (Directory.Exists(usersRoot))
        {
            folders.AddRange(Directory.EnumerateDirectories(usersRoot)
                .Select(profile => (
                    Path.Combine(profile, "AppData", "Roaming", "Microsoft", "Windows", "Start Menu", "Programs", "Startup"),
                    Path.GetFileName(profile))));
        }

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder.Path))
            {
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(folder.Path))
                {
                    AddEntry(
                        entries,
                        seen,
                        new StartupEntry(
                            "Startup Folder",
                            Path.GetFileNameWithoutExtension(file),
                            "Файл",
                            folder.Scope,
                            "Startup folder offline",
                            file,
                            file));
                }
            }
            catch (Exception ex) when (IsAccessException(ex))
            {
                warnings.Add($"Не удалось прочитать offline Startup folder {folder.Path}: {ex.Message}");
            }
        }
    }

    private static void AddOfflineScheduledTasks(
        AppSession session,
        List<StartupEntry> entries,
        List<string> warnings,
        HashSet<string> seen,
        HashSet<string> folders)
    {
        var tasksRoot = Path.Combine(session.WindowsPath!, "System32", "Tasks");
        if (!Directory.Exists(tasksRoot))
        {
            return;
        }

        folders.Add("\\");
        foreach (var directory in Directory.EnumerateDirectories(tasksRoot, "*", SearchOption.AllDirectories))
        {
            var relativeDirectory = Path.GetRelativePath(tasksRoot, directory).Replace('/', '\\').Trim('\\');
            if (!string.IsNullOrWhiteSpace(relativeDirectory))
            {
                folders.Add($@"\{relativeDirectory}");
            }
        }

        foreach (var file in Directory.EnumerateFiles(tasksRoot, "*", SearchOption.AllDirectories))
        {
            try
            {
                var document = XDocument.Load(file);
                var commands = document.Descendants()
                    .Where(node => node.Name.LocalName == "Exec")
                    .Select(exec =>
                    {
                        var command = exec.Elements().FirstOrDefault(node => node.Name.LocalName == "Command")?.Value ?? string.Empty;
                        var arguments = exec.Elements().FirstOrDefault(node => node.Name.LocalName == "Arguments")?.Value ?? string.Empty;
                        return string.IsNullOrWhiteSpace(arguments) ? command : $"{command} {arguments}";
                    })
                    .Where(command => !string.IsNullOrWhiteSpace(command))
                    .ToList();
                var triggers = document.Descendants()
                    .Where(node => node.Parent?.Name.LocalName == "Triggers")
                    .Select(DescribeOfflineTrigger)
                    .Where(trigger => !string.IsNullOrWhiteSpace(trigger))
                    .ToList();
                var author = document.Descendants().FirstOrDefault(node => node.Name.LocalName == "Author")?.Value ?? string.Empty;
                var hidden = document.Descendants().FirstOrDefault(node => node.Name.LocalName == "Hidden")?.Value;

                AddEntry(
                    entries,
                    seen,
                    new StartupEntry(
                        "Scheduled Task",
                        Path.GetRelativePath(tasksRoot, file),
                        "Задание",
                        "Offline",
                        "Планировщик offline",
                        commands.Count == 0 ? "(команда не указана)" : string.Join("; ", commands),
                        file,
                        OfflineScheduledTaskFile: file,
                        TaskTriggers: triggers.Count == 0 ? "(нет триггеров)" : string.Join("; ", triggers),
                        TaskAuthor: author,
                        TaskHidden: string.Equals(hidden, "true", StringComparison.OrdinalIgnoreCase) ? "Да" : "Нет"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                warnings.Add($"Не удалось прочитать задачу {file}: {ex.Message}");
            }
        }
    }

    private static string DescribeOfflineTrigger(XElement trigger)
    {
        var name = trigger.Name.LocalName switch
        {
            "TimeTrigger" => "по времени",
            "CalendarTrigger" => "по календарю",
            "BootTrigger" => "при загрузке",
            "LogonTrigger" => "при входе",
            "IdleTrigger" => "при простое",
            "EventTrigger" => "по событию",
            "RegistrationTrigger" => "при регистрации",
            "SessionStateChangeTrigger" => "при смене сеанса",
            _ => trigger.Name.LocalName,
        };
        var start = trigger.Elements().FirstOrDefault(node => node.Name.LocalName == "StartBoundary")?.Value;
        return string.IsNullOrWhiteSpace(start) ? name : $"{name}: {start}";
    }

    private static string GetCurrentControlSet(OfflineRegistryHive hive)
    {
        using var selectKey = hive.Root.OpenSubKey("Select");
        var current = selectKey?.GetValue("Current") is int value ? value : 1;
        return $"ControlSet{current:000}";
    }

    private static void ReadServices(OfflineRegistryHive hive, string controlSet, List<StartupEntry> entries, HashSet<string> seen)
    {
        using var servicesKey = hive.Root.OpenSubKey($@"{controlSet}\Services");
        if (servicesKey is null)
        {
            return;
        }

        foreach (var serviceName in servicesKey.GetSubKeyNames())
        {
            using var serviceKey = servicesKey.OpenSubKey(serviceName);
            var start = TryGetInt(serviceKey?.GetValue("Start"));
            if (serviceKey is null || start is null or > 2)
            {
                continue;
            }

            var imagePath = ValueToString(serviceKey.GetValue("ImagePath"));
            using var parametersKey = serviceKey.OpenSubKey("Parameters");
            var serviceDll = ValueToString(parametersKey?.GetValue("ServiceDll"));
            var command = string.IsNullOrWhiteSpace(serviceDll) ? imagePath : $"{imagePath} | ServiceDll: {serviceDll}";
            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            var type = TryGetInt(serviceKey.GetValue("Type"));
            var entryType = type is 1 or 2 ? "Драйвер" : "Служба";
            var scope = start switch
            {
                0 => "Boot",
                1 => "System",
                2 => "Automatic",
                _ => "Unknown",
            };

            AddEntry(
                entries,
                seen,
                new StartupEntry(
                    entryType == "Драйвер" ? "Drivers" : "Services",
                    serviceName,
                    entryType,
                    scope,
                    "Service Control Manager offline",
                    command,
                    $@"Offline SYSTEM\{controlSet}\Services\{serviceName}",
                    RegistryKeyPath: $@"{controlSet}\Services\{serviceName}",
                    OfflineRegistryHiveFile: hive.HiveFile,
                    OfflineRegistryMountPrefix: hive.MountPrefix,
                    StartType: scope));
        }
    }

    private static void ReadExplorerKeys(OfflineRegistryHive hive, List<StartupEntry> entries, HashSet<string> seen)
    {
        foreach (var path in new[]
        {
            $@"{CurrentVersion}\Explorer\Browser Helper Objects",
            $@"{CurrentVersion}\Explorer\ShellExecuteHooks",
            $@"{CurrentVersion}\Explorer\ShellIconOverlayIdentifiers",
            @"Classes\Directory\Shellex\ContextMenuHandlers",
            @"Classes\Directory\Background\Shellex\ContextMenuHandlers",
            @"Classes\Drive\Shellex\ContextMenuHandlers",
        })
        {
            using var key = hive.Root.OpenSubKey(path);
            if (key is null)
            {
                continue;
            }

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                AddEntry(entries, seen, new StartupEntry("Explorer", subKeyName, "Shell extension", "Offline", "Explorer offline", subKeyName, $@"Offline SOFTWARE\{path}\{subKeyName}"));
            }
        }
    }

    private static void ReadValues(
        OfflineRegistryHive hive,
        string keyPath,
        string category,
        string type,
        string source,
        string scope,
        List<StartupEntry> entries,
        HashSet<string> seen)
    {
        using var key = hive.Root.OpenSubKey(keyPath);
        if (key is null)
        {
            return;
        }

        foreach (var valueName in key.GetValueNames())
        {
            var command = ValueToString(key.GetValue(valueName));
            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            var rawValueName = string.IsNullOrWhiteSpace(valueName) ? string.Empty : valueName;
            AddEntry(
                entries,
                seen,
                new StartupEntry(
                    category,
                    string.IsNullOrWhiteSpace(valueName) ? "(по умолчанию)" : valueName,
                    type,
                    scope,
                    source,
                    command,
                    $@"Offline {hive.DisplayName}\{keyPath}\{valueName}",
                    RegistryKeyPath: keyPath,
                    RegistryValueName: rawValueName,
                    RegistryValueKind: GetValueKind(key, rawValueName),
                    RegistryEditText: ValueToEditableString(key.GetValue(rawValueName)),
                    OfflineRegistryHiveFile: hive.HiveFile,
                    OfflineRegistryMountPrefix: hive.MountPrefix));
        }
    }

    private static void ReadSelectedValues(
        OfflineRegistryHive hive,
        string keyPath,
        string category,
        string type,
        string source,
        IReadOnlyCollection<string> valueNames,
        string scope,
        List<StartupEntry> entries,
        HashSet<string> seen,
        bool includeEmpty = false)
    {
        using var key = hive.Root.OpenSubKey(keyPath);
        if (key is null)
        {
            return;
        }

        foreach (var valueName in valueNames)
        {
            var value = key.GetValue(valueName);
            if (value is null)
            {
                continue;
            }

            var command = ValueToString(value);
            if (!includeEmpty && string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            AddEntry(
                entries,
                seen,
                new StartupEntry(
                    category,
                    valueName,
                    type,
                    scope,
                    source,
                    command,
                    $@"Offline {hive.DisplayName}\{keyPath}\{valueName}",
                    RegistryKeyPath: keyPath,
                    RegistryValueName: valueName,
                    RegistryValueKind: GetValueKind(key, valueName),
                    RegistryEditText: ValueToEditableString(value),
                    OfflineRegistryHiveFile: hive.HiveFile,
                    OfflineRegistryMountPrefix: hive.MountPrefix));
        }
    }

    private static void ReadSubKeyValue(
        OfflineRegistryHive hive,
        string keyPath,
        string category,
        string type,
        string source,
        string valueName,
        string scope,
        List<StartupEntry> entries,
        HashSet<string> seen)
    {
        using var key = hive.Root.OpenSubKey(keyPath);
        if (key is null)
        {
            return;
        }

        foreach (var subKeyName in key.GetSubKeyNames())
        {
            using var subKey = key.OpenSubKey(subKeyName);
            var command = ValueToString(subKey?.GetValue(valueName));
            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            var subKeyPath = $@"{keyPath}\{subKeyName}";
            AddEntry(
                entries,
                seen,
                new StartupEntry(
                    category,
                    subKeyName,
                    type,
                    scope,
                    source,
                    command,
                    $@"Offline {hive.DisplayName}\{subKeyPath}\{valueName}",
                    RegistryKeyPath: subKeyPath,
                    RegistryValueName: valueName,
                    RegistryValueKind: subKey is null ? RegistryValueKind.String : GetValueKind(subKey, valueName),
                    RegistryEditText: ValueToEditableString(subKey?.GetValue(valueName)),
                    OfflineRegistryHiveFile: hive.HiveFile,
                    OfflineRegistryMountPrefix: hive.MountPrefix));
        }
    }

    private static void AddEntry(List<StartupEntry> entries, HashSet<string> seen, StartupEntry entry)
    {
        var identity = $"{entry.Category}|{entry.Name}|{entry.Command}|{entry.Location}";
        if (seen.Add(identity))
        {
            entries.Add(entry);
        }
    }

    private static string ValueToString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            string[] values => string.Join("; ", values),
            byte[] bytes => $"0x{Convert.ToHexString(bytes)}",
            _ => Convert.ToString(value) ?? string.Empty,
        };
    }

    private static string ValueToEditableString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            string[] values => string.Join(Environment.NewLine, values),
            byte[] bytes => Convert.ToHexString(bytes),
            _ => Convert.ToString(value) ?? string.Empty,
        };
    }

    private static RegistryValueKind GetValueKind(RegistryKey key, string valueName)
    {
        try
        {
            return key.GetValueKind(valueName);
        }
        catch
        {
            return RegistryValueKind.String;
        }
    }

    private static int? TryGetInt(object? value)
    {
        return value switch
        {
            int number => number,
            long number => (int)number,
            string text when int.TryParse(text, out var number) => number,
            _ => null,
        };
    }

    private static string SanitizeHiveName(string value)
    {
        return string.Concat(value.Select(character => char.IsLetterOrDigit(character) ? character : '_'));
    }

    private static bool IsAccessException(Exception ex)
    {
        return ex is UnauthorizedAccessException or IOException or System.Security.SecurityException;
    }

    private sealed class OfflineRegistryHive : IDisposable
    {
        private readonly string _mountName;

        private OfflineRegistryHive(string mountName, string hiveFile, string mountPrefix)
        {
            _mountName = mountName;
            Root = Registry.LocalMachine.OpenSubKey(mountName) ?? throw new InvalidOperationException($"Не удалось открыть HKLM\\{mountName}.");
            DisplayName = $@"HKLM\{mountName}";
            HiveFile = hiveFile;
            MountPrefix = mountPrefix;
        }

        public RegistryKey Root { get; }

        public string DisplayName { get; }

        public string HiveFile { get; }

        public string MountPrefix { get; }

        public static OfflineRegistryHive? TryLoad(string hiveFile, string mountPrefix, List<string> warnings)
        {
            if (!File.Exists(hiveFile))
            {
                warnings.Add($"Hive не найден: {hiveFile}");
                return null;
            }

            var mountName = $"{mountPrefix}_{Environment.ProcessId}";
            RunReg("unload", $@"HKLM\{mountName}", ignoreErrors: true);
            var result = RunReg("load", $@"HKLM\{mountName} ""{hiveFile}""", ignoreErrors: false);
            if (result.ExitCode != 0)
            {
                warnings.Add($"Не удалось загрузить hive {hiveFile}. Код reg.exe: {result.ExitCode}.");
                return null;
            }

            try
            {
                return new OfflineRegistryHive(mountName, hiveFile, mountPrefix);
            }
            catch (Exception ex)
            {
                warnings.Add(ex.Message);
                RunReg("unload", $@"HKLM\{mountName}", ignoreErrors: true);
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
}
