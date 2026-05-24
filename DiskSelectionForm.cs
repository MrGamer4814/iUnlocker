using Microsoft.Win32;

namespace IUnlocker;

public sealed class DiskSelectionForm : Form
{
    private readonly ListView _driveList = new();
    private readonly Label _windowsLabel = new();
    private readonly Label _environmentLabel = new();
    private readonly Button _continueButton = new();
    private readonly bool _isWinPe;

    private List<DiskCandidate> _candidates = [];

    public AppSession? SelectedSession { get; private set; }

    public DiskSelectionForm()
    {
        _isWinPe = AppSession.DetectWinPe();
        BuildInterface();
        Load += (_, _) => LoadDrives();
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - выбор диска";
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        MinimumSize = new Size(760, 460);
        ClientSize = new Size(820, 500);
        UiTheme.ApplyForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Выберите диск",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6),
        };
        UiTheme.StyleTitle(title);

        _environmentLabel.AutoSize = true;
        _environmentLabel.ForeColor = UiTheme.MutedText;
        _environmentLabel.Text = _isWinPe
            ? "Среда: WinPE. Выберите диск с установленной Windows."
            : "Среда: обычная Windows. Можно выбрать любой доступный диск.";
        _environmentLabel.Margin = new Padding(0, 0, 0, 14);

        _driveList.Dock = DockStyle.Fill;
        _driveList.View = View.Details;
        _driveList.FullRowSelect = true;
        _driveList.HideSelection = false;
        _driveList.MultiSelect = false;
        _driveList.Columns.Add("Диск", 90);
        _driveList.Columns.Add("Метка", 180);
        _driveList.Columns.Add("Среда", 90);
        _driveList.Columns.Add("Windows", 360);
        _driveList.Columns.Add("Свободно", 110, HorizontalAlignment.Right);
        _driveList.Columns.Add("Размер", 110, HorizontalAlignment.Right);
        _driveList.SelectedIndexChanged += (_, _) => UpdateSelectionText();
        _driveList.DoubleClick += (_, _) => ContinueWithSelection();
        UiTheme.StyleListView(_driveList);

        _windowsLabel.AutoSize = true;
        _windowsLabel.Padding = new Padding(0, 12, 0, 8);
        _windowsLabel.ForeColor = UiTheme.Text;
        _windowsLabel.Text = "Windows: не выбрана";

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
        };

        _continueButton.Text = "Продолжить";
        _continueButton.AutoSize = true;
        _continueButton.Enabled = false;
        _continueButton.Click += (_, _) => ContinueWithSelection();
        UiTheme.StyleButton(_continueButton, primary: true);

        var refreshButton = new Button
        {
            Text = "Обновить",
            AutoSize = true,
            Margin = new Padding(0, 0, 8, 0),
        };
        refreshButton.Click += (_, _) => LoadDrives();
        UiTheme.StyleButton(refreshButton);

        buttons.Controls.Add(_continueButton);
        buttons.Controls.Add(refreshButton);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(_environmentLabel, 0, 1);
        root.Controls.Add(_driveList, 0, 2);
        root.Controls.Add(_windowsLabel, 0, 3);
        root.Controls.Add(buttons, 0, 4);
        Controls.Add(root);
    }

    private void LoadDrives()
    {
        _candidates = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateCandidate)
            .ToList();

        _driveList.BeginUpdate();
        _driveList.Items.Clear();

        foreach (var candidate in _candidates)
        {
            var item = new ListViewItem(candidate.Root);
            item.SubItems.Add(candidate.DisplayName);
            item.SubItems.Add(candidate.Root.StartsWith("X:", StringComparison.OrdinalIgnoreCase) ? "WinPE" : "");
            item.SubItems.Add(candidate.Root.StartsWith("X:", StringComparison.OrdinalIgnoreCase)
                ? "WinPE, не offline Windows"
                : candidate.WindowsStatus);
            item.SubItems.Add(candidate.FreeSpace is null ? "" : FormatSize(candidate.FreeSpace.Value));
            item.SubItems.Add(candidate.TotalSize is null ? "" : FormatSize(candidate.TotalSize.Value));
            item.Tag = candidate;
            _driveList.Items.Add(item);
        }

        _driveList.EndUpdate();

        var preferred = _candidates.FirstOrDefault(candidate =>
                candidate.WindowsPath is not null &&
                (!_isWinPe || !candidate.Root.StartsWith("X:", StringComparison.OrdinalIgnoreCase)))
            ?? _candidates.FirstOrDefault(candidate => candidate.WindowsPath is not null)
            ?? _candidates.FirstOrDefault();

        if (preferred is not null)
        {
            foreach (ListViewItem item in _driveList.Items)
            {
                if (item.Tag is DiskCandidate candidate && candidate.Equals(preferred))
                {
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                    break;
                }
            }
        }

        UpdateSelectionText();
    }

    private static DiskCandidate CreateCandidate(DriveInfo drive)
    {
        var probe = FindWindowsInstallation(drive.RootDirectory.FullName);
        return new DiskCandidate(
            drive.RootDirectory.FullName,
            string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.DriveType.ToString() : drive.VolumeLabel,
            probe.WindowsPath,
            probe.Status,
            probe.Version,
            probe.Error,
            drive.IsReady,
            drive.TotalSize,
            drive.AvailableFreeSpace);
    }

    private static WindowsProbeResult FindWindowsInstallation(string root)
    {
        WindowsProbeResult? bestFailedProbe = null;
        var candidates = new[]
        {
            Path.Combine(root, "Windows"),
            Path.Combine(root, "WINNT"),
        };

        foreach (var path in candidates)
        {
            var probe = ProbeWindowsDirectory(root, path);
            if (probe.WindowsPath is not null)
            {
                return probe;
            }

            bestFailedProbe ??= probe;
        }

        return bestFailedProbe ?? WindowsProbeResult.NotFound();
    }

    private static WindowsProbeResult ProbeWindowsDirectory(string root, string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return WindowsProbeResult.NotFound();
            }

            var systemHive = Path.Combine(path, "System32", "config", "SYSTEM");
            var softwareHive = Path.Combine(path, "System32", "config", "SOFTWARE");
            var kernel = Path.Combine(path, "System32", "ntoskrnl.exe");
            if (!File.Exists(systemHive) || !File.Exists(softwareHive) || !File.Exists(kernel))
            {
                return WindowsProbeResult.Invalid("папка Windows неполная");
            }

            var version = IsCurrentWindowsPath(path)
                ? ReadLiveWindowsVersion()
                : ReadOfflineWindowsVersion(systemHive, softwareHive);

            if (!version.IsValid)
            {
                return WindowsProbeResult.Invalid(version.Error ?? "не удалось подтвердить Windows через реестр");
            }

            var status = string.IsNullOrWhiteSpace(version.DisplayName)
                ? path
                : $"{version.DisplayName} ({path})";

            return new WindowsProbeResult(path, status, version.DisplayName, null);
        }
        catch (Exception ex)
        {
            return WindowsProbeResult.Invalid(ex.Message);
        }
    }

    private static bool IsCurrentWindowsPath(string path)
    {
        var currentWindowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(currentWindowsPath))
        {
            return false;
        }

        return SameFullPath(path, currentWindowsPath);
    }

    private static bool SameFullPath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd('\\'),
                Path.GetFullPath(right).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static WindowsVersionProbe ReadLiveWindowsVersion()
    {
        try
        {
            using var softwareKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            using var systemKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\Select");
            return ReadWindowsVersion(softwareKey, systemKey);
        }
        catch (Exception ex)
        {
            return WindowsVersionProbe.Failed(ex.Message);
        }
    }

    private static WindowsVersionProbe ReadOfflineWindowsVersion(string systemHive, string softwareHive)
    {
        try
        {
            using var software = OfflineRegistryHiveMount.Load(softwareHive, "IUnlocker_DETECT_SOFTWARE");
            using var system = OfflineRegistryHiveMount.Load(systemHive, "IUnlocker_DETECT_SYSTEM");
            using var softwareKey = software.Root.OpenSubKey(@"Microsoft\Windows NT\CurrentVersion");
            using var systemKey = system.Root.OpenSubKey("Select");
            return ReadWindowsVersion(softwareKey, systemKey);
        }
        catch (Exception ex)
        {
            return WindowsVersionProbe.Failed(ex.Message);
        }
    }

    private static WindowsVersionProbe ReadWindowsVersion(RegistryKey? currentVersionKey, RegistryKey? selectKey)
    {
        if (currentVersionKey is null)
        {
            return WindowsVersionProbe.Failed(@"нет ключа Windows NT\CurrentVersion");
        }

        if (selectKey?.GetValue("Current") is null)
        {
            return WindowsVersionProbe.Failed(@"нет SYSTEM\Select\Current");
        }

        var productName = ReadString(currentVersionKey, "ProductName");
        var edition = ReadString(currentVersionKey, "EditionID");
        var displayVersion = ReadString(currentVersionKey, "DisplayVersion");
        var releaseId = ReadString(currentVersionKey, "ReleaseId");
        var build = ReadString(currentVersionKey, "CurrentBuildNumber") ?? ReadString(currentVersionKey, "CurrentBuild");
        var ubr = currentVersionKey.GetValue("UBR");

        if (string.IsNullOrWhiteSpace(productName) && string.IsNullOrWhiteSpace(build))
        {
            return WindowsVersionProbe.Failed("в SOFTWARE нет ProductName/CurrentBuild");
        }

        var name = !string.IsNullOrWhiteSpace(productName)
            ? productName
            : $"Windows {edition}".Trim();
        var version = displayVersion ?? releaseId;
        var buildText = build;
        if (TryParseBuild(build, out var buildNumber) && buildNumber >= 22000)
        {
            name = NormalizeWindows11Name(name);
        }

        if (ubr is not null && !string.IsNullOrWhiteSpace(buildText))
        {
            buildText = $"{buildText}.{ubr}";
        }

        var parts = new[] { name, version, string.IsNullOrWhiteSpace(buildText) ? null : $"build {buildText}" }
            .Where(part => !string.IsNullOrWhiteSpace(part));

        return WindowsVersionProbe.Valid(string.Join(" ", parts));
    }

    private static string? ReadString(RegistryKey key, string name)
    {
        return key.GetValue(name) as string;
    }

    private static bool TryParseBuild(string? build, out int buildNumber)
    {
        buildNumber = 0;
        return !string.IsNullOrWhiteSpace(build) &&
               int.TryParse(build, out buildNumber);
    }

    private static string NormalizeWindows11Name(string name)
    {
        if (name.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows 11" + name["Windows 10".Length..];
        }

        return name.Equals("Windows", StringComparison.OrdinalIgnoreCase)
            ? "Windows 11"
            : name;
    }

    private void UpdateSelectionText()
    {
        var candidate = GetSelectedCandidate();
        _continueButton.Enabled = candidate is not null;

        if (candidate is null)
        {
            _windowsLabel.Text = "Windows: не выбрана";
            return;
        }

        _windowsLabel.Text = candidate.WindowsPath is null
            ? $"Выбран диск: {candidate.Root}. Windows на этом диске не найдена: {candidate.WindowsError ?? "нет признаков установленной Windows"}."
            : candidate.Root.StartsWith("X:", StringComparison.OrdinalIgnoreCase)
                ? $"Выбран диск: {candidate.Root}. Это WinPE, автозагрузка будет читаться из текущей временной среды."
            : $"Выбран диск: {candidate.Root}. Windows: {candidate.WindowsVersion ?? candidate.WindowsPath}";
    }

    private DiskCandidate? GetSelectedCandidate()
    {
        return _driveList.SelectedItems.Count == 0
            ? null
            : _driveList.SelectedItems[0].Tag as DiskCandidate;
    }

    private void ContinueWithSelection()
    {
        var candidate = GetSelectedCandidate();
        if (candidate is null)
        {
            return;
        }

        if (IsWinPeDrive(candidate.Root))
        {
            var result = MessageBox.Show(
                this,
                "Вы выбрали X:\\. Это временная среда WinPE, а не установленная Windows. Автозагрузка будет показана только для WinPE.\r\n\r\nПродолжить?",
                "Выбран WinPE-диск",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
            {
                return;
            }
        }
        else if (candidate.WindowsPath is null)
        {
            MessageBox.Show(
                this,
                $"На выбранном диске не найдена установленная Windows.\r\n\r\nПричина: {candidate.WindowsError ?? "нет признаков установленной Windows"}.\r\n\r\nВыберите диск, где находится полноценная установленная Windows.",
                "Windows не найдена",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        SelectedSession = new AppSession(candidate.Root, candidate.WindowsPath, _isWinPe);
        DialogResult = DialogResult.OK;
        Close();
    }

    private static bool IsWinPeDrive(string root)
    {
        return root.StartsWith("X:", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        var size = (double)bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} {units[unit]}" : $"{size:0.##} {units[unit]}";
    }

    private sealed record WindowsProbeResult(string? WindowsPath, string Status, string? Version, string? Error)
    {
        public static WindowsProbeResult NotFound()
        {
            return new WindowsProbeResult(null, "не найдена", null, "папка Windows не найдена");
        }

        public static WindowsProbeResult Invalid(string error)
        {
            return new WindowsProbeResult(null, $"не подтверждена: {error}", null, error);
        }
    }

    private sealed record WindowsVersionProbe(bool IsValid, string? DisplayName, string? Error)
    {
        public static WindowsVersionProbe Valid(string displayName)
        {
            return new WindowsVersionProbe(true, displayName, null);
        }

        public static WindowsVersionProbe Failed(string error)
        {
            return new WindowsVersionProbe(false, null, error);
        }
    }
}
