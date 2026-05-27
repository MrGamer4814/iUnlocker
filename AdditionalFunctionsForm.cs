namespace IUnlocker;

using System.Diagnostics;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;

public sealed class AdditionalFunctionsForm : Form
{
    private readonly AppSession _session;
    private readonly Button _replaceAccessibilityButton = new();
    private readonly Button _setCmdLineButton = new();
    private readonly Button _rescueLogonButton = new();
    private readonly Button _offlineDriversButton = new();
    private readonly Button _restoreLogonUiButton = new();
    private readonly Button _restoreFontsButton = new();
    private readonly Button _cleanTempButton = new();
    private readonly Button _sfcScanButton = new();
    private readonly Button _dismCheckHealthButton = new();
    private readonly Button _dismScanHealthButton = new();
    private readonly Button _dismRestoreHealthButton = new();
    private readonly Button _chkdskButton = new();
    private readonly Button _bootCheckButton = new();
    private readonly Button _bcdCheckButton = new();
    private readonly Button _exportReportButton = new();
    private readonly Button _resetSecurityPolicyButton = new();
    private readonly Button _disableTestModeButton = new();
    private readonly Button _verifySignaturesButton = new();
    private readonly Button _enableUacButton = new();
    private readonly Button _restartButton = new();
    private readonly Button _restartSafeModeButton = new();
    private readonly Button _shutdownButton = new();
    private readonly CheckBox _safeModeCheckBox = new();
    private readonly Label _statusLabel = new();
    private bool _cmdLineInstalled;
    private bool _accessibilityToolsReplaced;
    private bool _syncingSafeModeCheckBox;

    public AdditionalFunctionsForm(AppSession session)
    {
        _session = session;
        BuildInterface();
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - дополнительные функции";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 620);
        ClientSize = new Size(880, 760);
        UiTheme.ApplyForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(24),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "Дополнительные функции",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        };
        UiTheme.StyleTitle(title, 22F);

        var info = new Label
        {
            Text = GetInfoText(),
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 74,
            Margin = new Padding(0, 0, 0, 18),
        };
        UiTheme.StyleInfo(info);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(12),
        };
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            HotTrack = true,
        };

        ConfigureActionButton(
            _replaceAccessibilityButton,
            "Заменить sethc.exe и utilman.exe на iUnlocker",
            (_, _) => ReplaceAccessibilityTools());
        ConfigureActionButton(
            _setCmdLineButton,
            "Поставить запуск в CmdLine",
            (_, _) => SetSetupCmdLine());
        ConfigureActionButton(
            _rescueLogonButton,
            "Спасти вход Windows",
            (_, _) => RescueWindowsLogon());
        ConfigureActionButton(
            _offlineDriversButton,
            "Удаление драйвера offline",
            (_, _) => OpenOfflineDriverManager());
        ConfigureActionButton(
            _restoreLogonUiButton,
            "Восстановить LogonUI",
            (_, _) => RestoreLogonUi());
        ConfigureActionButton(
            _restoreFontsButton,
            "Восстановить все шрифты",
            (_, _) => RestoreAllFonts());
        ConfigureActionButton(
            _cleanTempButton,
            "Очистка TEMP",
            (_, _) => CleanTemp());
        ConfigureActionButton(
            _sfcScanButton,
            "SFC /scannow выбранной Windows",
            (_, _) => RunSfcScan());
        ConfigureActionButton(
            _dismCheckHealthButton,
            "DISM CheckHealth",
            (_, _) => RunDismHealth("CheckHealth"));
        ConfigureActionButton(
            _dismScanHealthButton,
            "DISM ScanHealth",
            (_, _) => RunDismHealth("ScanHealth"));
        ConfigureActionButton(
            _dismRestoreHealthButton,
            "DISM RestoreHealth",
            (_, _) => RunDismHealth("RestoreHealth"));
        ConfigureActionButton(
            _chkdskButton,
            "CHKDSK выбранного диска",
            (_, _) => RunChkdsk());
        ConfigureActionButton(
            _bootCheckButton,
            "Проверка загрузчика выбранной Windows",
            (_, _) => RunBootloaderCheck());
        ConfigureActionButton(
            _bcdCheckButton,
            "Проверка BCD",
            (_, _) => OpenBcdCheck());
        ConfigureActionButton(
            _exportReportButton,
            "Экспорт отчёта",
            (_, _) => ExportOfflineReport());
        ConfigureActionButton(
            _resetSecurityPolicyButton,
            "Сброс политик безопасности",
            (_, _) => ResetSecurityPolicy());
        ConfigureActionButton(
            _disableTestModeButton,
            "Отключить тестовый режим",
            (_, _) => DisableTestMode());
        ConfigureActionButton(
            _verifySignaturesButton,
            "Проверка подписей",
            (_, _) => OpenSignatureCheck());
        ConfigureActionButton(
            _enableUacButton,
            "Включить UAC",
            (_, _) => EnableUac());
        ConfigureActionButton(
            _restartButton,
            "Перезагрузка компьютера",
            (_, _) => RestartComputer());
        ConfigureActionButton(
            _restartSafeModeButton,
            "Перезагрузиться в безопасный режим",
            (_, _) => RestartToSafeMode());
        ConfigureActionButton(
            _shutdownButton,
            "Выключение компьютера",
            (_, _) => ShutdownComputer());
        ConfigureSafeModeCheckBox();

        var offlineWindows = IsOfflineWindowsSelected();
        _replaceAccessibilityButton.Enabled = offlineWindows;
        _offlineDriversButton.Enabled = offlineWindows;
        _setCmdLineButton.Enabled = CanSetCmdLine();
        _bootCheckButton.Enabled = _session.IsWinPe;
        _resetSecurityPolicyButton.Enabled = IsCurrentWindowsSelected();
        UpdateAccessibilityButtonState();
        UpdateCmdLineButtonState();
        UpdateSafeModeState();

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.AutoSize = false;
        _statusLabel.ForeColor = UiTheme.MutedText;
        _statusLabel.Padding = new Padding(2, 10, 2, 0);
        _statusLabel.Text = offlineWindows
            ? "WinPE-режим: доступны операции с выбранной offline-Windows."
            : "CmdLine доступен в текущей Windows. Замена sethc/utilman доступна только в WinPE.";

        var recoveryPage = CreateActionPage();
        AddActionButton(recoveryPage, _restoreLogonUiButton, 0, 0);
        AddActionButton(recoveryPage, _sfcScanButton, 1, 0);
        AddActionButton(recoveryPage, _dismCheckHealthButton, 0, 1);
        AddActionButton(recoveryPage, _dismScanHealthButton, 1, 1);
        AddActionButton(recoveryPage, _dismRestoreHealthButton, 0, 2, columnSpan: 2);
        AddActionButton(recoveryPage, _chkdskButton, 0, 3, columnSpan: 2);
        AddActionButton(recoveryPage, _restoreFontsButton, 0, 4, columnSpan: 2);
        AddTab(tabs, "Восстановление", recoveryPage);

        var securityPage = CreateActionPage();
        AddActionButton(securityPage, _enableUacButton, 0, 0);
        AddActionButton(securityPage, _resetSecurityPolicyButton, 1, 0);
        AddActionButton(securityPage, _disableTestModeButton, 0, 1);
        AddActionButton(securityPage, _verifySignaturesButton, 1, 1);
        AddActionButton(securityPage, _bootCheckButton, 0, 2, columnSpan: 2);
        AddActionButton(securityPage, _bcdCheckButton, 0, 3);
        AddActionButton(securityPage, _exportReportButton, 1, 3);
        AddTab(tabs, "Безопасность", securityPage);

        var accessPage = CreateActionPage();
        AddActionButton(accessPage, _replaceAccessibilityButton, 0, 0);
        AddActionButton(accessPage, _setCmdLineButton, 1, 0);
        AddActionButton(accessPage, _rescueLogonButton, 0, 1, columnSpan: 2);
        AddActionButton(accessPage, _offlineDriversButton, 0, 2);
        AddActionButton(accessPage, _cleanTempButton, 1, 2);
        AddTab(tabs, "Система", accessPage);

        var powerPage = CreateActionPage();
        AddActionButton(powerPage, _restartButton, 0, 0);
        AddActionButton(powerPage, _shutdownButton, 1, 0);
        AddActionButton(powerPage, _restartSafeModeButton, 0, 1, columnSpan: 2);
        AddActionControl(powerPage, _safeModeCheckBox, 0, 2, columnSpan: 2);
        AddTab(tabs, "Питание", powerPage);

        content.Controls.Add(tabs, 0, 0);
        content.Controls.Add(_statusLabel, 0, 1);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(info, 0, 1);
        root.Controls.Add(content, 0, 2);
        Controls.Add(root);
    }

    private static TableLayoutPanel CreateActionPage()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(12),
            BackColor = UiTheme.Surface,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var index = 0; index < 5; index++)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        }

        return panel;
    }

    private static void AddTab(TabControl tabs, string title, Control content)
    {
        var page = new TabPage(title)
        {
            BackColor = UiTheme.Surface,
        };
        page.Controls.Add(content);
        tabs.TabPages.Add(page);
    }

    private static void AddActionButton(TableLayoutPanel panel, Button button, int column, int row, int columnSpan = 1)
    {
        AddActionControl(panel, button, column, row, columnSpan);
    }

    private static void AddActionControl(TableLayoutPanel panel, Control control, int column, int row, int columnSpan = 1)
    {
        panel.Controls.Add(control, column, row);
        if (columnSpan > 1)
        {
            panel.SetColumnSpan(control, columnSpan);
        }
    }

    private static void ConfigureActionButton(Button button, string text, EventHandler onClick)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 0, 12, 12);
        button.Font = AppFonts.Create(10F);
        button.TextAlign = ContentAlignment.MiddleCenter;
        UiTheme.StyleButton(button);
        button.Click += onClick;
    }

    private void ConfigureSafeModeCheckBox()
    {
        _safeModeCheckBox.Text = "Безопасный режим";
        _safeModeCheckBox.Dock = DockStyle.Fill;
        _safeModeCheckBox.Margin = new Padding(4, 4, 12, 12);
        _safeModeCheckBox.TextAlign = ContentAlignment.MiddleLeft;
        _safeModeCheckBox.CheckAlign = ContentAlignment.MiddleLeft;
        _safeModeCheckBox.CheckedChanged += (_, _) => SafeModeCheckBoxChanged();
        UiTheme.StyleCheckBox(_safeModeCheckBox);
    }

    private bool IsOfflineWindowsSelected()
    {
        return _session.IsWinPe &&
               _session.WindowsPath is not null &&
               !_session.DriveRoot.StartsWith("X:", StringComparison.OrdinalIgnoreCase);
    }

    private bool CanSetCmdLine()
    {
        return IsOfflineWindowsSelected() || !_session.IsWinPe;
    }

    private void ReplaceAccessibilityTools()
    {
        if (!EnsureOfflineWindowsAction())
        {
            return;
        }

        if (_accessibilityToolsReplaced)
        {
            RestoreAccessibilityTools();
            return;
        }

        var result = MessageBox.Show(
            this,
            "Заменить sethc.exe и utilman.exe на текущий iUnlocker?\r\n\r\nПеред заменой будут созданы резервные копии *.iUnlocker.bak.",
            "Дополнительные функции",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var system32 = Path.Combine(_session.WindowsPath!, "System32");
            var source = Application.ExecutablePath;
            ReplaceSystemTool(source, Path.Combine(system32, "sethc.exe"));
            ReplaceSystemTool(source, Path.Combine(system32, "utilman.exe"));
            UpdateAccessibilityButtonState();
            SetStatus("sethc.exe и utilman.exe заменены. Резервные копии сохранены рядом с файлами.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Не удалось заменить файлы", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void ReplaceSystemTool(string sourceExe, string targetExe)
    {
        if (!File.Exists(sourceExe))
        {
            throw new FileNotFoundException("iUnlocker.exe не найден.", sourceExe);
        }

        if (!File.Exists(targetExe))
        {
            throw new FileNotFoundException("Системный файл не найден.", targetExe);
        }

        var backup = GetBackupPath(targetExe);
        if (!File.Exists(backup))
        {
            File.Copy(targetExe, backup, overwrite: false);
        }

        File.Copy(sourceExe, targetExe, overwrite: true);
    }

    private void RestoreAccessibilityTools()
    {
        var result = MessageBox.Show(
            this,
            "Восстановить sethc.exe и utilman.exe из резервных копий *.iUnlocker.bak?",
            "Дополнительные функции",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var system32 = Path.Combine(_session.WindowsPath!, "System32");
            RestoreSystemTool(Path.Combine(system32, "sethc.exe"));
            RestoreSystemTool(Path.Combine(system32, "utilman.exe"));
            UpdateAccessibilityButtonState();
            SetStatus("sethc.exe и utilman.exe восстановлены из резервных копий.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Не удалось восстановить файлы", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void RestoreSystemTool(string targetExe)
    {
        var backup = GetExistingBackupPath(targetExe);
        if (!File.Exists(backup))
        {
            throw new FileNotFoundException("Резервная копия не найдена.", backup);
        }

        File.Copy(backup, targetExe, overwrite: true);
    }

    private static string GetBackupPath(string targetExe)
    {
        return targetExe + ".iUnlocker.bak";
    }

    private static string? GetExistingBackupPath(string targetExe)
    {
        var newBackup = targetExe + ".iUnlocker.bak";
        if (File.Exists(newBackup))
        {
            return newBackup;
        }

        var oldBackup = targetExe + ".IUnlocker.bak";
        return File.Exists(oldBackup) ? oldBackup : null;
    }

    private void UpdateAccessibilityButtonState()
    {
        try
        {
            _accessibilityToolsReplaced = AreAccessibilityToolsReplaced();
            _replaceAccessibilityButton.Text = _accessibilityToolsReplaced
                ? "Восстановить sethc.exe и utilman.exe"
                : "Заменить sethc.exe и utilman.exe на iUnlocker";
        }
        catch
        {
            _accessibilityToolsReplaced = false;
            _replaceAccessibilityButton.Text = "Заменить sethc.exe и utilman.exe на iUnlocker";
        }
    }

    private bool AreAccessibilityToolsReplaced()
    {
        if (!IsOfflineWindowsSelected())
        {
            return false;
        }

        var system32 = Path.Combine(_session.WindowsPath!, "System32");
        return LooksLikeIUnlockerFile(Path.Combine(system32, "sethc.exe")) &&
               LooksLikeIUnlockerFile(Path.Combine(system32, "utilman.exe")) &&
               GetExistingBackupPath(Path.Combine(system32, "sethc.exe")) is not null &&
               GetExistingBackupPath(Path.Combine(system32, "utilman.exe")) is not null;
    }

    private static bool LooksLikeIUnlockerFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return string.Equals(info.ProductName, "iUnlocker", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(info.ProductName, "IUnlocker", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(info.FileDescription, "iUnlocker", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(info.FileDescription, "IUnlocker", StringComparison.OrdinalIgnoreCase) ||
                   Path.GetFileName(path).Equals("iUnlocker.exe", StringComparison.OrdinalIgnoreCase) ||
                   Path.GetFileName(path).Equals("IUnlocker.exe", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void SetSetupCmdLine()
    {
        if (!EnsureCmdLineAction())
        {
            return;
        }

        if (_cmdLineInstalled)
        {
            RemoveSetupCmdLine();
            return;
        }

        var result = MessageBox.Show(
            this,
            GetSetCmdLineConfirmationText(),
            "Дополнительные функции",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var system32 = IsOfflineWindowsSelected()
                ? Path.Combine(_session.WindowsPath!, "System32")
                : Environment.SystemDirectory;
            var targetExe = Path.Combine(system32, "iUnlocker.exe");
            File.Copy(Application.ExecutablePath, targetExe, overwrite: true);

            if (IsOfflineWindowsSelected())
            {
                var systemHive = Path.Combine(_session.WindowsPath!, "System32", "config", "SYSTEM");
                OfflineRegistryEditor.SetValue(
                    systemHive,
                    "IUnlocker_CMDLINE",
                    "Setup",
                    "CmdLine",
                    "iUnlocker.exe",
                    RegistryValueKind.String);
                OfflineRegistryEditor.SetValue(
                    systemHive,
                    "IUnlocker_CMDLINE",
                    "Setup",
                    "SetupType",
                    2,
                    RegistryValueKind.DWord);
            }
            else
            {
                using var setupKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\Setup", writable: true)
                    ?? throw new InvalidOperationException(@"Не удалось открыть HKLM\SYSTEM\Setup для записи.");
                setupKey.SetValue("CmdLine", targetExe, RegistryValueKind.String);
                setupKey.SetValue("SetupType", 2, RegistryValueKind.DWord);
            }

            UpdateCmdLineButtonState();
            SetStatus(IsOfflineWindowsSelected()
                ? "iUnlocker.exe скопирован в offline System32. CmdLine = iUnlocker.exe, SetupType=2."
                : $"iUnlocker.exe скопирован в System32. CmdLine = {targetExe}, SetupType=2.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Не удалось установить CmdLine", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private string GetSetCmdLineConfirmationText()
    {
        return IsOfflineWindowsSelected()
            ? "Скопировать iUnlocker в offline System32 и прописать запуск через SYSTEM\\Setup\\CmdLine?\r\n\r\nБудет установлено: CmdLine = iUnlocker.exe, SetupType = 2.\r\n\r\nВажно: без %SystemRoot%, чтобы Windows не зациклилась при запуске."
            : $"Скопировать iUnlocker в System32 и прописать запуск через SYSTEM\\Setup\\CmdLine?\r\n\r\nБудет установлено: CmdLine = {Path.Combine(Environment.SystemDirectory, "iUnlocker.exe")}, SetupType = 2.";
    }

    private void RemoveSetupCmdLine()
    {
        var result = MessageBox.Show(
            this,
            "Убрать запуск iUnlocker из SYSTEM\\Setup\\CmdLine?\r\n\r\nCmdLine будет очищен, SetupType будет установлен в 0.",
            "Дополнительные функции",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
        {
            return;
        }

        try
        {
            if (IsOfflineWindowsSelected())
            {
                var systemHive = Path.Combine(_session.WindowsPath!, "System32", "config", "SYSTEM");
                OfflineRegistryEditor.SetValue(systemHive, "IUnlocker_CMDLINE_REMOVE", "Setup", "CmdLine", string.Empty, RegistryValueKind.String);
                OfflineRegistryEditor.SetValue(systemHive, "IUnlocker_CMDLINE_REMOVE", "Setup", "SetupType", 0, RegistryValueKind.DWord);
            }
            else
            {
                using var setupKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\Setup", writable: true)
                    ?? throw new InvalidOperationException(@"Не удалось открыть HKLM\SYSTEM\Setup для записи.");
                setupKey.SetValue("CmdLine", string.Empty, RegistryValueKind.String);
                setupKey.SetValue("SetupType", 0, RegistryValueKind.DWord);
            }

            UpdateCmdLineButtonState();
            SetStatus("Запуск iUnlocker из CmdLine убран. SetupType установлен в 0.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Не удалось убрать CmdLine", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateCmdLineButtonState()
    {
        try
        {
            _cmdLineInstalled = IsIUnlockerCmdLineInstalled();
            _setCmdLineButton.Text = _cmdLineInstalled
                ? "Убрать запуск из CmdLine"
                : "Поставить запуск в CmdLine";
        }
        catch
        {
            _cmdLineInstalled = false;
            _setCmdLineButton.Text = "Поставить запуск в CmdLine";
        }
    }

    private bool IsIUnlockerCmdLineInstalled()
    {
        if (!CanSetCmdLine())
        {
            return false;
        }

        if (IsOfflineWindowsSelected())
        {
            var systemHive = Path.Combine(_session.WindowsPath!, "System32", "config", "SYSTEM");
            using var hive = OfflineRegistryHiveMount.Load(systemHive, "IUnlocker_CMDLINE_CHECK");
            using var setupKey = hive.Root.OpenSubKey("Setup", writable: false);
            return LooksLikeIUnlockerCmdLine(setupKey?.GetValue("CmdLine"));
        }

        using var liveSetupKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\Setup", writable: false);
        return LooksLikeIUnlockerCmdLine(liveSetupKey?.GetValue("CmdLine"));
    }

    private static bool LooksLikeIUnlockerCmdLine(object? value)
    {
        return value is string text &&
               (text.Contains("iUnlocker.exe", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("IUnlocker.exe", StringComparison.OrdinalIgnoreCase));
    }

    private bool EnsureCmdLineAction()
    {
        if (CanSetCmdLine())
        {
            return true;
        }

        MessageBox.Show(
            this,
            "В WinPE сначала выберите диск с установленной Windows.",
            "Недоступно",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        return false;
    }

    private bool EnsureOfflineWindowsAction()
    {
        if (IsOfflineWindowsSelected())
        {
            return true;
        }

        MessageBox.Show(
            this,
            "Эта функция доступна только в WinPE при выбранном диске с установленной Windows.",
            "Недоступно",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        return false;
    }

    private void OpenOfflineDriverManager()
    {
        if (!EnsureOfflineWindowsAction())
        {
            return;
        }

        var form = new OfflineDriverManagerForm(_session);
        form.Show(this);
    }

    private void RescueWindowsLogon()
    {
        if (string.IsNullOrWhiteSpace(_session.WindowsPath) || !Directory.Exists(_session.WindowsPath))
        {
            MessageBox.Show(this, "На выбранном диске не найдена папка Windows.", "Спасти вход Windows", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var message =
            "Применить режим спасения входа?\r\n\r\n" +
            "Будет восстановлено:\r\n" +
            "- Winlogon Shell = explorer.exe\r\n" +
            "- Winlogon Userinit = userinit.exe\r\n" +
            "- Setup CmdLine будет очищен\r\n" +
            "- Safe Mode будет отключён в BCD\r\n" +
            "- IFEO Debugger для logon/explorer компонентов будет удалён\r\n\r\n" +
            "После этого будет предложено запустить SFC для LogonUI.exe.";

        if (MessageBox.Show(this, message, "Спасти вход Windows", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var actions = new List<string>();
        var warnings = new List<string>();

        try
        {
            RestoreWinlogonDefaults();
            actions.Add("Winlogon восстановлен");
        }
        catch (Exception ex)
        {
            warnings.Add($"Winlogon: {ex.Message}");
        }

        try
        {
            ClearSetupCmdLine();
            actions.Add("Setup CmdLine очищен");
        }
        catch (Exception ex)
        {
            warnings.Add($"CmdLine: {ex.Message}");
        }

        try
        {
            ClearLogonIfeoDebuggers();
            actions.Add("IFEO Debugger удалён");
        }
        catch (Exception ex)
        {
            warnings.Add($"IFEO: {ex.Message}");
        }

        try
        {
            SetSafeModeEnabled(false);
            actions.Add("Safe Mode отключён");
            UpdateSafeModeState();
        }
        catch (Exception ex)
        {
            warnings.Add($"Safe Mode: {ex.Message}");
        }

        TryRestoreAccessibilityToolsForRescue(actions, warnings);
        SetStatus($"Спасение входа завершено. Успешно: {actions.Count}. Предупреждений: {warnings.Count}.");

        var summary = string.Join("\r\n", actions.Select(action => "- " + action));
        if (warnings.Count > 0)
        {
            summary += "\r\n\r\nПредупреждения:\r\n" + string.Join("\r\n", warnings.Select(warning => "- " + warning));
        }

        if (MessageBox.Show(
                this,
                $"{summary}\r\n\r\nЗапустить точечное восстановление LogonUI.exe через SFC?",
                "Спасти вход Windows",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes)
        {
            StartLogonUiSfc();
        }
    }

    private void RestoreWinlogonDefaults()
    {
        if (IsCurrentWindowsSelected())
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", writable: true)
                ?? throw new InvalidOperationException("Не удалось открыть Winlogon.");
            WriteWinlogonDefaults(key);
            return;
        }

        var softwareHive = Path.Combine(_session.WindowsPath!, "System32", "config", "SOFTWARE");
        using var hive = OfflineRegistryHiveMount.Load(softwareHive, "IUnlocker_RESCUE_SOFTWARE");
        using var offlineKey = hive.Root.CreateSubKey(@"Microsoft\Windows NT\CurrentVersion\Winlogon", writable: true)
            ?? throw new InvalidOperationException("Не удалось открыть offline Winlogon.");
        WriteWinlogonDefaults(offlineKey);
    }

    private void WriteWinlogonDefaults(RegistryKey key)
    {
        key.SetValue("Shell", "explorer.exe", RegistryValueKind.String);
        key.SetValue("Userinit", GetDefaultUserinitValue(), RegistryValueKind.String);
    }

    private string GetDefaultUserinitValue()
    {
        if (IsCurrentWindowsSelected())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "system32", "userinit.exe") + ",";
        }

        return @"C:\Windows\system32\userinit.exe,";
    }

    private void ClearSetupCmdLine()
    {
        if (IsOfflineWindowsSelected())
        {
            var systemHive = Path.Combine(_session.WindowsPath!, "System32", "config", "SYSTEM");
            using var hive = OfflineRegistryHiveMount.Load(systemHive, "IUnlocker_RESCUE_SYSTEM");
            using var setupKey = hive.Root.CreateSubKey("Setup", writable: true)
                ?? throw new InvalidOperationException("Не удалось открыть offline SYSTEM\\Setup.");
            WriteSetupDefaults(setupKey);
            return;
        }

        using var liveSetupKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\Setup", writable: true)
            ?? throw new InvalidOperationException(@"Не удалось открыть HKLM\SYSTEM\Setup.");
        WriteSetupDefaults(liveSetupKey);
    }

    private static void WriteSetupDefaults(RegistryKey setupKey)
    {
        setupKey.SetValue("CmdLine", string.Empty, RegistryValueKind.String);
        setupKey.SetValue("SetupType", 0, RegistryValueKind.DWord);
    }

    private void ClearLogonIfeoDebuggers()
    {
        if (IsCurrentWindowsSelected())
        {
            using var ifeo = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", writable: true)
                ?? throw new InvalidOperationException("Не удалось открыть IFEO.");
            ClearIfeoDebuggers(ifeo);
            return;
        }

        var softwareHive = Path.Combine(_session.WindowsPath!, "System32", "config", "SOFTWARE");
        using var hive = OfflineRegistryHiveMount.Load(softwareHive, "IUnlocker_RESCUE_IFEO");
        using var offlineIfeo = hive.Root.CreateSubKey(@"Microsoft\Windows NT\CurrentVersion\Image File Execution Options", writable: true)
            ?? throw new InvalidOperationException("Не удалось открыть offline IFEO.");
        ClearIfeoDebuggers(offlineIfeo);
    }

    private static void ClearIfeoDebuggers(RegistryKey ifeoRoot)
    {
        foreach (var imageName in new[] { "explorer.exe", "userinit.exe", "winlogon.exe", "logonui.exe", "sethc.exe", "utilman.exe" })
        {
            using var imageKey = ifeoRoot.OpenSubKey(imageName, writable: true);
            imageKey?.DeleteValue("Debugger", throwOnMissingValue: false);
        }
    }

    private void TryRestoreAccessibilityToolsForRescue(List<string> actions, List<string> warnings)
    {
        if (!IsOfflineWindowsSelected())
        {
            return;
        }

        try
        {
            var system32 = Path.Combine(_session.WindowsPath!, "System32");
            RestoreSystemToolIfBackupExists(Path.Combine(system32, "sethc.exe"));
            RestoreSystemToolIfBackupExists(Path.Combine(system32, "utilman.exe"));
            UpdateAccessibilityButtonState();
            actions.Add("sethc/utilman восстановлены при наличии backup");
        }
        catch (Exception ex)
        {
            warnings.Add($"sethc/utilman: {ex.Message}");
        }
    }

    private static void RestoreSystemToolIfBackupExists(string targetExe)
    {
        var backup = GetExistingBackupPath(targetExe);
        if (backup is not null && File.Exists(backup))
        {
            File.Copy(backup, targetExe, overwrite: true);
        }
    }

    private void StartLogonUiSfc()
    {
        var logonUiPath = Path.Combine(_session.WindowsPath!, "System32", "LogonUI.exe");
        var arguments = IsCurrentWindowsSelected()
            ? $"/scanfile={QuoteArgument(logonUiPath)}"
            : $"/scanfile={QuoteArgument(logonUiPath)} /offbootdir={QuoteArgument(EnsureTrailingSlash(_session.DriveRoot))} /offwindir={QuoteArgument(_session.WindowsPath!)}";
        StartVisibleCmdCommand("iUnlocker LogonUI", $"sfc.exe {arguments}");
        SetStatus("Точечное восстановление LogonUI запущено в окне cmd.exe.");
    }

    private void RunSfcScan()
    {
        if (string.IsNullOrWhiteSpace(_session.WindowsPath) || !Directory.Exists(_session.WindowsPath))
        {
            MessageBox.Show(
                this,
                "На выбранном диске не найдена папка Windows.",
                "SFC",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var isCurrentWindows = IsCurrentWindowsSelected();
        var arguments = isCurrentWindows
            ? "/scannow"
            : $"/scannow /offbootdir={QuoteArgument(EnsureTrailingSlash(_session.DriveRoot))} /offwindir={QuoteArgument(_session.WindowsPath)}";

        var confirmation = isCurrentWindows
            ? "Запустить SFC /scannow для текущей Windows?\r\n\r\nЭто может занять много времени."
            : $"Запустить offline SFC для выбранной Windows?\r\n\r\nBoot: {_session.DriveRoot}\r\nWindows: {_session.WindowsPath}\r\n\r\nЭто может занять много времени.";
        if (MessageBox.Show(this, confirmation, "SFC", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            StartVisibleCmdCommand("iUnlocker SFC", $"sfc.exe {arguments}");
            SetStatus($"SFC запущен в окне cmd.exe: sfc.exe {arguments}");
        }
        catch (Exception ex)
        {
            SetStatus($"SFC не запущен: {ex.Message}");
            MessageBox.Show(this, ex.Message, "SFC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RunDismHealth(string mode)
    {
        if (string.IsNullOrWhiteSpace(_session.WindowsPath) || !Directory.Exists(_session.WindowsPath))
        {
            MessageBox.Show(
                this,
                "На выбранном диске не найдена папка Windows.",
                "DISM",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var arguments = IsCurrentWindowsSelected()
            ? $"/Online /Cleanup-Image /{mode}"
            : $"/Image:{EnsureTrailingSlash(_session.DriveRoot)} /Cleanup-Image /{mode}";

        var confirmation = IsCurrentWindowsSelected()
            ? $"Запустить DISM {mode} для текущей Windows?"
            : $"Запустить DISM {mode} для выбранной Windows?\r\n\r\nImage: {_session.DriveRoot}";
        if (MessageBox.Show(this, confirmation, "DISM", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            StartVisibleCmdCommand($"iUnlocker DISM {mode}", $"dism.exe {arguments}");
            SetStatus($"DISM {mode} запущен в окне cmd.exe.");
        }
        catch (Exception ex)
        {
            SetStatus($"DISM {mode} не запущен: {ex.Message}");
            MessageBox.Show(this, ex.Message, "DISM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RunChkdsk()
    {
        var drive = GetDriveLetterArgument(_session.DriveRoot);
        if (string.IsNullOrWhiteSpace(drive))
        {
            MessageBox.Show(this, "Не удалось определить выбранный диск.", "CHKDSK", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var arguments = IsCurrentWindowsSelected()
            ? $"{drive} /scan"
            : $"{drive} /f";

        var confirmation = IsCurrentWindowsSelected()
            ? $"Запустить CHKDSK для текущего диска?\r\n\r\nchkdsk {arguments}"
            : $"Запустить CHKDSK для выбранного диска?\r\n\r\nchkdsk {arguments}";
        if (MessageBox.Show(this, confirmation, "CHKDSK", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            StartVisibleCmdCommand("iUnlocker CHKDSK", $"chkdsk {arguments}");
            SetStatus($"CHKDSK запущен в окне cmd.exe: chkdsk {arguments}");
        }
        catch (Exception ex)
        {
            SetStatus($"CHKDSK не запущен: {ex.Message}");
            MessageBox.Show(this, ex.Message, "CHKDSK", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RunBootloaderCheck()
    {
        if (!_session.IsWinPe)
        {
            MessageBox.Show(
                this,
            "Проверка загрузчика выбранной Windows доступна только из WinPE.",
                "Проверка загрузчика",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var command = "bcdedit /enum all & echo. & bootrec /scanos";
        if (MessageBox.Show(this, "Запустить проверку загрузчика выбранной Windows?\r\n\r\nБудут выполнены bcdedit /enum all и bootrec /scanos.", "Проверка загрузчика", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            StartVisibleCmdCommand("iUnlocker Boot Check", command);
            SetStatus("Проверка загрузчика запущена в окне cmd.exe.");
        }
        catch (Exception ex)
        {
            SetStatus($"Проверка загрузчика не запущена: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Проверка загрузчика", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenBcdCheck()
    {
        try
        {
            var form = new BcdCheckForm(_session);
            form.Show(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Проверка BCD", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ExportOfflineReport()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Сохранить отчёт iUnlocker",
            Filter = "Текстовый отчёт (*.txt)|*.txt|Все файлы (*.*)|*.*",
            FileName = $"iUnlocker-report-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            OfflineReportExporter.Export(_session, dialog.FileName);
            SetStatus($"Отчёт сохранён: {dialog.FileName}");
            MessageBox.Show(this, "Отчёт сохранён.", "Экспорт отчёта", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SetStatus($"Отчёт не сохранён: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Экспорт отчёта", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RestoreLogonUi()
    {
        if (string.IsNullOrWhiteSpace(_session.WindowsPath) || !Directory.Exists(_session.WindowsPath))
        {
            MessageBox.Show(this, "На выбранном диске не найдена папка Windows.", "LogonUI", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var logonUiPath = Path.Combine(_session.WindowsPath, "System32", "LogonUI.exe");
        var arguments = IsCurrentWindowsSelected()
            ? $"/scanfile={QuoteArgument(logonUiPath)}"
            : $"/scanfile={QuoteArgument(logonUiPath)} /offbootdir={QuoteArgument(EnsureTrailingSlash(_session.DriveRoot))} /offwindir={QuoteArgument(_session.WindowsPath)}";

        if (MessageBox.Show(this, $"Запустить восстановление LogonUI через SFC?\r\n\r\n{logonUiPath}", "LogonUI", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            StartVisibleCmdCommand("iUnlocker LogonUI", $"sfc.exe {arguments}");
            SetStatus("Восстановление LogonUI запущено в окне cmd.exe.");
        }
        catch (Exception ex)
        {
            SetStatus($"LogonUI не запущен: {ex.Message}");
            MessageBox.Show(this, ex.Message, "LogonUI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RestoreAllFonts()
    {
        if (string.IsNullOrWhiteSpace(_session.WindowsPath) || !Directory.Exists(_session.WindowsPath))
        {
            MessageBox.Show(this, "На выбранном диске не найдена папка Windows.", "Восстановление шрифтов", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirmation = IsCurrentWindowsSelected()
            ? "Восстановить стандартные шрифты текущей Windows?\r\n\r\nБудут сброшены подмены Segoe UI, очищен кэш шрифтов и запущена точечная проверка SFC только для файлов шрифтов."
            : $"Восстановить стандартные шрифты выбранной Windows?\r\n\r\nWindows: {_session.WindowsPath}\r\n\r\nБудут сброшены offline-подмены Segoe UI, очищен offline-кэш шрифтов и запущена точечная offline-проверка SFC только для файлов шрифтов.";
        if (MessageBox.Show(this, confirmation, "Восстановление шрифтов", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            RestoreFontRegistryDefaults();
            var (deleted, errors) = CleanSelectedFontCache();
            var command = BuildFontSfcCommand();

            StartVisibleCmdCommand("iUnlocker Fonts", command);
            SetStatus($"Восстановление шрифтов запущено. Кэш очищен: {deleted}, ошибок: {errors}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Шрифты не восстановлены: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Восстановление шрифтов", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private string BuildFontSfcCommand()
    {
        var suffix = IsCurrentWindowsSelected()
            ? string.Empty
            : $" /offbootdir={QuoteArgument(EnsureTrailingSlash(_session.DriveRoot))} /offwindir={QuoteArgument(_session.WindowsPath!)}";

        return string.Join(" & ", GetSystemFontFiles().Select(file =>
        {
            var path = Path.Combine(_session.WindowsPath!, "Fonts", file);
            return $"sfc.exe /scanfile={QuoteArgument(path)}{suffix}";
        }));
    }

    private static IReadOnlyList<string> GetSystemFontFiles()
    {
        return
        [
            "segoeui.ttf",
            "segoeuib.ttf",
            "segoeuii.ttf",
            "segoeuiz.ttf",
            "segoeuil.ttf",
            "seguisb.ttf",
            "segoeuisl.ttf",
            "seguisym.ttf",
            "seguiemj.ttf",
            "seguihis.ttf",
            "segmdl2.ttf",
            "SegoeIcons.ttf",
            "tahoma.ttf",
            "tahomabd.ttf",
            "arial.ttf",
            "arialbd.ttf",
            "ariali.ttf",
            "arialbi.ttf",
            "micross.ttf",
        ];
    }

    private void RestoreFontRegistryDefaults()
    {
        if (IsCurrentWindowsSelected())
        {
            using var fontsKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts", writable: true)
                ?? throw new InvalidOperationException("Не удалось открыть ключ Fonts.");
            using var substitutesKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\FontSubstitutes", writable: true)
                ?? throw new InvalidOperationException("Не удалось открыть ключ FontSubstitutes.");
            WriteDefaultFontValues(fontsKey, substitutesKey);
            return;
        }

        var softwareHive = Path.Combine(_session.WindowsPath!, "System32", "config", "SOFTWARE");
        using var hive = OfflineRegistryHiveMount.Load(softwareHive, "IUnlocker_FONTS");
        using var offlineFontsKey = hive.Root.CreateSubKey(@"Microsoft\Windows NT\CurrentVersion\Fonts", writable: true)
            ?? throw new InvalidOperationException("Не удалось открыть offline-ключ Fonts.");
        using var offlineSubstitutesKey = hive.Root.CreateSubKey(@"Microsoft\Windows NT\CurrentVersion\FontSubstitutes", writable: true)
            ?? throw new InvalidOperationException("Не удалось открыть offline-ключ FontSubstitutes.");
        WriteDefaultFontValues(offlineFontsKey, offlineSubstitutesKey);
    }

    private static void WriteDefaultFontValues(RegistryKey fontsKey, RegistryKey substitutesKey)
    {
        var segoeFonts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Segoe UI (TrueType)"] = "segoeui.ttf",
            ["Segoe UI Black (TrueType)"] = "seguibl.ttf",
            ["Segoe UI Bold (TrueType)"] = "segoeuib.ttf",
            ["Segoe UI Bold Italic (TrueType)"] = "segoeuiz.ttf",
            ["Segoe UI Emoji (TrueType)"] = "seguiemj.ttf",
            ["Segoe UI Historic (TrueType)"] = "seguihis.ttf",
            ["Segoe UI Italic (TrueType)"] = "segoeuii.ttf",
            ["Segoe UI Light (TrueType)"] = "segoeuil.ttf",
            ["Segoe UI Semibold (TrueType)"] = "seguisb.ttf",
            ["Segoe UI Semilight (TrueType)"] = "segoeuisl.ttf",
            ["Segoe UI Symbol (TrueType)"] = "seguisym.ttf",
            ["Segoe MDL2 Assets (TrueType)"] = "segmdl2.ttf",
            ["Segoe Fluent Icons (TrueType)"] = "SegoeIcons.ttf",
        };

        foreach (var pair in segoeFonts)
        {
            fontsKey.SetValue(pair.Key, pair.Value, RegistryValueKind.String);
        }

        foreach (var valueName in substitutesKey.GetValueNames()
                     .Where(name => name.StartsWith("Segoe UI", StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            substitutesKey.DeleteValue(valueName, throwOnMissingValue: false);
        }

        substitutesKey.SetValue("MS Shell Dlg", "Microsoft Sans Serif", RegistryValueKind.String);
        substitutesKey.SetValue("MS Shell Dlg 2", "Tahoma", RegistryValueKind.String);
    }

    private (int Deleted, int Errors) CleanSelectedFontCache()
    {
        var deleted = 0;
        var errors = 0;
        var candidates = new List<string>
        {
            Path.Combine(_session.WindowsPath!, "System32", "FNTCACHE.DAT"),
            Path.Combine(_session.WindowsPath!, "ServiceProfiles", "LocalService", "AppData", "Local"),
        };

        var usersRoot = Path.Combine(_session.DriveRoot, "Users");
        if (Directory.Exists(usersRoot))
        {
            foreach (var profile in Directory.EnumerateDirectories(usersRoot))
            {
                candidates.Add(Path.Combine(profile, "AppData", "Local"));
            }
        }

        if (IsCurrentWindowsSelected())
        {
            TryStartProcess("net.exe", "stop FontCache");
        }

        foreach (var candidate in candidates)
        {
            DeleteFontCacheCandidate(candidate, ref deleted, ref errors);
        }

        if (IsCurrentWindowsSelected())
        {
            TryStartProcess("net.exe", "start FontCache");
        }

        return (deleted, errors);
    }

    private static void DeleteFontCacheCandidate(string path, ref int deleted, ref int errors)
    {
        if (File.Exists(path))
        {
            TryDeleteFile(path, ref deleted, ref errors);
            return;
        }

        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "FontCache*", SearchOption.TopDirectoryOnly))
        {
            TryDeleteFile(file, ref deleted, ref errors);
        }
    }

    private static void TryDeleteFile(string path, ref int deleted, ref int errors)
    {
        try
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            deleted++;
        }
        catch
        {
            errors++;
        }
    }

    private void CleanTemp()
    {
        var tempDirs = GetTempDirectories().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (tempDirs.Count == 0)
        {
            MessageBox.Show(this, "TEMP-папки для выбранной Windows не найдены.", "Очистка TEMP", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var preview = string.Join("\r\n", tempDirs.Take(8));
        if (tempDirs.Count > 8)
        {
            preview += $"\r\n...и ещё {tempDirs.Count - 8}";
        }

        if (MessageBox.Show(this, $"Очистить содержимое TEMP-папок?\r\n\r\n{preview}", "Очистка TEMP", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var deleted = 0;
        var errors = 0;
        foreach (var directory in tempDirs)
        {
            CleanDirectoryContents(directory, ref deleted, ref errors);
        }

        SetStatus($"Очистка TEMP завершена. Удалено объектов: {deleted}. Ошибок: {errors}.");
    }

    private IEnumerable<string> GetTempDirectories()
    {
        if (!string.IsNullOrWhiteSpace(_session.WindowsPath))
        {
            yield return Path.Combine(_session.WindowsPath, "Temp");
        }

        var usersRoot = Path.Combine(_session.DriveRoot, "Users");
        if (Directory.Exists(usersRoot))
        {
            foreach (var profile in Directory.EnumerateDirectories(usersRoot))
            {
                yield return Path.Combine(profile, "AppData", "Local", "Temp");
            }
        }

        if (IsCurrentWindowsSelected())
        {
            yield return Path.GetTempPath();
        }
    }

    private static void CleanDirectoryContents(string directory, ref int deleted, ref int errors)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(directory).ToList())
        {
            try
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else if (File.Exists(entry))
                {
                    File.Delete(entry);
                }

                deleted++;
            }
            catch
            {
                errors++;
            }
        }
    }

    private void ResetSecurityPolicy()
    {
        if (!IsCurrentWindowsSelected())
        {
            MessageBox.Show(this, "Сброс политик безопасности через secedit доступен только для текущей запущенной Windows.", "Сброс политик безопасности", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        const string command = @"secedit /configure /cfg %windir%\inf\defltbase.inf /db %temp%\iUnlocker_secedit.sdb /verbose";
        if (MessageBox.Show(this, "Сбросить политики безопасности текущей Windows к базовым значениям?\r\n\r\nЭто может изменить локальные политики безопасности.", "Сброс политик безопасности", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            StartVisibleCmdCommand("iUnlocker Security Policy", command);
            SetStatus("Сброс политик безопасности запущен в окне cmd.exe.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Сброс политик безопасности", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void DisableTestMode()
    {
        var command = GetDisableTestModeCommand();
        if (MessageBox.Show(this, $"Отключить тестовый режим загрузчика?\r\n\r\n{command}", "Отключить тестовый режим", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            StartVisibleCmdCommand("iUnlocker Test Mode", command);
            SetStatus("Команда отключения тестового режима запущена в cmd.exe.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Отключить тестовый режим", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private string GetDisableTestModeCommand()
    {
        if (_session.IsWinPe)
        {
            var bcdStore = FindSelectedBcdStore();
            if (!string.IsNullOrWhiteSpace(bcdStore))
            {
                return $@"bcdedit /store ""{bcdStore}"" /set {{default}} testsigning off & bcdedit /store ""{bcdStore}"" /set {{default}} nointegritychecks off";
            }
        }

        return "bcdedit /set testsigning off & bcdedit /set nointegritychecks off";
    }

    private string? FindSelectedBcdStore()
    {
        var candidates = new[]
        {
            Path.Combine(_session.DriveRoot, "Boot", "BCD"),
            Path.Combine(_session.DriveRoot, "EFI", "Microsoft", "Boot", "BCD"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private void OpenSignatureCheck()
    {
        if (string.IsNullOrWhiteSpace(_session.WindowsPath) || !Directory.Exists(_session.WindowsPath))
        {
            MessageBox.Show(this, "На выбранном диске не найдена папка Windows.", "Проверка подписей", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var form = new SignatureCheckForm(_session);
        form.Show(this);
    }

    private void EnableUac()
    {
        if (string.IsNullOrWhiteSpace(_session.WindowsPath) || !Directory.Exists(_session.WindowsPath))
        {
            MessageBox.Show(this, "На выбранном диске не найдена папка Windows.", "UAC", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(this, "Включить UAC для выбранной Windows?\r\n\r\nДля применения может понадобиться перезагрузка.", "UAC", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            if (IsCurrentWindowsSelected())
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", writable: true)
                    ?? throw new InvalidOperationException("Не удалось открыть ключ UAC.");
                WriteUacValues(key);
            }
            else
            {
                var softwareHive = Path.Combine(_session.WindowsPath, "System32", "config", "SOFTWARE");
                using var hive = OfflineRegistryHiveMount.Load(softwareHive, "IUnlocker_UAC");
                using var key = hive.Root.CreateSubKey(@"Microsoft\Windows\CurrentVersion\Policies\System", writable: true)
                    ?? throw new InvalidOperationException("Не удалось открыть offline-ключ UAC.");
                WriteUacValues(key);
            }

            SetStatus("UAC включён. Для применения может понадобиться перезагрузка.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "UAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void WriteUacValues(RegistryKey key)
    {
        key.SetValue("EnableLUA", 1, RegistryValueKind.DWord);
        key.SetValue("ConsentPromptBehaviorAdmin", 5, RegistryValueKind.DWord);
        key.SetValue("PromptOnSecureDesktop", 1, RegistryValueKind.DWord);
        key.SetValue("EnableInstallerDetection", 1, RegistryValueKind.DWord);
    }

    private static void StartVisibleCmdCommand(string title, string command)
    {
        var cmdArguments = $"/k \"title {title} & {command} & echo. & pause\"";
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = cmdArguments,
            UseShellExecute = true,
            CreateNoWindow = false,
        });

        if (process is null)
        {
            throw new InvalidOperationException("Не удалось запустить процесс.");
        }
    }

    private static string EnsureTrailingSlash(string path)
    {
        return path.EndsWith('\\') ? path : path + "\\";
    }

    private static string QuoteArgument(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }

    private bool IsCurrentWindowsSelected()
    {
        return !_session.IsWinPe &&
               !string.IsNullOrWhiteSpace(_session.WindowsPath) &&
               SameFullPath(_session.WindowsPath, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
    }

    private static string GetDriveLetterArgument(string driveRoot)
    {
        if (string.IsNullOrWhiteSpace(driveRoot))
        {
            return string.Empty;
        }

        var root = driveRoot.TrimEnd('\\');
        return root.EndsWith(':') ? root : root.Length >= 2 && root[1] == ':' ? root[..2] : root;
    }

    private static bool SameFullPath(string left, string right)
    {
        try
        {
            return Path.GetFullPath(left).TrimEnd('\\')
                .Equals(Path.GetFullPath(right).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void SafeModeCheckBoxChanged()
    {
        if (_syncingSafeModeCheckBox)
        {
            return;
        }

        var enable = _safeModeCheckBox.Checked;
        var message = enable
            ? "Включить постоянную загрузку в безопасный режим?\r\n\r\nWindows будет входить в безопасный режим при каждой загрузке, пока этот checkbox не будет отключён."
            : "Отключить постоянную загрузку в безопасный режим?";

        if (MessageBox.Show(this, message, "Безопасный режим", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            UpdateSafeModeState();
            return;
        }

        try
        {
            SetSafeModeEnabled(enable);
            SetStatus(enable
                ? "Постоянная загрузка в безопасный режим включена."
                : "Постоянная загрузка в безопасный режим отключена.");
            UpdateSafeModeState();
        }
        catch (Exception ex)
        {
            UpdateSafeModeState();
            MessageBox.Show(this, ex.Message, "Безопасный режим", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RestartToSafeMode()
    {
        if (MessageBox.Show(
                this,
                "Включить безопасный режим и перезагрузить компьютер?\r\n\r\nЧтобы потом вернуться к обычной загрузке, отключите checkbox \"Безопасный режим\".",
                "Безопасный режим",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            SetSafeModeEnabled(true);
            UpdateSafeModeState();
            SetStatus("Безопасный режим включён, отправлена команда перезагрузки.");
            StartPowerAction(reboot: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Безопасный режим", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateSafeModeState()
    {
        var available = CanUseSafeModeBcd();
        _restartSafeModeButton.Enabled = available;
        _safeModeCheckBox.Enabled = available;

        _syncingSafeModeCheckBox = true;
        try
        {
            _safeModeCheckBox.Checked = available && IsSafeModeEnabled();
        }
        catch
        {
            _safeModeCheckBox.Checked = false;
            _safeModeCheckBox.Enabled = false;
        }
        finally
        {
            _syncingSafeModeCheckBox = false;
        }
    }

    private bool CanUseSafeModeBcd()
    {
        if (!_session.IsWinPe)
        {
            return true;
        }

        return IsOfflineWindowsSelected() && !string.IsNullOrWhiteSpace(FindSelectedBcdStore());
    }

    private bool IsSafeModeEnabled()
    {
        var result = RunBcdEdit(GetSafeModeEnumArguments());
        return result.ExitCode == 0 &&
               result.Output.Contains("safeboot", StringComparison.OrdinalIgnoreCase);
    }

    private void SetSafeModeEnabled(bool enable)
    {
        if (!CanUseSafeModeBcd())
        {
            throw new InvalidOperationException("BCD выбранной Windows не найден. В WinPE выберите диск с установленной Windows.");
        }

        var arguments = enable
            ? GetSafeModeSetArguments()
            : GetSafeModeDeleteArguments();
        var result = RunBcdEdit(arguments);
        if (result.ExitCode != 0 && !(enable == false && result.Output.Contains("Element not found", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(result.Output.Trim().Length == 0
                ? "bcdedit завершился с ошибкой."
                : result.Output.Trim());
        }
    }

    private string GetSafeModeEnumArguments()
    {
        return _session.IsWinPe
            ? $"/store {QuoteArgument(FindSelectedBcdStore()!)} /enum {{default}}"
            : "/enum {current}";
    }

    private string GetSafeModeSetArguments()
    {
        return _session.IsWinPe
            ? $"/store {QuoteArgument(FindSelectedBcdStore()!)} /set {{default}} safeboot minimal"
            : "/set {current} safeboot minimal";
    }

    private string GetSafeModeDeleteArguments()
    {
        return _session.IsWinPe
            ? $"/store {QuoteArgument(FindSelectedBcdStore()!)} /deletevalue {{default}} safeboot"
            : "/deletevalue {current} safeboot";
    }

    private static (int ExitCode, string Output) RunBcdEdit(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bcdedit.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Default,
                StandardErrorEncoding = Encoding.Default,
            },
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + error);
    }

    private void RestartComputer()
    {
        if (MessageBox.Show(this, "Перезагрузить компьютер?", "Перезагрузка", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            StartPowerAction(reboot: true);
        }
    }

    private void ShutdownComputer()
    {
        if (MessageBox.Show(this, "Выключить компьютер?", "Выключение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            StartPowerAction(reboot: false);
        }
    }

    private void StartPowerAction(bool reboot)
    {
        if (_session.IsWinPe && TryStartWpeUtil(reboot))
        {
            SetStatus(reboot ? "Команда WinPE reboot отправлена." : "Команда WinPE shutdown отправлена.");
            return;
        }

        var flags = reboot
            ? ExitWindowsFlags.EWX_REBOOT
            : ExitWindowsFlags.EWX_POWEROFF;

        if (TryExitWindows(flags))
        {
            SetStatus(reboot ? "Команда перезагрузки отправлена." : "Команда выключения отправлена.");
            return;
        }

        StartShutdownExe(reboot ? "/r /t 0" : "/s /t 0");
    }

    private static bool TryStartWpeUtil(bool reboot)
    {
        var wpeutilPath = Path.Combine(Environment.SystemDirectory, "wpeutil.exe");
        var fileName = File.Exists(wpeutilPath) ? wpeutilPath : "wpeutil.exe";
        return TryStartProcess(fileName, reboot ? "reboot" : "shutdown");
    }

    private static void StartShutdownExe(string arguments)
    {
        if (!TryStartProcess("shutdown.exe", arguments))
        {
            throw new InvalidOperationException("Не удалось запустить команду питания.");
        }
    }

    private static bool TryStartProcess(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            return process is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryExitWindows(ExitWindowsFlags flags)
    {
        try
        {
            EnableShutdownPrivilege();
            return ExitWindowsEx(flags | ExitWindowsFlags.EWX_FORCEIFHUNG, ShutdownReason.SHTDN_REASON_MAJOR_OTHER);
        }
        catch
        {
            return false;
        }
    }

    private static void EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAccess.TOKEN_ADJUST_PRIVILEGES | TokenAccess.TOKEN_QUERY, out var token))
        {
            return;
        }

        try
        {
            if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out var luid))
            {
                return;
            }

            var privileges = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = PrivilegeAttributes.SE_PRIVILEGE_ENABLED,
            };
            AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
    }

    private string GetInfoText()
    {
        var windowsText = _session.WindowsPath is null
            ? "Windows: не найдена на выбранном диске"
            : $"Windows: {_session.WindowsPath}";

        return $"Среда: {_session.EnvironmentName}\r\nДиск: {_session.DriveRoot}\r\n{windowsText}";
    }

    [Flags]
    private enum ExitWindowsFlags : uint
    {
        EWX_REBOOT = 0x00000002,
        EWX_POWEROFF = 0x00000008,
        EWX_FORCEIFHUNG = 0x00000010,
    }

    private enum ShutdownReason : uint
    {
        SHTDN_REASON_MAJOR_OTHER = 0x00000000,
    }

    [Flags]
    private enum TokenAccess : uint
    {
        TOKEN_ADJUST_PRIVILEGES = 0x0020,
        TOKEN_QUERY = 0x0008,
    }

    private enum PrivilegeAttributes : uint
    {
        SE_PRIVILEGE_ENABLED = 0x00000002,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public Luid Luid;
        public PrivilegeAttributes Attributes;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ExitWindowsEx(ExitWindowsFlags uFlags, ShutdownReason dwReason);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, TokenAccess desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        bool disableAllPrivileges,
        ref TokenPrivileges newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
