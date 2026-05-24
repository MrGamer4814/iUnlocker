namespace IUnlocker;

using System.Diagnostics;
using Microsoft.Win32;
using System.Runtime.InteropServices;

public sealed class AdditionalFunctionsForm : Form
{
    private readonly AppSession _session;
    private readonly Button _replaceAccessibilityButton = new();
    private readonly Button _setCmdLineButton = new();
    private readonly Button _restoreLogonUiButton = new();
    private readonly Button _cleanTempButton = new();
    private readonly Button _sfcScanButton = new();
    private readonly Button _dismCheckHealthButton = new();
    private readonly Button _dismScanHealthButton = new();
    private readonly Button _dismRestoreHealthButton = new();
    private readonly Button _chkdskButton = new();
    private readonly Button _bootCheckButton = new();
    private readonly Button _resetSecurityPolicyButton = new();
    private readonly Button _disableTestModeButton = new();
    private readonly Button _verifySignaturesButton = new();
    private readonly Button _enableUacButton = new();
    private readonly Button _restartButton = new();
    private readonly Button _shutdownButton = new();
    private readonly Label _statusLabel = new();
    private bool _cmdLineInstalled;
    private bool _accessibilityToolsReplaced;

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
            _restoreLogonUiButton,
            "Восстановить LogonUI",
            (_, _) => RestoreLogonUi());
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
            _shutdownButton,
            "Выключение компьютера",
            (_, _) => ShutdownComputer());

        var offlineWindows = IsOfflineWindowsSelected();
        _replaceAccessibilityButton.Enabled = offlineWindows;
        _setCmdLineButton.Enabled = CanSetCmdLine();
        _bootCheckButton.Enabled = _session.IsWinPe;
        _resetSecurityPolicyButton.Enabled = IsCurrentWindowsSelected();
        UpdateAccessibilityButtonState();
        UpdateCmdLineButtonState();

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
        AddTab(tabs, "Восстановление", recoveryPage);

        var securityPage = CreateActionPage();
        AddActionButton(securityPage, _enableUacButton, 0, 0);
        AddActionButton(securityPage, _resetSecurityPolicyButton, 1, 0);
        AddActionButton(securityPage, _disableTestModeButton, 0, 1);
        AddActionButton(securityPage, _verifySignaturesButton, 1, 1);
        AddActionButton(securityPage, _bootCheckButton, 0, 2, columnSpan: 2);
        AddTab(tabs, "Безопасность", securityPage);

        var accessPage = CreateActionPage();
        AddActionButton(accessPage, _replaceAccessibilityButton, 0, 0);
        AddActionButton(accessPage, _setCmdLineButton, 1, 0);
        AddActionButton(accessPage, _cleanTempButton, 0, 1, columnSpan: 2);
        AddTab(tabs, "Система", accessPage);

        var powerPage = CreateActionPage();
        AddActionButton(powerPage, _restartButton, 0, 0);
        AddActionButton(powerPage, _shutdownButton, 1, 0);
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
        panel.Controls.Add(button, column, row);
        if (columnSpan > 1)
        {
            panel.SetColumnSpan(button, columnSpan);
        }
    }

    private static void ConfigureActionButton(Button button, string text, EventHandler onClick)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 0, 12, 12);
        button.Font = new Font("Segoe UI", 10, FontStyle.Regular);
        button.TextAlign = ContentAlignment.MiddleCenter;
        UiTheme.StyleButton(button);
        button.Click += onClick;
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
