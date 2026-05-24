using Microsoft.Win32;

namespace IUnlocker;

public sealed record StartupEntry(
    string Category,
    string Name,
    string Type,
    string Scope,
    string Source,
    string Command,
    string Location,
    RegistryHive? RegistryHive = null,
    RegistryView RegistryView = RegistryView.Default,
    string? RegistryKeyPath = null,
    string? RegistryValueName = null,
    RegistryValueKind RegistryValueKind = RegistryValueKind.String,
    string? RegistryEditText = null,
    string? OfflineRegistryHiveFile = null,
    string? OfflineRegistryMountPrefix = null,
    string? ScheduledTaskPath = null,
    string? OfflineScheduledTaskFile = null,
    string SignatureStatus = "",
    string SignaturePublisher = "")
{
    public bool CanEditRegistry =>
        (RegistryHive is not null &&
         !string.IsNullOrWhiteSpace(RegistryKeyPath) &&
         RegistryValueName is not null) ||
        (!string.IsNullOrWhiteSpace(OfflineRegistryHiveFile) &&
         !string.IsNullOrWhiteSpace(OfflineRegistryMountPrefix) &&
         !string.IsNullOrWhiteSpace(RegistryKeyPath) &&
         RegistryValueName is not null);

    public bool CanEditScheduledTask =>
        Category == "Scheduled Task" &&
        (!string.IsNullOrWhiteSpace(ScheduledTaskPath) ||
         !string.IsNullOrWhiteSpace(OfflineScheduledTaskFile));
}

public sealed record StartupScanResult(
    IReadOnlyList<StartupEntry> Entries,
    IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<string> ScheduledTaskFolders { get; init; } = [];
}
