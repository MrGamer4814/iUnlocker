using Microsoft.Win32;

namespace IUnlocker;

public static class StartupScanner
{
    private const string CurrentVersion = @"SOFTWARE\Microsoft\Windows\CurrentVersion";
    private const string CurrentVersionNt = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string ClassesRoot = @"SOFTWARE\Classes";

    public static StartupScanResult Scan()
    {
        var entries = new List<StartupEntry>();
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scheduledTaskFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddRunEntries(entries, warnings, seen);
        AddStartupFolderEntries(entries, warnings, seen);
        AddWinlogonEntries(entries, warnings, seen);
        AddCommandLineEntries(entries, warnings, seen);
        AddBootExecuteEntries(entries, warnings, seen);
        AddAppInitEntries(entries, warnings, seen);
        AddImageHijackEntries(entries, warnings, seen);
        AddExplorerAddons(entries, warnings, seen);
        AddServicesAndDrivers(entries, warnings, seen);
        AddScheduledTaskEntries(entries, warnings, seen, scheduledTaskFolders);
        AddWmiEntries(entries, warnings, seen);
        AddLsaEntries(entries, warnings, seen);
        AddPrintMonitorEntries(entries, warnings, seen);

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

    private static void AddRunEntries(List<StartupEntry> entries, List<string> warnings, HashSet<string> seen)
    {
        var runKeys = new[]
        {
            "Run",
            "RunOnce",
            "RunOnceEx",
            "RunServices",
            "RunServicesOnce",
            @"Policies\Explorer\Run",
        };

        foreach (var target in GetUserAndMachineTargets())
        {
            foreach (var runKey in runKeys)
            {
                ReadRegistryValues(target, $@"{CurrentVersion}\{runKey}", "Run", "Реестр", runKey, entries, warnings, seen);
            }
        }

        foreach (var target in GetUserAndMachineTargets())
        {
            ReadSubKeyValue(
                target,
                $@"{CurrentVersion}\RunOnceEx",
                "Run",
                "Реестр",
                "RunOnceEx",
                "Depend",
                entries,
                warnings,
                seen);
        }

        foreach (var target in GetUserAndMachineTargets())
        {
            ReadSubKeyValue(
                target,
                $@"{CurrentVersion}\RunOnceEx",
                "Run",
                "Реестр",
                "RunOnceEx",
                string.Empty,
                entries,
                warnings,
                seen);
        }

        foreach (var target in GetUserAndMachineTargets())
        {
            ReadSubKeyValue(
                target,
                @"SOFTWARE\Microsoft\Active Setup\Installed Components",
                "Active Setup",
                "Реестр",
                "Active Setup StubPath",
                "StubPath",
                entries,
                warnings,
                seen);
        }
    }

    private static void AddWinlogonEntries(List<StartupEntry> entries, List<string> warnings, HashSet<string> seen)
    {
        var values = new[] { "Shell", "Userinit", "Taskman", "AppSetup", "VmApplet", "GinaDLL", "System" };

        foreach (var target in GetUserAndMachineTargets())
        {
            ReadSelectedRegistryValues(
                target,
                $@"{CurrentVersionNt}\Winlogon",
                "Winlogon",
                "Реестр",
                "Winlogon",
                values,
                entries,
                warnings,
                seen);
        }

        foreach (var target in GetMachineTargets())
        {
            ReadSubKeyValue(
                target,
                $@"{CurrentVersionNt}\Winlogon\Notify",
                "Winlogon",
                "Реестр",
                "Winlogon Notify",
                "DLLName",
                entries,
                warnings,
                seen);
        }
    }

    private static void AddCommandLineEntries(List<StartupEntry> entries, List<string> warnings, HashSet<string> seen)
    {
        foreach (var target in GetUserAndMachineTargets())
        {
            ReadSelectedRegistryValues(
                target,
                @"SOFTWARE\Microsoft\Command Processor",
                "CMDLINE",
                "Реестр",
                "Command Processor AutoRun",
                ["AutoRun"],
                entries,
                warnings,
                seen);
        }

        foreach (var target in GetSystemTargets())
        {
            ReadSelectedRegistryValues(
                target,
                @"SYSTEM\Setup",
                "CMDLINE",
                "Реестр",
                "Setup CmdLine",
                ["CmdLine", "SetupType"],
                entries,
                warnings,
                seen,
                includeEmpty: true);
        }
    }

    private static void AddBootExecuteEntries(List<StartupEntry> entries, List<string> warnings, HashSet<string> seen)
    {
        var values = new[] { "BootExecute", "SetupExecute", "Execute", "S0InitialCommand" };

        foreach (var target in GetSystemTargets())
        {
            ReadSelectedRegistryValues(
                target,
                @"SYSTEM\CurrentControlSet\Control\Session Manager",
                "BootExecute",
                "Реестр",
                "Session Manager",
                values,
                entries,
                warnings,
                seen);
        }
    }

    private static void AddAppInitEntries(List<StartupEntry> entries, List<string> warnings, HashSet<string> seen)
    {
        foreach (var target in GetMachineTargets())
        {
            ReadSelectedRegistryValues(
                target,
                $@"{CurrentVersionNt}\Windows",
                "AppInit_DLLs",
                "Реестр",
                "AppInit DLLs",
                ["AppInit_DLLs", "LoadAppInit_DLLs", "RequireSignedAppInit_DLLs"],
                entries,
                warnings,
                seen,
                includeEmpty: true);
        }
    }

    private static void AddImageHijackEntries(List<StartupEntry> entries, List<string> warnings, HashSet<string> seen)
    {
        foreach (var target in GetMachineTargets())
        {
            ReadSubKeyValue(
                target,
                $@"{CurrentVersionNt}\Image File Execution Options",
                "IFEO",
                "Реестр",
                "Image File Execution Options",
                "Debugger",
                entries,
                warnings,
                seen);

            ReadSubKeyValue(
                target,
                $@"{CurrentVersionNt}\SilentProcessExit",
                "IFEO",
                "Реестр",
                "Silent Process Exit",
                "MonitorProcess",
                entries,
                warnings,
                seen);
        }
    }

    private static void AddExplorerAddons(List<StartupEntry> entries, List<string> warnings, HashSet<string> seen)
    {
        var subKeySources = new[]
        {
            new ExplorerSubKey($@"{CurrentVersion}\Explorer\Browser Helper Objects", "BHO", true),
            new ExplorerSubKey($@"{CurrentVersion}\Explorer\ShellExecuteHooks", "ShellExecuteHooks", true),
            new ExplorerSubKey($@"{CurrentVersion}\Explorer\ShellIconOverlayIdentifiers", "ShellIconOverlay", true),
            new ExplorerSubKey($@"{ClassesRoot}\Directory\Shellex\ContextMenuHandlers", "ContextMenuHandlers", true),
            new ExplorerSubKey($@"{ClassesRoot}\Directory\Background\Shellex\ContextMenuHandlers", "ContextMenuHandlers", true),
            new ExplorerSubKey($@"{ClassesRoot}\Drive\Shellex\ContextMenuHandlers", "ContextMenuHandlers", true),
        };

        foreach (var target in GetUserAndMachineTargets())
        {
            foreach (var source in subKeySources)
            {
                ReadExplorerSubKeys(target, source, entries, warnings, seen);
            }

            ReadExplorerValueClsids(
                target,
                $@"{CurrentVersion}\ShellServiceObjectDelayLoad",
                "ShellServiceObjectDelayLoad",
                entries,
                warnings,
                seen);

            ReadExplorerValueClsids(
                target,
                $@"{CurrentVersion}\Explorer\SharedTaskScheduler",
                "SharedTaskScheduler",
                entries,
                warnings,
                seen);
        }
    }

    private static void AddServicesAndDrivers(List<StartupEntry> entries, List<string> warnings, HashSet<string> seen)
    {
        foreach (var target in GetSystemTargets())
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(target.Hive, target.View);
                using var servicesKey = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");

                if (servicesKey is null)
                {
                    continue;
                }

                foreach (var serviceName in servicesKey.GetSubKeyNames())
                {
                    using var serviceKey = servicesKey.OpenSubKey(serviceName);
                    if (serviceKey is null)
                    {
                        continue;
                    }

                    var start = TryGetInt(serviceKey.GetValue("Start"));
                    if (start is null or > 2)
                    {
                        continue;
                    }

                    var imagePath = ValueToString(serviceKey.GetValue("ImagePath"));
                    var serviceDll = ReadServiceDll(serviceKey);
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
                            "Service Control Manager",
                            command,
                            $@"{target.DisplayHive}\SYSTEM\CurrentControlSet\Services\{serviceName} ({ViewName(target.View)})",
                            RegistryHive: target.Hive,
                            RegistryView: target.View,
                            RegistryKeyPath: $@"SYSTEM\CurrentControlSet\Services\{serviceName}",
                            StartType: scope));
                }
            }
            catch (Exception ex) when (IsRegistryAccessException(ex))
            {
                warnings.Add($"Не удалось прочитать службы: {ex.Message}");
            }
        }
    }

    private static string ReadServiceDll(RegistryKey serviceKey)
    {
        using var parametersKey = serviceKey.OpenSubKey("Parameters");
        return parametersKey is null ? string.Empty : ValueToString(parametersKey.GetValue("ServiceDll"));
    }

    private static void AddStartupFolderEntries(List<StartupEntry> entries, List<string> warnings, HashSet<string> seen)
    {
        var folders = new[]
        {
            new StartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Текущий пользователь"),
            new StartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Все пользователи"),
        };

        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder.Path) || !Directory.Exists(folder.Path))
            {
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(folder.Path))
                {
                    var fileName = Path.GetFileName(file);
                    var command = TryResolveShortcut(file) ?? file;

                    AddEntry(
                        entries,
                        seen,
                        new StartupEntry(
                            "Startup Folder",
                            Path.GetFileNameWithoutExtension(fileName),
                            "Файл",
                            folder.Scope,
                            "Startup folder",
                            command,
                            file));
                }
            }
            catch (Exception ex) when (IsRegistryAccessException(ex))
            {
                warnings.Add($"Не удалось прочитать папку {folder.Path}: {ex.Message}");
            }
        }
    }

    private static string? TryResolveShortcut(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return null;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(path);
            string targetPath = shortcut.TargetPath;
            string arguments = shortcut.Arguments;

            return string.IsNullOrWhiteSpace(arguments)
                ? targetPath
                : $"{targetPath} {arguments}";
        }
        catch
        {
            return null;
        }
    }

    private static void AddScheduledTaskEntries(
        List<StartupEntry> entries,
        List<string> warnings,
        HashSet<string> seen,
        HashSet<string> folders)
    {
        try
        {
            var serviceType = Type.GetTypeFromProgID("Schedule.Service");
            if (serviceType is null)
            {
                warnings.Add("Планировщик заданий недоступен через COM.");
                return;
            }

            dynamic service = Activator.CreateInstance(serviceType)!;
            service.Connect();
            dynamic rootFolder = service.GetFolder("\\");
            ReadTaskFolder(rootFolder, entries, warnings, seen, folders);
        }
        catch (Exception ex)
        {
            warnings.Add($"Не удалось прочитать Планировщик заданий: {ex.Message}");
        }
    }

    private static void ReadTaskFolder(
        dynamic folder,
        List<StartupEntry> entries,
        List<string> warnings,
        HashSet<string> seen,
        HashSet<string> folders)
    {
        try
        {
            folders.Add(Convert.ToString(folder.Path) ?? "\\");

            foreach (dynamic task in folder.GetTasks(0))
            {
                AddScheduledTask(task, entries, seen);
            }

            foreach (dynamic childFolder in folder.GetFolders(0))
            {
                ReadTaskFolder(childFolder, entries, warnings, seen, folders);
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Не удалось прочитать папку заданий: {ex.Message}");
        }
    }

    private static void AddScheduledTask(dynamic task, List<StartupEntry> entries, HashSet<string> seen)
    {
        try
        {
            dynamic definition = task.Definition;

            bool enabled = task.Enabled;
            var command = GetTaskCommand(definition.Actions);
            var taskPath = Convert.ToString(task.Path);

            AddEntry(
                entries,
                seen,
                new StartupEntry(
                    "Scheduled Task",
                    Convert.ToString(task.Name) ?? "(без имени)",
                    "Задание",
                    enabled ? "Включено" : "Отключено",
                    "Планировщик",
                    command,
                    $"Task Scheduler: {task.Path}",
                    ScheduledTaskPath: taskPath,
                    TaskTriggers: GetTaskTriggers(definition.Triggers),
                    TaskLastRun: FormatComDate(task.LastRunTime),
                    TaskNextRun: FormatComDate(task.NextRunTime),
                    TaskAuthor: Convert.ToString(definition.RegistrationInfo.Author) ?? string.Empty,
                    TaskHidden: GetTaskHiddenText(definition)));
        }
        catch
        {
            // Some system tasks do not expose all properties to non-elevated processes.
        }
    }

    private static string GetTaskCommand(dynamic actions)
    {
        var parts = new List<string>();

        foreach (dynamic action in actions)
        {
            try
            {
                int type = action.Type;

                if (type == 0)
                {
                    string path = action.Path;
                    string arguments = action.Arguments;
                    parts.Add(string.IsNullOrWhiteSpace(arguments) ? path : $"{path} {arguments}");
                }
                else
                {
                    parts.Add($"Action type {type}");
                }
            }
            catch
            {
                parts.Add("(действие недоступно)");
            }
        }

        return parts.Count == 0 ? "(команда не указана)" : string.Join("; ", parts);
    }

    private static string GetTaskTriggers(dynamic triggers)
    {
        var parts = new List<string>();
        foreach (dynamic trigger in triggers)
        {
            try
            {
                int type = trigger.Type;
                var typeText = type switch
                {
                    1 => "однократно",
                    2 => "ежедневно",
                    3 => "еженедельно",
                    4 => "ежемесячно",
                    5 => "ежемесячно по дням",
                    6 => "при простое",
                    7 => "при регистрации",
                    8 => "при загрузке",
                    9 => "при входе",
                    11 => "по событию",
                    12 => "при создании/изменении",
                    _ => $"тип {type}",
                };
                string start = trigger.StartBoundary;
                parts.Add(string.IsNullOrWhiteSpace(start) ? typeText : $"{typeText}: {start}");
            }
            catch
            {
                parts.Add("(триггер недоступен)");
            }
        }

        return parts.Count == 0 ? "(нет триггеров)" : string.Join("; ", parts);
    }

    private static string GetTaskHiddenText(dynamic definition)
    {
        try
        {
            bool hidden = definition.Settings.Hidden;
            return hidden ? "Да" : "Нет";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatComDate(object value)
    {
        try
        {
            if (value is DateTime dateTime && dateTime.Year > 1900)
            {
                return dateTime.ToString("yyyy-MM-dd HH:mm:ss");
            }

            var text = Convert.ToString(value) ?? string.Empty;
            return DateTime.TryParse(text, out var parsed) && parsed.Year > 1900
                ? parsed.ToString("yyyy-MM-dd HH:mm:ss")
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void AddWmiEntries(List<StartupEntry> entries, List<string> warnings, HashSet<string> seen)
    {
        try
        {
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (locatorType is null)
            {
                warnings.Add("WMI недоступен через COM.");
                return;
            }

            dynamic locator = Activator.CreateInstance(locatorType)!;
            dynamic service = locator.ConnectServer(".", @"root\subscription");

            foreach (dynamic consumer in service.ExecQuery("SELECT * FROM CommandLineEventConsumer"))
            {
                AddEntry(
                    entries,
                    seen,
                    new StartupEntry(
                        "WMI",
                        Convert.ToString(consumer.Name) ?? "(без имени)",
                        "CommandLineEventConsumer",
                        "root\\subscription",
                        "WMI permanent consumer",
                        Convert.ToString(consumer.CommandLineTemplate) ?? string.Empty,
                        @"WMI: root\subscription\CommandLineEventConsumer"));
            }

            foreach (dynamic consumer in service.ExecQuery("SELECT * FROM ActiveScriptEventConsumer"))
            {
                AddEntry(
                    entries,
                    seen,
                    new StartupEntry(
                        "WMI",
                        Convert.ToString(consumer.Name) ?? "(без имени)",
                        "ActiveScriptEventConsumer",
                        "root\\subscription",
                        "WMI permanent consumer",
                        Convert.ToString(consumer.ScriptFileName) ?? Convert.ToString(consumer.ScriptingEngine) ?? string.Empty,
                        @"WMI: root\subscription\ActiveScriptEventConsumer"));
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Не удалось прочитать WMI автозапуск: {ex.Message}");
        }
    }

    private static void AddLsaEntries(List<StartupEntry> entries, List<string> warnings, HashSet<string> seen)
    {
        foreach (var target in GetSystemTargets())
        {
            ReadSelectedRegistryValues(
                target,
                @"SYSTEM\CurrentControlSet\Control\Lsa",
                "LSA",
                "Реестр",
                "LSA providers",
                ["Authentication Packages", "Notification Packages", "Security Packages", "OSConfig"],
                entries,
                warnings,
                seen);
        }
    }

    private static void AddPrintMonitorEntries(List<StartupEntry> entries, List<string> warnings, HashSet<string> seen)
    {
        foreach (var target in GetSystemTargets())
        {
            ReadSubKeyValue(
                target,
                @"SYSTEM\CurrentControlSet\Control\Print\Monitors",
                "Print Monitor",
                "Реестр",
                "Print monitors",
                "Driver",
                entries,
                warnings,
                seen);
        }
    }

    private static void ReadRegistryValues(
        RegistryTarget target,
        string keyPath,
        string category,
        string type,
        string source,
        List<StartupEntry> entries,
        List<string> warnings,
        HashSet<string> seen)
    {
        try
        {
            using var key = OpenSubKey(target, keyPath);
            if (key is null)
            {
                return;
            }

            foreach (var valueName in key.GetValueNames())
            {
                AddRegistryValueEntry(target, keyPath, key, valueName, category, type, source, entries, seen);
            }
        }
        catch (Exception ex) when (IsRegistryAccessException(ex))
        {
            warnings.Add($"Нет доступа к {target.DisplayHive}\\{keyPath}: {ex.Message}");
        }
    }

    private static void ReadSelectedRegistryValues(
        RegistryTarget target,
        string keyPath,
        string category,
        string type,
        string source,
        IReadOnlyCollection<string> valueNames,
        List<StartupEntry> entries,
        List<string> warnings,
        HashSet<string> seen,
        bool includeEmpty = false)
    {
        try
        {
            using var key = OpenSubKey(target, keyPath);
            if (key is null)
            {
                return;
            }

            foreach (var valueName in valueNames)
            {
                if (key.GetValue(valueName) is null)
                {
                    continue;
                }

                AddRegistryValueEntry(target, keyPath, key, valueName, category, type, source, entries, seen, includeEmpty);
            }
        }
        catch (Exception ex) when (IsRegistryAccessException(ex))
        {
            warnings.Add($"Нет доступа к {target.DisplayHive}\\{keyPath}: {ex.Message}");
        }
    }

    private static void ReadSubKeyValue(
        RegistryTarget target,
        string keyPath,
        string category,
        string type,
        string source,
        string valueName,
        List<StartupEntry> entries,
        List<string> warnings,
        HashSet<string> seen)
    {
        try
        {
            using var key = OpenSubKey(target, keyPath);
            if (key is null)
            {
                return;
            }

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                using var subKey = key.OpenSubKey(subKeyName);
                if (subKey is null)
                {
                    continue;
                }

                var value = valueName.Length == 0 ? subKey.GetValue(null) : subKey.GetValue(valueName);
                var command = ValueToString(value);

                if (string.IsNullOrWhiteSpace(command))
                {
                    continue;
                }

                var displayValueName = valueName.Length == 0 ? "(по умолчанию)" : valueName;
                var subKeyPath = $@"{keyPath}\{subKeyName}";
                AddEntry(
                    entries,
                    seen,
                    new StartupEntry(
                        category,
                        subKeyName,
                        type,
                        target.Scope,
                        source,
                        command,
                        $@"{target.DisplayHive}\{subKeyPath}\{displayValueName} ({ViewName(target.View)})",
                        target.Hive,
                        target.View,
                        subKeyPath,
                        valueName.Length == 0 ? string.Empty : valueName,
                        GetValueKind(subKey, valueName),
                        ValueToEditableString(value)));
            }
        }
        catch (Exception ex) when (IsRegistryAccessException(ex))
        {
            warnings.Add($"Нет доступа к {target.DisplayHive}\\{keyPath}: {ex.Message}");
        }
    }

    private static void AddRegistryValueEntry(
        RegistryTarget target,
        string keyPath,
        RegistryKey key,
        string valueName,
        string category,
        string type,
        string source,
        List<StartupEntry> entries,
        HashSet<string> seen,
        bool includeEmpty = false)
    {
        var value = key.GetValue(valueName);
        var command = ValueToString(value);
        if (!includeEmpty && string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(valueName) ? "(по умолчанию)" : valueName;
        var rawValueName = string.IsNullOrWhiteSpace(valueName) ? string.Empty : valueName;
        AddEntry(
            entries,
            seen,
            new StartupEntry(
                category,
                name,
                type,
                target.Scope,
                source,
                command,
                $@"{target.DisplayHive}\{keyPath}\{name} ({ViewName(target.View)})",
                target.Hive,
                target.View,
                keyPath,
                rawValueName,
                GetValueKind(key, rawValueName),
                ValueToEditableString(value)));
    }

    private static void ReadExplorerSubKeys(
        RegistryTarget target,
        ExplorerSubKey source,
        List<StartupEntry> entries,
        List<string> warnings,
        HashSet<string> seen)
    {
        try
        {
            using var key = OpenSubKey(target, source.Path);
            if (key is null)
            {
                return;
            }

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                using var subKey = key.OpenSubKey(subKeyName);
                var clsid = source.SubKeyNameIsClsid ? subKeyName : ValueToString(subKey?.GetValue(null));
                var command = ResolveClsid(target, clsid);

                AddEntry(
                    entries,
                    seen,
                    new StartupEntry(
                        "Explorer",
                        subKeyName,
                        "Shell extension",
                        target.Scope,
                        source.Source,
                        string.IsNullOrWhiteSpace(command) ? clsid : command,
                        $@"{target.DisplayHive}\{source.Path}\{subKeyName} ({ViewName(target.View)})"));
            }
        }
        catch (Exception ex) when (IsRegistryAccessException(ex))
        {
            warnings.Add($"Нет доступа к {target.DisplayHive}\\{source.Path}: {ex.Message}");
        }
    }

    private static void ReadExplorerValueClsids(
        RegistryTarget target,
        string keyPath,
        string source,
        List<StartupEntry> entries,
        List<string> warnings,
        HashSet<string> seen)
    {
        try
        {
            using var key = OpenSubKey(target, keyPath);
            if (key is null)
            {
                return;
            }

            foreach (var valueName in key.GetValueNames())
            {
                var clsid = ValueToString(key.GetValue(valueName));
                var command = ResolveClsid(target, clsid);

                AddEntry(
                    entries,
                    seen,
                    new StartupEntry(
                        "Explorer",
                        string.IsNullOrWhiteSpace(valueName) ? "(по умолчанию)" : valueName,
                        "Shell extension",
                        target.Scope,
                        source,
                        string.IsNullOrWhiteSpace(command) ? clsid : command,
                        $@"{target.DisplayHive}\{keyPath}\{valueName} ({ViewName(target.View)})"));
            }
        }
        catch (Exception ex) when (IsRegistryAccessException(ex))
        {
            warnings.Add($"Нет доступа к {target.DisplayHive}\\{keyPath}: {ex.Message}");
        }
    }

    private static string ResolveClsid(RegistryTarget target, string clsid)
    {
        if (string.IsNullOrWhiteSpace(clsid))
        {
            return string.Empty;
        }

        var cleanClsid = clsid.Trim();
        var clsidPath = $@"{ClassesRoot}\CLSID\{cleanClsid}";

        foreach (var probeTarget in new[]
        {
            target,
            target with { Hive = RegistryHive.LocalMachine, Scope = "Все пользователи", DisplayHive = "HKLM" },
            target with { Hive = RegistryHive.CurrentUser, Scope = "Текущий пользователь", DisplayHive = "HKCU" },
        })
        {
            try
            {
                using var clsidKey = OpenSubKey(probeTarget, clsidPath);
                if (clsidKey is null)
                {
                    continue;
                }

                var description = ValueToString(clsidKey.GetValue(null));
                using var inproc = clsidKey.OpenSubKey("InprocServer32");
                using var localServer = clsidKey.OpenSubKey("LocalServer32");
                var server = ValueToString(inproc?.GetValue(null));
                if (string.IsNullOrWhiteSpace(server))
                {
                    server = ValueToString(localServer?.GetValue(null));
                }

                return string.IsNullOrWhiteSpace(description)
                    ? server
                    : $"{description} | {server}";
            }
            catch
            {
                // Try the next registry view/hive.
            }
        }

        return string.Empty;
    }

    private static RegistryKey? OpenSubKey(RegistryTarget target, string path)
    {
        using var baseKey = RegistryKey.OpenBaseKey(target.Hive, target.View);
        return baseKey.OpenSubKey(path);
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
            string text => Environment.ExpandEnvironmentVariables(text),
            string[] values => string.Join("; ", values.Select(Environment.ExpandEnvironmentVariables)),
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

    private static bool IsRegistryAccessException(Exception ex)
    {
        return ex is UnauthorizedAccessException or IOException or System.Security.SecurityException;
    }

    private static IReadOnlyList<RegistryTarget> GetUserAndMachineTargets()
    {
        return
        [
            new(RegistryHive.CurrentUser, RegistryView.Registry64, "Текущий пользователь", "HKCU"),
            new(RegistryHive.CurrentUser, RegistryView.Registry32, "Текущий пользователь", "HKCU"),
            new(RegistryHive.LocalMachine, RegistryView.Registry64, "Все пользователи", "HKLM"),
            new(RegistryHive.LocalMachine, RegistryView.Registry32, "Все пользователи", "HKLM"),
        ];
    }

    private static IReadOnlyList<RegistryTarget> GetMachineTargets()
    {
        return
        [
            new(RegistryHive.LocalMachine, RegistryView.Registry64, "Все пользователи", "HKLM"),
            new(RegistryHive.LocalMachine, RegistryView.Registry32, "Все пользователи", "HKLM"),
        ];
    }

    private static IReadOnlyList<RegistryTarget> GetSystemTargets()
    {
        return
        [
            new(RegistryHive.LocalMachine, RegistryView.Registry64, "Система", "HKLM"),
        ];
    }

    private static string ViewName(RegistryView view)
    {
        return view == RegistryView.Registry32 ? "32-bit" : "64-bit";
    }

    private sealed record RegistryTarget(RegistryHive Hive, RegistryView View, string Scope, string DisplayHive);

    private sealed record StartupFolder(string Path, string Scope);

    private sealed record ExplorerSubKey(string Path, string Source, bool SubKeyNameIsClsid);
}
