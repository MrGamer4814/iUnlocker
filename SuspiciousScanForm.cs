using System.Diagnostics;

namespace IUnlocker;

public sealed class SuspiciousScanForm : Form
{
    private readonly AppSession _session;
    private readonly DataGridView _grid = new();
    private readonly Button _scanButton = new();
    private readonly Button _quarantineButton = new();
    private readonly Button _openLocationButton = new();
    private readonly Label _statusLabel = new();

    private List<SuspiciousFinding> _findings = [];

    public SuspiciousScanForm(AppSession session)
    {
        _session = session;
        BuildInterface();
        Load += (_, _) => BeginScan();
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - скан подозрительного";
        StartPosition = FormStartPosition.CenterParent;
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

        ConfigureButton(_scanButton, "Сканировать", (_, _) => BeginScan(), primary: true);
        ConfigureButton(_quarantineButton, "В карантин", (_, _) => QuarantineSelected());
        ConfigureButton(_openLocationButton, "Открыть в проводнике iUnlocker", (_, _) => OpenSelectedLocation());
        toolbar.Controls.AddRange([_scanButton, _quarantineButton, _openLocationButton]);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToOrderColumns = true;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        UiTheme.StyleGrid(_grid);

        AddColumn(nameof(SuspiciousFinding.Source), "Источник", 150);
        AddColumn(nameof(SuspiciousFinding.Name), "Название", 180);
        AddColumn(nameof(SuspiciousFinding.Reason), "Причина", 320);
        AddColumn(nameof(SuspiciousFinding.SignatureStatus), "Подпись", 140);
        AddColumn(nameof(SuspiciousFinding.SignaturePublisher), "Издатель", 190);
        AddColumn(nameof(SuspiciousFinding.Path), "Путь файла", 520);

        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(0, 10, 0, 0);
        _statusLabel.ForeColor = UiTheme.MutedText;

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(_statusLabel, 0, 2);
        Controls.Add(root);
        UpdateButtons();
    }

    private static void ConfigureButton(Button button, string text, EventHandler onClick, bool primary = false)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Margin = new Padding(0, 0, 8, 8);
        button.Click += onClick;
        UiTheme.StyleButton(button, primary);
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

    private async void BeginScan()
    {
        _scanButton.Enabled = false;
        _statusLabel.Text = "Идёт сканирование...";
        Cursor = Cursors.WaitCursor;

        try
        {
            _findings = await Task.Run(ScanSuspicious);
            _grid.DataSource = _findings;
            _statusLabel.Text = $"Найдено подозрительного: {_findings.Count}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Скан подозрительного", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _scanButton.Enabled = true;
            Cursor = Cursors.Default;
            UpdateButtons();
        }
    }

    private List<SuspiciousFinding> ScanSuspicious()
    {
        var rows = new List<SuspiciousFinding>();
        rows.AddRange(ScanStartupEntries());
        rows.AddRange(ScanProcesses());

        return rows
            .GroupBy(row => $"{row.Source}|{row.Path}|{row.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(row => row.Source, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private IEnumerable<SuspiciousFinding> ScanStartupEntries()
    {
        StartupScanResult result;
        if (_session.IsWinPe && _session.WindowsPath is not null && !_session.DriveRoot.StartsWith("X:", StringComparison.OrdinalIgnoreCase))
        {
            result = OfflineStartupScanner.Scan(_session);
        }
        else
        {
            result = StartupScanner.Scan();
        }

        foreach (var entry in result.Entries)
        {
            var path = TryGetExistingTargetPath(entry.Command, entry.Location);
            var reason = GetStartupReason(entry, path);
            if (reason is null)
            {
                continue;
            }

            var signature = FileSignatureVerifier.Verify(path);
            yield return new SuspiciousFinding(
                "Автозагрузка",
                entry.Name,
                reason,
                signature.Status,
                signature.Publisher,
                path ?? string.Empty);
        }
    }

    private IEnumerable<SuspiciousFinding> ScanProcesses()
    {
        foreach (var process in Process.GetProcesses())
        {
            string name;
            string path = string.Empty;

            try
            {
                name = process.ProcessName;
                path = process.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                name = SafeProcessName(process);
            }
            finally
            {
                process.Dispose();
            }

            var reason = GetFileReason(name, path);
            if (reason is null)
            {
                continue;
            }

            var signature = FileSignatureVerifier.Verify(path);
            yield return new SuspiciousFinding(
                "Процесс",
                name,
                reason,
                signature.Status,
                signature.Publisher,
                path);
        }
    }

    private static string SafeProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return $"PID {process.Id}";
        }
    }

    private static string? GetStartupReason(StartupEntry entry, string? filePath)
    {
        var text = $"{entry.Command} {entry.Location}";
        var fileReason = GetFileReason(entry.Name, filePath);
        if (fileReason is not null)
        {
            return fileReason;
        }

        if (ContainsAny(text, ["powershell", "pwsh", "wscript", "cscript", "mshta", "rundll32", "regsvr32", "cmd.exe /c"]))
        {
            return "Подозрительная команда запуска";
        }

        if (ContainsAny(text, ["http://", "https://"]))
        {
            return "Команда содержит ссылку";
        }

        if (filePath is null &&
            !entry.Location.StartsWith("HK", StringComparison.OrdinalIgnoreCase) &&
            !entry.Location.StartsWith("Offline", StringComparison.OrdinalIgnoreCase) &&
            !entry.Location.StartsWith("Task Scheduler:", StringComparison.OrdinalIgnoreCase))
        {
            return "Файл из записи не найден";
        }

        return null;
    }

    private static string? GetFileReason(string name, string? path)
    {
        if (ContainsAny(name, ["powershell", "pwsh", "wscript", "cscript", "mshta", "rundll32", "regsvr32"]))
        {
            return "Подозрительный интерпретатор";
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (ContainsAny(path, [@"\appdata\", @"\temp\", @"\downloads\", @"\users\public\", @"\$recycle.bin\"]))
        {
            return "Подозрительное расположение";
        }

        if (path.EndsWith(".scr", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".vbs", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            return "Подозрительное расширение";
        }

        var signature = FileSignatureVerifier.Verify(path);
        if (signature.Status.Contains("поврежд", StringComparison.OrdinalIgnoreCase) ||
            signature.Status.Contains("Запрещ", StringComparison.OrdinalIgnoreCase))
        {
            return "Проблема с цифровой подписью";
        }

        return null;
    }

    private static bool ContainsAny(string text, IEnumerable<string> needles)
    {
        return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private string? TryGetExistingTargetPath(string command, string location)
    {
        if (File.Exists(location))
        {
            return location;
        }

        foreach (var candidate in GetCommandPathCandidates(command))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private IEnumerable<string> GetCommandPathCandidates(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            yield break;
        }

        var expanded = ExpandCommandVariables(command.Trim());
        if (expanded.StartsWith('"'))
        {
            var closingQuote = expanded.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                yield return expanded[1..closingQuote];
            }
        }

        var separators = new[] { " /", " -", " \t" };
        var end = expanded.Length;
        foreach (var separator in separators)
        {
            var index = expanded.IndexOf(separator, StringComparison.Ordinal);
            if (index > 0)
            {
                end = Math.Min(end, index);
            }
        }

        yield return expanded[..end].Trim('"', ' ');

        foreach (var extension in new[] { ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".scr" })
        {
            var index = expanded.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                yield return expanded[..(index + extension.Length)].Trim('"', ' ');
            }
        }
    }

    private string ExpandCommandVariables(string command)
    {
        if (_session.WindowsPath is not null)
        {
            command = command
                .Replace("%SystemRoot%", _session.WindowsPath, StringComparison.OrdinalIgnoreCase)
                .Replace("%windir%", _session.WindowsPath, StringComparison.OrdinalIgnoreCase)
                .Replace("%SystemDrive%", _session.DriveRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                .Replace("%ProgramFiles%", Path.Combine(_session.DriveRoot, "Program Files").TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                .Replace("%ProgramFiles(x86)%", Path.Combine(_session.DriveRoot, "Program Files (x86)").TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        return Environment.ExpandEnvironmentVariables(command);
    }

    private SuspiciousFinding? GetSelectedFinding()
    {
        return _grid.CurrentRow?.DataBoundItem as SuspiciousFinding;
    }

    private void QuarantineSelected()
    {
        var finding = GetSelectedFinding();
        if (finding is null || string.IsNullOrWhiteSpace(finding.Path) || !File.Exists(finding.Path))
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Переместить файл в карантин?\r\n\r\n{finding.Path}\r\n\r\nПричина: {finding.Reason}",
            "Карантин",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (result != DialogResult.Yes)
        {
            return;
        }

        try
        {
            QuarantineManager.QuarantineFile(finding.Path, finding.Reason, finding.Source);
            _findings.Remove(finding);
            _grid.DataSource = _findings.ToList();
            _statusLabel.Text = $"Файл перемещён в карантин. Осталось: {_findings.Count}.";
            UpdateButtons();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Карантин", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenSelectedLocation()
    {
        var finding = GetSelectedFinding();
        if (finding is null || string.IsNullOrWhiteSpace(finding.Path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(finding.Path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var explorer = new FileExplorerForm(_session, directory, finding.Path);
        explorer.Show(this);
    }

    private void UpdateButtons()
    {
        var finding = GetSelectedFinding();
        var hasFile = finding is not null && !string.IsNullOrWhiteSpace(finding.Path) && File.Exists(finding.Path);
        _quarantineButton.Enabled = hasFile;
        _openLocationButton.Enabled = hasFile;
    }

    private sealed record SuspiciousFinding(
        string Source,
        string Name,
        string Reason,
        string SignatureStatus,
        string SignaturePublisher,
        string Path);
}
