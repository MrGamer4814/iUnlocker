using Microsoft.Win32;

namespace IUnlocker;

public sealed class RestrictionsUnlockForm : Form
{
    private readonly AppSession _session;
    private readonly DataGridView _grid = new();
    private readonly TextBox _searchBox = new();
    private readonly Button _refreshButton = new();
    private readonly Button _unlockSelectedButton = new();
    private readonly Button _unlockActiveButton = new();
    private readonly CheckBox _activeOnlyBox = new();
    private readonly Label _statusLabel = new();

    private List<RestrictionRow> _rows = [];

    public RestrictionsUnlockForm(AppSession session)
    {
        _session = session;
        BuildInterface();
        Load += (_, _) => RefreshRestrictions();
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - разблокировка ограничений";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 560);
        ClientSize = new Size(1220, 700);
        UiTheme.ApplyForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 10),
        };

        _refreshButton.Text = "Сканирование";
        _refreshButton.AutoSize = true;
        _refreshButton.Click += (_, _) => RefreshRestrictions();
        UiTheme.StyleButton(_refreshButton, primary: true);

        _unlockSelectedButton.Text = "Снять выбранное";
        _unlockSelectedButton.AutoSize = true;
        _unlockSelectedButton.Click += (_, _) => UnlockSelectedRestriction();
        UiTheme.StyleButton(_unlockSelectedButton);

        _unlockActiveButton.Text = "Снять всё заблокированное";
        _unlockActiveButton.AutoSize = true;
        _unlockActiveButton.Click += (_, _) => UnlockActiveRestrictions();
        UiTheme.StyleButton(_unlockActiveButton);

        _activeOnlyBox.Text = "Только заблокированное";
        _activeOnlyBox.AutoSize = true;
        _activeOnlyBox.Checked = true;
        _activeOnlyBox.Margin = new Padding(16, 4, 0, 0);
        _activeOnlyBox.CheckedChanged += (_, _) => ApplyFilter();
        UiTheme.StyleCheckBox(_activeOnlyBox);

        _searchBox.Width = 320;
        _searchBox.PlaceholderText = "Поиск по названию, пользователю, пути, значению";
        _searchBox.Margin = new Padding(16, 0, 0, 0);
        _searchBox.TextChanged += (_, _) => ApplyFilter();
        UiTheme.StyleTextBox(_searchBox);

        toolbar.Controls.Add(_refreshButton);
        toolbar.Controls.Add(_unlockSelectedButton);
        toolbar.Controls.Add(_unlockActiveButton);
        toolbar.Controls.Add(_activeOnlyBox);
        toolbar.Controls.Add(_searchBox);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AllowUserToOrderColumns = true;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoGenerateColumns = false;
        UiTheme.StyleGrid(_grid);
        _grid.CellFormatting += GridCellFormatting;
        _grid.SelectionChanged += (_, _) => UpdateButtons();

        AddColumn(nameof(RestrictionRow.Name), "Ограничение", 220);
        AddColumn(nameof(RestrictionRow.Group), "Группа", 150);
        AddColumn(nameof(RestrictionRow.ValueText), "Значение", 130);
        AddColumn(nameof(RestrictionRow.Path), "Путь", 720);

        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(0, 10, 0, 0);
        _statusLabel.ForeColor = UiTheme.MutedText;

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(_statusLabel, 0, 2);
        Controls.Add(root);
        UpdateButtons();
    }

    private void AddColumn(string property, string header, int width)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            Name = property,
            Width = width,
            SortMode = DataGridViewColumnSortMode.Automatic,
        });
    }

    private void RefreshRestrictions()
    {
        Cursor = Cursors.WaitCursor;
        _refreshButton.Enabled = false;

        try
        {
            _rows = ScanRestrictions();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Разблокировка ограничений", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _refreshButton.Enabled = true;
            Cursor = Cursors.Default;
            UpdateButtons();
        }
    }

    private List<RestrictionRow> ScanRestrictions()
    {
        var rows = new List<RestrictionRow>();
        var warnings = new List<string>();

        if (_session.IsWinPe && _session.WindowsPath is not null && !_session.DriveRoot.StartsWith("X:", StringComparison.OrdinalIgnoreCase))
        {
            AddOfflineMachineRows(rows, warnings);
            AddOfflineUserRows(rows, warnings);
        }
        else
        {
            AddLiveMachineRows(rows, warnings);
            AddLiveUserRows(rows, warnings);
        }

        if (warnings.Count > 0)
        {
            _statusLabel.Text = $"Предупреждения: {string.Join(" | ", warnings.Take(3))}";
        }

        return rows
            .GroupBy(row => row.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(row => row.IsActive)
            .ThenBy(row => row.Group, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Scope, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void AddLiveMachineRows(List<RestrictionRow> rows, List<string> warnings)
    {
        foreach (var definition in RestrictionDefinitions.Where(item => item.Target == RestrictionTarget.Machine))
        {
            var keyPath = $@"SOFTWARE\{definition.KeyPath}";
            AddLiveRow(rows, warnings, definition, RegistryHive.LocalMachine, keyPath, "Компьютер", $@"HKEY_LOCAL_MACHINE\{keyPath}");
        }

        AddLivePolicyScan(rows, warnings, RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies", "Компьютер", @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies");
        AddLivePolicyScan(rows, warnings, RegistryHive.LocalMachine, @"SOFTWARE\Policies", "Компьютер", @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies");
        AddLiveIfeoDebuggerRows(rows, warnings);
    }

    private void AddLiveUserRows(List<RestrictionRow> rows, List<string> warnings)
    {
        foreach (var definition in RestrictionDefinitions.Where(item => item.Target == RestrictionTarget.User))
        {
            AddLiveRow(rows, warnings, definition, RegistryHive.CurrentUser, definition.KeyPath, "Текущий пользователь", $@"HKEY_CURRENT_USER\{definition.KeyPath}");
        }

        AddLivePolicyScan(rows, warnings, RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Policies", "Текущий пользователь", @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Policies");
        AddLivePolicyScan(rows, warnings, RegistryHive.CurrentUser, @"Software\Policies", "Текущий пользователь", @"HKEY_CURRENT_USER\Software\Policies");
    }

    private static void AddLivePolicyScan(
        List<RestrictionRow> rows,
        List<string> warnings,
        RegistryHive hive,
        string keyPath,
        string scope,
        string displayPath)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(keyPath, writable: false);
            if (key is null)
            {
                return;
            }

            AddDynamicPolicyRows(rows, key, scope, displayPath, (path, valueName) => new RestrictionLocation(hive, path, valueName, null), keyPath, depth: 0);
        }
        catch (Exception ex)
        {
            warnings.Add($"{displayPath}: {ex.Message}");
        }
    }

    private static void AddLiveRow(
        List<RestrictionRow> rows,
        List<string> warnings,
        RestrictionDefinition definition,
        RegistryHive hive,
        string keyPath,
        string scope,
        string displayPath)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(keyPath, writable: false);
            var value = key?.GetValue(definition.ValueName);
            rows.Add(RestrictionRow.FromDefinition(
                definition,
                scope,
                $@"{displayPath}\{definition.ValueName}",
                value,
                new RestrictionLocation(hive, keyPath, definition.ValueName, null)));
        }
        catch (Exception ex)
        {
            warnings.Add($"{displayPath}: {ex.Message}");
        }
    }

    private void AddOfflineMachineRows(List<RestrictionRow> rows, List<string> warnings)
    {
        if (_session.WindowsPath is null)
        {
            return;
        }

        var hiveFile = Path.Combine(_session.WindowsPath, "System32", "config", "SOFTWARE");
        AddOfflineHiveRows(
            rows,
            warnings,
            hiveFile,
            "Компьютер",
            "HKEY_LOCAL_MACHINE",
            RestrictionTarget.Machine);
    }

    private void AddOfflineUserRows(List<RestrictionRow> rows, List<string> warnings)
    {
        if (_session.WindowsPath is null)
        {
            return;
        }

        AddOfflineHiveRows(
            rows,
            warnings,
            Path.Combine(_session.WindowsPath, "System32", "config", "DEFAULT"),
            "DEFAULT",
            @"HKEY_USERS\DEFAULT",
            RestrictionTarget.User);

        var usersRoot = Path.Combine(_session.DriveRoot, "Users");
        if (!Directory.Exists(usersRoot))
        {
            return;
        }

        foreach (var profile in Directory.EnumerateDirectories(usersRoot).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
        {
            var hiveFile = Path.Combine(profile, "NTUSER.DAT");
            if (!File.Exists(hiveFile))
            {
                continue;
            }

            var userName = Path.GetFileName(profile);
            AddOfflineHiveRows(rows, warnings, hiveFile, userName, $@"HKEY_USERS\{userName}", RestrictionTarget.User);
        }
    }

    private static void AddOfflineHiveRows(
        List<RestrictionRow> rows,
        List<string> warnings,
        string hiveFile,
        string scope,
        string displayRoot,
        RestrictionTarget target)
    {
        if (!File.Exists(hiveFile))
        {
            warnings.Add($"Hive не найден: {hiveFile}");
            return;
        }

        try
        {
            using var hive = OfflineRegistryHiveMount.Load(hiveFile, $"IUnlocker_RESTRICT_{SanitizeHiveName(scope)}");
            foreach (var definition in RestrictionDefinitions.Where(item => item.Target == target))
            {
                using var key = hive.Root.OpenSubKey(definition.KeyPath, writable: false);
                var value = key?.GetValue(definition.ValueName);
                rows.Add(RestrictionRow.FromDefinition(
                    definition,
                    scope,
                    $@"{displayRoot}\{definition.KeyPath}\{definition.ValueName}",
                    value,
                    new RestrictionLocation(null, definition.KeyPath, definition.ValueName, hiveFile)));
            }

            var policyRoots = target == RestrictionTarget.Machine
                ? new[] { "Microsoft\\Windows\\CurrentVersion\\Policies", "Policies" }
                : ["Software\\Microsoft\\Windows\\CurrentVersion\\Policies", "Software\\Policies"];

            foreach (var policyRoot in policyRoots)
            {
                using var key = hive.Root.OpenSubKey(policyRoot, writable: false);
                if (key is null)
                {
                    continue;
                }

                AddDynamicPolicyRows(
                    rows,
                    key,
                    scope,
                    $@"{displayRoot}\{policyRoot}",
                    (path, valueName) => new RestrictionLocation(null, path, valueName, hiveFile),
                    policyRoot,
                    depth: 0);
            }

            if (target == RestrictionTarget.Machine)
            {
                AddOfflineIfeoDebuggerRows(rows, hive.Root, scope, displayRoot, hiveFile);
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"{hiveFile}: {ex.Message}");
        }
    }

    private static void AddLiveIfeoDebuggerRows(List<RestrictionRow> rows, List<string> warnings)
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
        const string displayRoot = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            using var ifeoRoot = baseKey.OpenSubKey(keyPath, writable: false);
            if (ifeoRoot is null)
            {
                return;
            }

            AddIfeoDebuggerRows(
                rows,
                ifeoRoot,
                "Компьютер",
                displayRoot,
                (appName, valueName) => new RestrictionLocation(RegistryHive.LocalMachine, $@"{keyPath}\{appName}", valueName, null));
        }
        catch (Exception ex)
        {
            warnings.Add($"{displayRoot}: {ex.Message}");
        }
    }

    private static void AddOfflineIfeoDebuggerRows(
        List<RestrictionRow> rows,
        RegistryKey softwareRoot,
        string scope,
        string displayRoot,
        string hiveFile)
    {
        const string keyPath = @"Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
        using var ifeoRoot = softwareRoot.OpenSubKey(keyPath, writable: false);
        if (ifeoRoot is null)
        {
            return;
        }

        AddIfeoDebuggerRows(
            rows,
            ifeoRoot,
            scope,
            $@"{displayRoot}\{keyPath}",
            (appName, valueName) => new RestrictionLocation(null, $@"{keyPath}\{appName}", valueName, hiveFile));
    }

    private static void AddIfeoDebuggerRows(
        List<RestrictionRow> rows,
        RegistryKey ifeoRoot,
        string scope,
        string displayRoot,
        Func<string, string, RestrictionLocation> createLocation)
    {
        string[] appNames;
        try
        {
            appNames = ifeoRoot.GetSubKeyNames();
        }
        catch
        {
            return;
        }

        foreach (var appName in appNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            using var appKey = ifeoRoot.OpenSubKey(appName, writable: false);
            var debugger = appKey?.GetValue("Debugger");
            if (!IsRestrictiveValue(debugger))
            {
                continue;
            }

            rows.Add(RestrictionRow.FromDynamic(
                "IFEO",
                $"Debugger: {appName}",
                scope,
                $@"{displayRoot}\{appName}\Debugger",
                debugger,
                createLocation(appName, "Debugger")));
        }
    }

    private static void AddDynamicPolicyRows(
        List<RestrictionRow> rows,
        RegistryKey key,
        string scope,
        string displayPath,
        Func<string, string, RestrictionLocation> createLocation,
        string keyPath,
        int depth)
    {
        if (depth > 14)
        {
            return;
        }

        string[] valueNames;
        try
        {
            valueNames = key.GetValueNames();
        }
        catch
        {
            valueNames = [];
        }

        foreach (var valueName in valueNames)
        {
            object? value;
            try
            {
                value = key.GetValue(valueName);
            }
            catch
            {
                continue;
            }

            if (!IsRestrictiveValue(value))
            {
                continue;
            }

            var displayName = string.IsNullOrEmpty(valueName) ? "(по умолчанию)" : valueName;
            var group = DetermineDynamicGroup(keyPath, displayName);
            if (group == "Policy" || group == "Защита" || !IsKnownDynamicRestriction(displayName))
            {
                continue;
            }

            rows.Add(RestrictionRow.FromDynamic(
                group,
                BuildDynamicName(keyPath, displayName),
                scope,
                $@"{displayPath}\{displayName}",
                value,
                createLocation(keyPath, valueName)));
        }

        string[] subKeyNames;
        try
        {
            subKeyNames = key.GetSubKeyNames();
        }
        catch
        {
            return;
        }

        foreach (var subKeyName in subKeyNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            using var child = key.OpenSubKey(subKeyName, writable: false);
            if (child is null)
            {
                continue;
            }

            AddDynamicPolicyRows(
                rows,
                child,
                scope,
                $@"{displayPath}\{subKeyName}",
                createLocation,
                $@"{keyPath}\{subKeyName}",
                depth + 1);
        }
    }

    private void ApplyFilter()
    {
        var query = _searchBox.Text.Trim();
        IEnumerable<RestrictionRow> visibleRows = _activeOnlyBox.Checked
            ? _rows.Where(row => row.IsActive)
            : _rows;

        if (!string.IsNullOrWhiteSpace(query))
        {
            visibleRows = visibleRows.Where(row => row.Matches(query));
        }

        var visibleList = visibleRows.ToList();
        _grid.DataSource = visibleList;
        var activeCount = _rows.Count(row => row.IsActive);
        var foundCount = _rows.Count(row => row.HasValue);
        _statusLabel.Text = $"Показано: {visibleList.Count} из {_rows.Count}. Найдено значений: {foundCount}. Заблокировано: {activeCount}.";
        UpdateButtons();
    }

    private RestrictionRow? GetSelectedRow()
    {
        return _grid.CurrentRow?.DataBoundItem as RestrictionRow;
    }

    private List<RestrictionRow> GetSelectedRows()
    {
        return _grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.DataBoundItem)
            .OfType<RestrictionRow>()
            .DistinctBy(row => row.Path)
            .ToList();
    }

    private void UnlockSelectedRestriction()
    {
        var rows = GetSelectedRows().Where(row => row.IsActive).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var preview = string.Join(Environment.NewLine, rows.Take(8).Select(row => $"{row.Name} - {row.Path}"));
        if (MessageBox.Show(this, $"Снять выбранные ограничения? Значений: {rows.Count}.\r\n\r\n{preview}", "Разблокировка", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        UnlockRows(rows);
    }

    private void UnlockActiveRestrictions()
    {
        var rows = _rows.Where(row => row.IsActive).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        if (MessageBox.Show(this, $"Снять все активные ограничения? Значений: {rows.Count}.", "Разблокировка", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        UnlockRows(rows);
    }

    private void UnlockRows(List<RestrictionRow> rows)
    {
        var errors = new List<string>();

        foreach (var row in rows)
        {
            try
            {
                DeleteRestrictionValue(row.Location);
            }
            catch (Exception ex)
            {
                errors.Add($"{row.Name}: {ex.Message}");
            }
        }

        RefreshRestrictions();

        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors.Take(8)), "Не всё удалось снять", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void DeleteRestrictionValue(RestrictionLocation location)
    {
        if (!string.IsNullOrWhiteSpace(location.HiveFile))
        {
            OfflineRegistryEditor.DeleteValue(location.HiveFile, "IUnlocker_RESTRICT_EDIT", location.KeyPath, location.ValueName);
            return;
        }

        if (location.LiveHive is null)
        {
            throw new InvalidOperationException("Нет live-hive для изменения.");
        }

        using var baseKey = RegistryKey.OpenBaseKey(location.LiveHive.Value, RegistryView.Default);
        using var key = baseKey.OpenSubKey(location.KeyPath, writable: true)
            ?? throw new InvalidOperationException("Ключ не найден или недоступен для записи.");
        key.DeleteValue(location.ValueName, throwOnMissingValue: false);
    }

    private void UpdateButtons()
    {
        _unlockSelectedButton.Enabled = GetSelectedRows().Any(row => row.IsActive);
        _unlockActiveButton.Enabled = _rows.Any(row => row.IsActive);
    }

    private static void GridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || grid.Rows[e.RowIndex].DataBoundItem is not RestrictionRow row)
        {
            return;
        }

        if (row.IsActive)
        {
            grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.FromArgb(255, 224, 224);
            grid.Rows[e.RowIndex].DefaultCellStyle.ForeColor = Color.FromArgb(110, 0, 0);
        }
        else if (row.HasValue)
        {
            grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 220);
            grid.Rows[e.RowIndex].DefaultCellStyle.ForeColor = Color.FromArgb(90, 55, 0);
        }
    }

    private static bool IsRestrictiveValue(object? value)
    {
        return value switch
        {
            null => false,
            int number => number != 0,
            uint number => number != 0,
            long number => number != 0,
            string text => !string.IsNullOrWhiteSpace(text) && !text.Trim().Equals("0", StringComparison.OrdinalIgnoreCase),
            string[] values => values.Any(value => !string.IsNullOrWhiteSpace(value)),
            byte[] bytes => bytes.Any(value => value != 0),
            _ => true,
        };
    }

    private static string ValueToString(object? value)
    {
        return value switch
        {
            null => "",
            string text => text,
            string[] values => string.Join("; ", values),
            byte[] bytes => $"0x{Convert.ToHexString(bytes)}",
            _ => Convert.ToString(value) ?? "",
        };
    }

    private static string DetermineDynamicGroup(string keyPath, string valueName)
    {
        var text = $"{keyPath}\\{valueName}";
        if (text.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase))
        {
            return "Защита";
        }

        if (text.Contains(@"\Explorer", StringComparison.OrdinalIgnoreCase))
        {
            return "Проводник";
        }

        if (text.Contains(@"\System", StringComparison.OrdinalIgnoreCase))
        {
            return "Система";
        }

        if (text.Contains("WindowsUpdate", StringComparison.OrdinalIgnoreCase))
        {
            return "Обновления";
        }

        if (text.Contains("Installer", StringComparison.OrdinalIgnoreCase))
        {
            return "Установщик";
        }

        if (text.Contains(@"\MMC", StringComparison.OrdinalIgnoreCase))
        {
            return "MMC";
        }

        return "Policy";
    }

    private static string BuildDynamicName(string keyPath, string valueName)
    {
        var known = RestrictionDefinitions.FirstOrDefault(definition =>
            keyPath.EndsWith(definition.KeyPath, StringComparison.OrdinalIgnoreCase) &&
            definition.ValueName.Equals(valueName, StringComparison.OrdinalIgnoreCase));
        if (known is not null)
        {
            return known.Name;
        }

        return valueName switch
        {
            "RestrictRun" => "Разрешены только указанные программы",
            "DisallowRun" => "Запрещённые программы",
            "NoChangingWallPaper" => "Смена обоев",
            "NoDispSettingsPage" => "Параметры дисплея",
            "NoSecurityTab" => "Вкладка Безопасность",
            "NoCommonGroups" => "Общие группы меню Пуск",
            "StartMenuLogOff" => "Выход из системы в меню Пуск",
            "DenyUsersFromMachGP" => "Обновление политики компьютера пользователями",
            "HidePowerOptions" => "Кнопки питания",
            "DisableContextMenusInStart" => "Контекстные меню в меню Пуск",
            "DisableSR" => "Восстановление системы",
            "DisableConfig" => "Настройка восстановления системы",
            "NoViewContextMenu" => "Контекстное меню",
            "NoFolderOptions" => "Параметры папок",
            "DisableTaskMgr" => "Диспетчер задач",
            "DisableRegistryTools" => "Редактор реестра",
            "DisableCMD" => "Командная строка",
            _ => valueName,
        };
    }

    private static bool IsKnownDynamicRestriction(string valueName)
    {
        return DynamicRestrictionNames.Contains(valueName);
    }

    private static string SanitizeHiveName(string name)
    {
        var chars = name.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray();
        return new string(chars).Trim('_');
    }

    private static readonly RestrictionDefinition[] RestrictionDefinitions =
    [
        new("Система", "Диспетчер задач", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr"),
        new("Система", "Редактор реестра", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableRegistryTools"),
        new("Система", "Командная строка", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableCMD"),
        new("Система", "Блокировка компьютера", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableLockWorkstation"),
        new("Система", "Смена пароля", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableChangePassword"),
        new("Система", "Панель управления экраном", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "NoDispCPL"),
        new("Проводник", "Панель управления", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoControlPanel"),
        new("Проводник", "Окно Выполнить", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoRun"),
        new("Проводник", "Поиск", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoFind"),
        new("Проводник", "Параметры папок", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoFolderOptions"),
        new("Проводник", "Вкладка Безопасность", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoSecurityTab"),
        new("Проводник", "Контекстное меню", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoViewContextMenu"),
        new("Проводник", "Контекстное меню панели задач", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoTrayContextMenu"),
        new("Проводник", "Рабочий стол", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDesktop"),
        new("Проводник", "Скрытые диски", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDrives"),
        new("Проводник", "Запрет доступа к дискам", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoViewOnDrive"),
        new("Проводник", "Настройки панели задач", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoSetTaskbar"),
        new("Проводник", "Выключение/завершение работы", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoClose"),
        new("Проводник", "Выход из системы", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoLogoff"),
        new("Проводник", "Выход из системы в меню Пуск", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "StartMenuLogOff"),
        new("Проводник", "Общие разделы меню Пуск", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoCommonGroups"),
        new("Проводник", "Клавиши Windows", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoWinKeys"),
        new("Проводник", "Меню Файл", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoFileMenu"),
        new("Проводник", "Недавние документы", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoRecentDocsMenu"),
        new("Проводник", "Установка принтера", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoAddPrinter"),
        new("Проводник", "Удаление принтера", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDeletePrinter"),
        new("Проводник", "Смена обоев", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop", "NoChangingWallPaper"),
        new("Проводник", "Кнопки питания", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "HidePowerOptions"),
        new("Проводник", "Контекстные меню в меню Пуск", RestrictionTarget.User, @"Software\Policies\Microsoft\Windows\Explorer", "DisableContextMenusInStart"),
        new("Программы", "Установка и удаление программ", RestrictionTarget.User, @"Software\Microsoft\Windows\CurrentVersion\Policies\Uninstall", "NoAddRemovePrograms"),
        new("MMC", "Запрет оснасток MMC", RestrictionTarget.User, @"Software\Policies\Microsoft\MMC", "RestrictToPermittedSnapins"),
        new("Установщик", "Windows Installer", RestrictionTarget.Machine, @"Policies\Microsoft\Windows\Installer", "DisableMSI"),
        new("Система", "Командная строка", RestrictionTarget.Machine, @"Policies\Microsoft\Windows\System", "DisableCMD"),
        new("Система", "Запрет применения политики компьютера пользователями", RestrictionTarget.Machine, @"Policies\Microsoft\Windows NT\MitigationOptions", "DenyUsersFromMachGP"),
        new("Проводник", "Кнопки питания", RestrictionTarget.Machine, @"Policies\Microsoft\Windows\Explorer", "HidePowerOptions"),
        new("Проводник", "Контекстные меню в меню Пуск", RestrictionTarget.Machine, @"Policies\Microsoft\Windows\Explorer", "DisableContextMenusInStart"),
        new("Восстановление", "Восстановление системы", RestrictionTarget.Machine, @"Microsoft\Windows NT\CurrentVersion\SystemRestore", "DisableSR"),
        new("Восстановление", "Настройка восстановления системы", RestrictionTarget.Machine, @"Microsoft\Windows NT\CurrentVersion\SystemRestore", "DisableConfig"),
        new("Восстановление", "Восстановление системы", RestrictionTarget.Machine, @"Policies\Microsoft\Windows NT\SystemRestore", "DisableSR"),
        new("Восстановление", "Настройка восстановления системы", RestrictionTarget.Machine, @"Policies\Microsoft\Windows NT\SystemRestore", "DisableConfig"),
        new("Обновления", "Доступ к Windows Update", RestrictionTarget.Machine, @"Policies\Microsoft\Windows\WindowsUpdate", "DisableWindowsUpdateAccess"),
    ];

    private static readonly HashSet<string> DynamicRestrictionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "DisableTaskMgr",
        "DisableRegistryTools",
        "DisableCMD",
        "RestrictToPermittedSnapins",
        "NoControlPanel",
        "NoRun",
        "NoViewOnDrive",
        "NoDrives",
        "NoFind",
        "NoViewContextMenu",
        "NoFolderOptions",
        "NoSecurityTab",
        "NoFileMenu",
        "NoClose",
        "NoCommonGroups",
        "StartMenuLogOff",
        "NoChangingWallPaper",
        "NoWinKeys",
        "NoSetTaskbar",
        "DisableLockWorkstation",
        "DisableChangePassword",
        "NoTrayContextMenu",
        "DenyUsersFromMachGP",
        "HidePowerOptions",
        "DisableContextMenusInStart",
        "DisableSR",
        "DisableConfig",
        "NoLogoff",
        "RestrictRun",
        "DisallowRun",
        "NoDesktop",
        "NoDispCPL",
        "NoDispSettingsPage",
        "NoAddRemovePrograms",
        "DisableMSI",
        "DisableWindowsUpdateAccess",
    };

    private sealed record RestrictionDefinition(string Group, string Name, RestrictionTarget Target, string KeyPath, string ValueName);

    private sealed record RestrictionLocation(RegistryHive? LiveHive, string KeyPath, string ValueName, string? HiveFile);

    private enum RestrictionTarget
    {
        Machine,
        User,
    }

    private sealed class RestrictionRow
    {
        public string Group { get; private init; } = "";

        public string Name { get; private init; } = "";

        public string Scope { get; private init; } = "";

        public string Status { get; private init; } = "";

        public string ValueText { get; private init; } = "";

        public string Path { get; private init; } = "";

        public bool HasValue { get; private init; }

        public bool IsActive { get; private init; }

        public RestrictionLocation Location { get; private init; } = new(null, "", "", null);

        public static RestrictionRow FromDefinition(
            RestrictionDefinition definition,
            string scope,
            string path,
            object? value,
            RestrictionLocation location)
        {
            var hasValue = value is not null;
            var active = IsRestrictiveValue(value);
            return new RestrictionRow
            {
                Group = definition.Group,
                Name = definition.Name,
                Scope = scope,
                Status = active ? "Ограничение активно" : hasValue ? "Значение есть" : "Не найдено",
                ValueText = ValueToString(value),
                Path = path,
                HasValue = hasValue,
                IsActive = active,
                Location = location,
            };
        }

        public static RestrictionRow FromDynamic(
            string group,
            string name,
            string scope,
            string path,
            object? value,
            RestrictionLocation location)
        {
            var hasValue = value is not null;
            var active = IsRestrictiveValue(value);
            return new RestrictionRow
            {
                Group = group,
                Name = name,
                Scope = scope,
                Status = active ? "Заблокировано" : hasValue ? "Значение есть" : "Не найдено",
                ValueText = ValueToString(value),
                Path = path,
                HasValue = hasValue,
                IsActive = active,
                Location = location,
            };
        }

        public bool Matches(string query)
        {
            return Group.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                   Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                   Scope.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                   Status.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                   ValueText.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                   Path.Contains(query, StringComparison.CurrentCultureIgnoreCase);
        }
    }
}
