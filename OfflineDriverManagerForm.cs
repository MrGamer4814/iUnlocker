using Microsoft.Win32;

namespace IUnlocker;

public sealed class OfflineDriverManagerForm : Form
{
    private readonly AppSession _session;
    private readonly ListView _drivers = new();
    private readonly Label _statusLabel = new();
    private readonly Button _refreshButton = new();
    private readonly Button _disableButton = new();
    private readonly Button _deleteEntryButton = new();
    private readonly Button _deleteFileButton = new();

    public OfflineDriverManagerForm(AppSession session)
    {
        _session = session;
        BuildInterface();
        Shown += (_, _) => RefreshDrivers();
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - offline драйверы";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 560);
        ClientSize = new Size(1050, 660);
        UiTheme.ApplyForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        };

        ConfigureButton(_refreshButton, "Обновить", (_, _) => RefreshDrivers(), primary: true);
        ConfigureButton(_disableButton, "Отключить", (_, _) => DisableSelectedDriver());
        ConfigureButton(_deleteEntryButton, "Удалить запись", (_, _) => DeleteSelectedDriverEntry());
        ConfigureButton(_deleteFileButton, "Удалить файл", (_, _) => DeleteSelectedDriverFile());
        actions.Controls.AddRange(new Control[] { _refreshButton, _disableButton, _deleteEntryButton, _deleteFileButton });

        _drivers.Dock = DockStyle.Fill;
        _drivers.View = View.Details;
        _drivers.FullRowSelect = true;
        _drivers.HideSelection = false;
        _drivers.MultiSelect = false;
        _drivers.Columns.Add("Имя", 170);
        _drivers.Columns.Add("Старт", 110);
        _drivers.Columns.Add("Тип", 120);
        _drivers.Columns.Add("Файл", 330);
        _drivers.Columns.Add("Описание", 220);
        _drivers.Columns.Add("Путь", 420);
        _drivers.SelectedIndexChanged += (_, _) => UpdateButtons();
        UiTheme.StyleListView(_drivers);

        var menu = new ContextMenuStrip();
        UiTheme.StyleContextMenu(menu);
        var disableMenuItem = menu.Items.Add("Отключить", null, (_, _) => DisableSelectedDriver());
        var deleteEntryMenuItem = menu.Items.Add("Удалить запись", null, (_, _) => DeleteSelectedDriverEntry());
        var deleteFileMenuItem = menu.Items.Add("Удалить файл", null, (_, _) => DeleteSelectedDriverFile());
        menu.Opening += (_, e) =>
        {
            var entry = GetSelectedEntry();
            disableMenuItem.Enabled = entry is not null;
            deleteEntryMenuItem.Enabled = entry is not null;
            deleteFileMenuItem.Enabled = entry is not null &&
                                         !string.IsNullOrWhiteSpace(entry.ResolvedPath) &&
                                         File.Exists(entry.ResolvedPath);
            UiTheme.HideUnavailableContextMenuItems(menu);
        };
        _drivers.ContextMenuStrip = menu;

        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = UiTheme.MutedText;
        _statusLabel.Padding = new Padding(0, 10, 0, 0);
        _statusLabel.Text = "Драйверы загружаются из offline SYSTEM выбранной Windows.";

        root.Controls.Add(actions, 0, 0);
        root.Controls.Add(_drivers, 0, 1);
        root.Controls.Add(_statusLabel, 0, 2);
        Controls.Add(root);
        UpdateButtons();
    }

    private static void ConfigureButton(Button button, string text, EventHandler click, bool primary = false)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Margin = new Padding(0, 0, 8, 0);
        button.Click += click;
        UiTheme.StyleButton(button, primary);
    }

    private void RefreshDrivers()
    {
        try
        {
            var entries = LoadDrivers();
            _drivers.BeginUpdate();
            _drivers.Items.Clear();
            foreach (var entry in entries)
            {
                var item = new ListViewItem(entry.ServiceName);
                item.SubItems.Add(entry.StartText);
                item.SubItems.Add(entry.TypeText);
                item.SubItems.Add(entry.ImagePath);
                item.SubItems.Add(entry.DisplayName);
                item.SubItems.Add(entry.ResolvedPath);
                item.Tag = entry;
                if (entry.StartValue == 4)
                {
                    item.ForeColor = UiTheme.MutedText;
                }

                _drivers.Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Offline драйверы", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _drivers.EndUpdate();
            UpdateButtons();
            _statusLabel.Text = $"Показано драйверов: {_drivers.Items.Count}.";
        }
    }

    private List<OfflineDriverEntry> LoadDrivers()
    {
        var systemHive = GetSystemHivePath();
        using var hive = OfflineRegistryHiveMount.Load(systemHive, "IUnlocker_DRIVERS");
        var controlSet = GetCurrentControlSetName(hive.Root);
        using var services = hive.Root.OpenSubKey($@"{controlSet}\Services", writable: false)
            ?? throw new InvalidOperationException($@"Не удалось открыть {controlSet}\Services.");

        var result = new List<OfflineDriverEntry>();
        foreach (var serviceName in services.GetSubKeyNames())
        {
            using var service = services.OpenSubKey(serviceName, writable: false);
            if (service is null)
            {
                continue;
            }

            var type = ReadDWord(service, "Type");
            if (type is not 1 and not 2)
            {
                continue;
            }

            var start = ReadDWord(service, "Start") ?? 3;
            var imagePath = Convert.ToString(service.GetValue("ImagePath")) ?? $@"System32\drivers\{serviceName}.sys";
            var displayName = Convert.ToString(service.GetValue("DisplayName")) ?? string.Empty;
            var resolvedPath = ResolveDriverPath(imagePath, serviceName);
            result.Add(new OfflineDriverEntry(
                serviceName,
                displayName,
                imagePath,
                resolvedPath,
                type.Value,
                GetDriverTypeText(type.Value),
                start,
                GetStartText(start)));
        }

        return result
            .OrderBy(entry => entry.StartValue)
            .ThenBy(entry => entry.ServiceName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void DisableSelectedDriver()
    {
        var entry = GetSelectedEntry();
        if (entry is null)
        {
            return;
        }

        if (MessageBox.Show(this, $"Отключить драйвер?\r\n\r\n{entry.ServiceName}", "Offline драйверы", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            SetDriverStart(entry.ServiceName, 4);
            _statusLabel.Text = $"Драйвер отключён: {entry.ServiceName}.";
            RefreshDrivers();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Offline драйверы", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void DeleteSelectedDriverEntry()
    {
        var entry = GetSelectedEntry();
        if (entry is null)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                $"Удалить запись драйвера из offline-реестра?\r\n\r\n{entry.ServiceName}\r\n\r\nФайл драйвера не будет удалён.",
                "Offline драйверы",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            DeleteDriverServiceKey(entry.ServiceName);
            _statusLabel.Text = $"Запись драйвера удалена: {entry.ServiceName}.";
            RefreshDrivers();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Offline драйверы", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void DeleteSelectedDriverFile()
    {
        var entry = GetSelectedEntry();
        if (entry is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.ResolvedPath) || !File.Exists(entry.ResolvedPath))
        {
            MessageBox.Show(this, "Файл драйвера не найден.", "Offline драйверы", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(
                this,
                $"Удалить файл драйвера?\r\n\r\n{entry.ResolvedPath}\r\n\r\nОбычно безопаснее сначала отключить драйвер.",
                "Offline драйверы",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            File.SetAttributes(entry.ResolvedPath, FileAttributes.Normal);
            File.Delete(entry.ResolvedPath);
            _statusLabel.Text = $"Файл удалён: {entry.ResolvedPath}.";
            RefreshDrivers();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Offline драйверы", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SetDriverStart(string serviceName, int startValue)
    {
        using var hive = OfflineRegistryHiveMount.Load(GetSystemHivePath(), "IUnlocker_DRIVER_EDIT");
        var controlSet = GetCurrentControlSetName(hive.Root);
        using var service = hive.Root.OpenSubKey($@"{controlSet}\Services\{serviceName}", writable: true)
            ?? throw new InvalidOperationException("Запись драйвера не найдена.");
        service.SetValue("Start", startValue, RegistryValueKind.DWord);
    }

    private void DeleteDriverServiceKey(string serviceName)
    {
        using var hive = OfflineRegistryHiveMount.Load(GetSystemHivePath(), "IUnlocker_DRIVER_DELETE");
        var controlSet = GetCurrentControlSetName(hive.Root);
        using var services = hive.Root.OpenSubKey($@"{controlSet}\Services", writable: true)
            ?? throw new InvalidOperationException($@"Не удалось открыть {controlSet}\Services.");
        services.DeleteSubKeyTree(serviceName, throwOnMissingSubKey: false);
    }

    private OfflineDriverEntry? GetSelectedEntry()
    {
        return _drivers.SelectedItems.Count == 0
            ? null
            : _drivers.SelectedItems[0].Tag as OfflineDriverEntry;
    }

    private void UpdateButtons()
    {
        var hasSelection = GetSelectedEntry() is not null;
        _disableButton.Enabled = hasSelection;
        _deleteEntryButton.Enabled = hasSelection;
        _deleteFileButton.Enabled = hasSelection;
    }

    private string GetSystemHivePath()
    {
        if (string.IsNullOrWhiteSpace(_session.WindowsPath))
        {
            throw new InvalidOperationException("Windows не выбрана.");
        }

        return Path.Combine(_session.WindowsPath, "System32", "config", "SYSTEM");
    }

    private string ResolveDriverPath(string imagePath, string serviceName)
    {
        var path = (imagePath ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path))
        {
            path = $@"System32\drivers\{serviceName}.sys";
        }

        path = path.Replace("%SystemRoot%", _session.WindowsPath!, StringComparison.OrdinalIgnoreCase)
            .Replace(@"\SystemRoot", _session.WindowsPath!, StringComparison.OrdinalIgnoreCase)
            .Replace("SystemRoot", _session.WindowsPath!, StringComparison.OrdinalIgnoreCase)
            .TrimStart('\\');

        if (path.StartsWith(@"??\", StringComparison.OrdinalIgnoreCase))
        {
            path = path[3..];
        }

        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.Combine(_session.WindowsPath!, path);
    }

    private static string GetCurrentControlSetName(RegistryKey systemRoot)
    {
        using var select = systemRoot.OpenSubKey("Select", writable: false);
        var current = ReadDWord(select, "Current") ?? 1;
        var name = $"ControlSet{current:000}";
        if (systemRoot.OpenSubKey(name, writable: false) is { } key)
        {
            key.Dispose();
            return name;
        }

        return systemRoot.GetSubKeyNames()
            .Where(subKey => subKey.StartsWith("ControlSet", StringComparison.OrdinalIgnoreCase))
            .OrderBy(subKey => subKey, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? name;
    }

    private static int? ReadDWord(RegistryKey? key, string valueName)
    {
        return key?.GetValue(valueName) switch
        {
            int value => value,
            long value => (int)value,
            _ => null,
        };
    }

    private static string GetStartText(int start)
    {
        return start switch
        {
            0 => "Boot",
            1 => "System",
            2 => "Auto",
            3 => "Manual",
            4 => "Disabled",
            _ => start.ToString(),
        };
    }

    private static string GetDriverTypeText(int type)
    {
        return type switch
        {
            1 => "Kernel",
            2 => "File system",
            _ => type.ToString(),
        };
    }

    private sealed record OfflineDriverEntry(
        string ServiceName,
        string DisplayName,
        string ImagePath,
        string ResolvedPath,
        int TypeValue,
        string TypeText,
        int StartValue,
        string StartText);
}
