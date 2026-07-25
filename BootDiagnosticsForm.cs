using System.Diagnostics;
using System.Text;

namespace IUnlocker;

public sealed class BootDiagnosticsForm : Form
{
    private readonly AppSession _session;
    private readonly DataGridView _grid = new();
    private readonly TextBox _detailsBox = new();
    private readonly Button _refreshButton = new();

    public BootDiagnosticsForm(AppSession session)
    {
        _session = session;
        BuildInterface();
        Shown += async (_, _) => await RefreshDiagnosticsAsync();
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - диагностика загрузки";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(820, 560);
        ClientSize = new Size(1060, 700);
        UiTheme.ApplyForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 10),
        };
        _refreshButton.Text = "Обновить";
        _refreshButton.AutoSize = true;
        _refreshButton.Click += async (_, _) => await RefreshDiagnosticsAsync();
        UiTheme.StyleButton(_refreshButton, primary: true);
        toolbar.Controls.Add(_refreshButton);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.AutoGenerateColumns = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(BootDiagnosticRow.Component), HeaderText = "Проверка", Width = 230 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(BootDiagnosticRow.Status), HeaderText = "Статус", Width = 125 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(BootDiagnosticRow.Details), HeaderText = "Сведения", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        UiTheme.StyleGrid(_grid);

        _detailsBox.Dock = DockStyle.Fill;
        _detailsBox.Multiline = true;
        _detailsBox.ReadOnly = true;
        _detailsBox.ScrollBars = ScrollBars.Both;
        _detailsBox.WordWrap = false;
        UiTheme.StyleTextBox(_detailsBox);

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(_detailsBox, 0, 2);
        Controls.Add(root);
    }

    private async Task RefreshDiagnosticsAsync()
    {
        _refreshButton.Enabled = false;
        _detailsBox.Text = "Проверка загрузочной конфигурации...";
        try
        {
            var result = await Task.Run(CollectDiagnostics);
            _grid.DataSource = result.Rows;
            _detailsBox.Text = result.Details;
        }
        catch (Exception ex)
        {
            _grid.DataSource = new[] { new BootDiagnosticRow("Диагностика", "Ошибка", ex.Message) };
            _detailsBox.Text = ex.ToString();
        }
        finally
        {
            _refreshButton.Enabled = true;
        }
    }

    private BootDiagnosticsResult CollectDiagnostics()
    {
        var rows = new List<BootDiagnosticRow>();
        var details = new StringBuilder();
        var windowsPath = GetWindowsPath();
        var driveRoot = Path.GetPathRoot(windowsPath) ?? _session.DriveRoot;

        rows.Add(new BootDiagnosticRow(
            "Установка Windows",
            Directory.Exists(windowsPath) ? "Найдена" : "Не найдена",
            windowsPath));

        AddFileCheck(rows, "Загрузчик Windows", Path.Combine(windowsPath, "System32", "winload.efi"), Path.Combine(windowsPath, "System32", "winload.exe"));
        AddFileCheck(rows, "Возобновление Windows", Path.Combine(windowsPath, "System32", "winresume.efi"), Path.Combine(windowsPath, "System32", "winresume.exe"));

        var efiFile = VolumeUtility.EnumerateVolumeRoots()
            .Select(volumeRoot => Path.Combine(volumeRoot, "EFI", "Microsoft", "Boot", "bootmgfw.efi"))
            .FirstOrDefault(File.Exists);
        rows.Add(new BootDiagnosticRow(
            "UEFI Boot Manager",
            efiFile is null ? "Не найден" : "Найден",
            efiFile ?? "EFI-раздел с bootmgfw.efi не найден среди доступных томов."));

        var legacyBootManager = Path.Combine(driveRoot, "bootmgr");
        rows.Add(new BootDiagnosticRow(
            "Boot Manager",
            File.Exists(legacyBootManager) ? "Найден" : efiFile is not null ? "Не требуется" : "Не найден",
            File.Exists(legacyBootManager)
                ? legacyBootManager
                : efiFile is not null
                    ? "Система использует UEFI Boot Manager на EFI-разделе. Отсутствие bootmgr на диске Windows нормально."
                    : legacyBootManager));

        var bcdStore = BcdUtility.FindSelectedBcdStore(_session);
        if (bcdStore is null)
        {
            rows.Add(new BootDiagnosticRow("BCD", "Не найден", "Не найден файл Boot\\BCD или EFI\\Microsoft\\Boot\\BCD."));
        }
        else
        {
            var arguments = _session.IsWinPe && _session.WindowsPath is not null && !_session.DriveRoot.StartsWith("X:", StringComparison.OrdinalIgnoreCase)
                ? $"/store {BcdUtility.QuoteArgument(bcdStore)} /enum all"
                : "/enum all";
            var bcdResult = BcdUtility.RunBcdEdit(arguments);
            var entries = bcdResult.ExitCode == 0 ? BcdUtility.ParseEntries(bcdResult.Output) : [];
            rows.Add(new BootDiagnosticRow(
                "BCD",
                bcdResult.ExitCode == 0 ? "Читается" : "Ошибка",
                bcdResult.ExitCode == 0 ? $"{bcdStore}. Записей: {entries.Count}." : bcdResult.Output.Trim()));
            details.AppendLine("=== BCD ===");
            details.AppendLine(bcdResult.Output.Trim());
            details.AppendLine();
        }

        var reagentXml = Path.Combine(windowsPath, "System32", "Recovery", "ReAgent.xml");
        var winReImage = Path.Combine(windowsPath, "System32", "Recovery", "Winre.wim");
        var winReFound = File.Exists(reagentXml) || File.Exists(winReImage);
        rows.Add(new BootDiagnosticRow(
            "Среда восстановления WinRE",
            winReFound ? "Обнаружена" : "Не обнаружена",
            File.Exists(reagentXml) ? reagentXml : File.Exists(winReImage) ? winReImage : "ReAgent.xml и Winre.wim в стандартном расположении не найдены."));

        var srtTrail = Path.Combine(windowsPath, "System32", "LogFiles", "Srt", "SrtTrail.txt");
        if (File.Exists(srtTrail))
        {
            var lines = File.ReadLines(srtTrail).TakeLast(80);
            rows.Add(new BootDiagnosticRow("Журнал Startup Repair", "Найден", srtTrail));
            details.AppendLine("=== SrtTrail.txt, последние строки ===");
            details.AppendLine(string.Join(Environment.NewLine, lines));
        }
        else
        {
            rows.Add(new BootDiagnosticRow("Журнал Startup Repair", "Нет", "SrtTrail.txt не найден. Это нормально, если автоматическое восстановление не запускалось."));
        }

        return new BootDiagnosticsResult(rows, details.Length == 0 ? "Подробных журналов для показа нет." : details.ToString());
    }

    private string GetWindowsPath()
    {
        if (!string.IsNullOrWhiteSpace(_session.WindowsPath) && Directory.Exists(_session.WindowsPath))
        {
            return _session.WindowsPath;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    }

    private static void AddFileCheck(List<BootDiagnosticRow> rows, string title, params string[] paths)
    {
        var path = paths.FirstOrDefault(File.Exists);
        rows.Add(new BootDiagnosticRow(title, path is null ? "Не найден" : "Найден", path ?? string.Join(" | ", paths)));
    }

    private sealed record BootDiagnosticRow(string Component, string Status, string Details);
    private sealed record BootDiagnosticsResult(IReadOnlyList<BootDiagnosticRow> Rows, string Details);
}
