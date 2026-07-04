namespace IUnlocker;

public sealed class BsodAnalyzerForm : Form
{
    private readonly AppSession _session;
    private readonly DataGridView _grid = new();
    private readonly Button _refreshButton = new();
    private readonly Button _openLocationButton = new();
    private readonly Label _statusLabel = new();

    private List<BsodAnalysisRow> _rows = [];

    public BsodAnalyzerForm(AppSession session)
    {
        _session = session;
        BuildInterface();
        Shown += (_, _) => RefreshRows();
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - анализ BSOD";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(980, 560);
        ClientSize = new Size(1180, 680);
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
            Margin = new Padding(0, 0, 0, 10),
        };
        ConfigureButton(_refreshButton, "Обновить", (_, _) => RefreshRows(), primary: true);
        ConfigureButton(_openLocationButton, "Открыть расположение", (_, _) => OpenSelectedLocation());
        toolbar.Controls.AddRange([_refreshButton, _openLocationButton]);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        UiTheme.StyleGrid(_grid);
        AddColumn(nameof(BsodAnalysisRow.DumpName), "Дамп", 160);
        AddColumn(nameof(BsodAnalysisRow.TimeCreated), "Дата", 150);
        AddColumn(nameof(BsodAnalysisRow.BugCheckCode), "BugCheck", 110);
        AddColumn(nameof(BsodAnalysisRow.BugCheckName), "Описание", 230);
        AddColumn(nameof(BsodAnalysisRow.SuspectDriver), "Драйвер", 150);
        AddColumn(nameof(BsodAnalysisRow.Signature), "Подпись", 210);
        AddColumn(nameof(BsodAnalysisRow.Size), "Размер", 90);
        AddColumn(nameof(BsodAnalysisRow.DriverMentions), "Упоминания .sys", 360);

        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = UiTheme.MutedText;
        _statusLabel.Padding = new Padding(0, 10, 0, 0);

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_grid, 0, 1);
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

    private void AddColumn(string property, string header, int width)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            Name = property,
            Width = width,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });
    }

    private void RefreshRows()
    {
        Cursor = Cursors.WaitCursor;
        _refreshButton.Enabled = false;
        try
        {
            _rows = BsodAnalyzer.Analyze(_session).ToList();
            _grid.DataSource = _rows;
            _statusLabel.Text = _rows.Count == 0
                ? "Дампы BSOD и события BugCheck не найдены."
                : $"Найдено записей BSOD: {_rows.Count}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Анализ BSOD", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _refreshButton.Enabled = true;
            Cursor = Cursors.Default;
            UpdateButtons();
        }
    }

    private void OpenSelectedLocation()
    {
        if (_grid.CurrentRow?.DataBoundItem is not BsodAnalysisRow row || string.IsNullOrWhiteSpace(row.Path))
        {
            return;
        }

        try
        {
            var initialPath = File.Exists(row.Path)
                ? Path.GetDirectoryName(row.Path)
                : Directory.Exists(row.Path)
                    ? row.Path
                    : Path.GetDirectoryName(row.Path);
            if (string.IsNullOrWhiteSpace(initialPath) || !Directory.Exists(initialPath))
            {
                MessageBox.Show(this, "Папка дампа не найдена.", "Открыть расположение", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var explorer = new FileExplorerForm(_session, initialPath, File.Exists(row.Path) ? row.Path : null);
            explorer.Show(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Открыть расположение", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateButtons()
    {
        _openLocationButton.Enabled = _grid.CurrentRow?.DataBoundItem is BsodAnalysisRow row &&
                                      !string.IsNullOrWhiteSpace(row.Path);
    }
}
