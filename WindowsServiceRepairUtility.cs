using Microsoft.Win32;

namespace IUnlocker;

internal static class WindowsServiceRepairUtility
{
    public static IReadOnlyList<ServiceRepairDefinition> Definitions { get; } =
    [
        new("RpcSs", "Удалённый вызов процедур (RPC)", 2),
        new("RpcEptMapper", "Сопоставитель конечных точек RPC", 2),
        new("DcomLaunch", "Запуск процессов DCOM", 2),
        new("EventLog", "Журнал событий Windows", 2),
        new("Schedule", "Планировщик заданий", 2),
        new("ProfSvc", "Служба профилей пользователей", 2),
        new("UserManager", "Диспетчер пользователей", 2),
        new("Winmgmt", "Инструментарий управления Windows", 2),
        new("PlugPlay", "Plug and Play", 3),
        new("TrustedInstaller", "Установщик модулей Windows", 3),
        new("wuauserv", "Центр обновления Windows", 3),
        new("BITS", "Фоновая интеллектуальная служба передачи", 3),
        new("BFE", "Базовая служба фильтрации", 2),
        new("mpssvc", "Брандмауэр Windows", 2),
        new("Dhcp", "DHCP-клиент", 2),
        new("Dnscache", "DNS-клиент", 2),
        new("LanmanWorkstation", "Рабочая станция", 2),
    ];

    public static IReadOnlyList<ServiceRepairRow> Scan(AppSession session)
    {
        return WithServicesKey(session, writable: false, (servicesKey, _) =>
        {
            var rows = new List<ServiceRepairRow>();
            foreach (var definition in Definitions)
            {
                using var service = servicesKey.OpenSubKey(definition.Name, writable: false);
                if (service is null)
                {
                    rows.Add(new ServiceRepairRow(
                        definition.Name,
                        definition.DisplayName,
                        "не найдено",
                        StartText(definition.RecommendedStart),
                        "нет службы",
                        definition.RecommendedStart));
                    continue;
                }

                var currentStart = ReadDWord(service, "Start");
                var displayName = Convert.ToString(service.GetValue("DisplayName"));
                var imagePath = Convert.ToString(service.GetValue("ImagePath")) ?? string.Empty;
                var status = currentStart == definition.RecommendedStart
                    ? "норма"
                    : "отличается";
                rows.Add(new ServiceRepairRow(
                    definition.Name,
                    string.IsNullOrWhiteSpace(displayName) ? definition.DisplayName : displayName,
                    currentStart is null ? "неизвестно" : StartText(currentStart.Value),
                    StartText(definition.RecommendedStart),
                    status,
                    definition.RecommendedStart,
                    imagePath));
            }

            return rows;
        });
    }

    public static void Restore(AppSession session, IEnumerable<ServiceRepairDefinition> definitions)
    {
        WithServicesKey(session, writable: true, (servicesKey, _) =>
        {
            foreach (var definition in definitions)
            {
                using var service = servicesKey.OpenSubKey(definition.Name, writable: true);
                service?.SetValue("Start", definition.RecommendedStart, RegistryValueKind.DWord);
            }

            return true;
        });
    }

    public static ServiceRepairDefinition? FindDefinition(string serviceName)
    {
        return Definitions.FirstOrDefault(item => item.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
    }

    public static string StartText(int start)
    {
        return start switch
        {
            0 => "Boot",
            1 => "System",
            2 => "Automatic",
            3 => "Manual",
            4 => "Disabled",
            _ => start.ToString(),
        };
    }

    private static T WithServicesKey<T>(AppSession session, bool writable, Func<RegistryKey, string, T> action)
    {
        if (session.IsWinPe && session.WindowsPath is not null && !session.DriveRoot.StartsWith("X:", StringComparison.OrdinalIgnoreCase))
        {
            var systemHive = Path.Combine(session.WindowsPath, "System32", "config", "SYSTEM");
            using var hive = OfflineRegistryHiveMount.Load(systemHive, "IUnlocker_SERVICE_REPAIR");
            var controlSet = GetCurrentControlSet(hive.Root);
            using var services = hive.Root.OpenSubKey($@"{controlSet}\Services", writable)
                ?? throw new InvalidOperationException($@"Не удалось открыть offline {controlSet}\Services.");
            return action(services, controlSet);
        }

        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        using var liveServices = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Services", writable)
            ?? throw new InvalidOperationException(@"Не удалось открыть HKLM\SYSTEM\CurrentControlSet\Services.");
        return action(liveServices, "CurrentControlSet");
    }

    private static string GetCurrentControlSet(RegistryKey systemRoot)
    {
        using var selectKey = systemRoot.OpenSubKey("Select");
        var current = selectKey?.GetValue("Current") is int value ? value : 1;
        return $"ControlSet{current:000}";
    }

    private static int? ReadDWord(RegistryKey key, string valueName)
    {
        return key.GetValue(valueName) switch
        {
            int value => value,
            long value => (int)value,
            string text when int.TryParse(text, out var value) => value,
            _ => null,
        };
    }
}

internal sealed record ServiceRepairDefinition(string Name, string DisplayName, int RecommendedStart);

internal sealed record ServiceRepairRow(
    string Name,
    string DisplayName,
    string CurrentStart,
    string RecommendedStart,
    string Status,
    int RecommendedStartValue,
    string ImagePath = "");
