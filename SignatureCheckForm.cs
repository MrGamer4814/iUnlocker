using System.ComponentModel;

namespace IUnlocker;

public sealed class SignatureCheckForm : Form
{
    private readonly AppSession _session;
    private readonly DataGridView _grid = new();
    private readonly Button _scanButton = new();
    private readonly CheckBox _showValidBox = new();
    private readonly Label _statusLabel = new();
    private readonly BindingList<SignatureCheckRow> _rows = [];

    public SignatureCheckForm(AppSession session)
    {
        _session = session;
        BuildInterface();
        Load += (_, _) => BeginScan();
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - проверка подписей";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(920, 540);
        ClientSize = new Size(1120, 680);
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

        _scanButton.Text = "Сканировать";
        _scanButton.AutoSize = true;
        _scanButton.Margin = new Padding(0, 0, 8, 8);
        _scanButton.Click += (_, _) => BeginScan();
        UiTheme.StyleButton(_scanButton, primary: true);

        _showValidBox.Text = "Показывать действительные";
        _showValidBox.AutoSize = true;
        _showValidBox.Margin = new Padding(0, 4, 8, 8);
        UiTheme.StyleCheckBox(_showValidBox);

        toolbar.Controls.Add(_scanButton);
        toolbar.Controls.Add(_showValidBox);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToOrderColumns = true;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.DataSource = _rows;
        UiTheme.StyleGrid(_grid);

        AddColumn(nameof(SignatureCheckRow.Name), "Файл", 180);
        AddColumn(nameof(SignatureCheckRow.Status), "Подпись", 150);
        AddColumn(nameof(SignatureCheckRow.Publisher), "Издатель", 220);
        AddColumn(nameof(SignatureCheckRow.Path), "Путь", 620);

        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(0, 10, 0, 0);
        _statusLabel.ForeColor = UiTheme.MutedText;

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(_statusLabel, 0, 2);
        Controls.Add(root);
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
        if (string.IsNullOrWhiteSpace(_session.WindowsPath) || !Directory.Exists(_session.WindowsPath))
        {
            MessageBox.Show(this, "На выбранном диске не найдена папка Windows.", "Проверка подписей", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _rows.Clear();
        _scanButton.Enabled = false;
        _statusLabel.Text = "Идёт проверка подписей...";

        var progress = new Progress<SignatureCheckRow>(row => _rows.Add(row));
        try
        {
            var summary = await Task.Run(() => ScanSignatures(_session.WindowsPath, _showValidBox.Checked, progress));
            _statusLabel.Text = $"Проверено: {summary.Checked}. Показано: {_rows.Count}. Проблем: {summary.Problems}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Проверка подписей", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _scanButton.Enabled = true;
        }
    }

    private static SignatureScanSummary ScanSignatures(string windowsPath, bool showValid, IProgress<SignatureCheckRow> progress)
    {
        var checkedCount = 0;
        var problemCount = 0;
        foreach (var path in EnumerateSignatureTargets(windowsPath))
        {
            checkedCount++;
            var signature = FileSignatureVerifier.Verify(path);
            var problem = !signature.IsValid;
            if (problem)
            {
                problemCount++;
            }

            if (showValid || problem)
            {
                progress.Report(new SignatureCheckRow(
                    Path.GetFileName(path),
                    string.IsNullOrWhiteSpace(signature.Status) ? "Не проверено" : signature.Status,
                    signature.Publisher,
                    path));
            }
        }

        return new SignatureScanSummary(checkedCount, problemCount);
    }

    private static IEnumerable<string> EnumerateSignatureTargets(string windowsPath)
    {
        foreach (var pattern in new[]
        {
            Path.Combine(windowsPath, "System32", "drivers", "*.sys"),
            Path.Combine(windowsPath, "System32", "*.exe"),
            Path.Combine(windowsPath, "System32", "*.dll"),
        })
        {
            var directory = Path.GetDirectoryName(pattern);
            var mask = Path.GetFileName(pattern);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, mask, SearchOption.TopDirectoryOnly).ToList();
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    private sealed record SignatureCheckRow(string Name, string Status, string Publisher, string Path);

    private sealed record SignatureScanSummary(int Checked, int Problems);
}
