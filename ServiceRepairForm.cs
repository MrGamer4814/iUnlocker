namespace IUnlocker;

public sealed class ServiceRepairForm : Form
{
    private readonly AppSession _session;
    private readonly DataGridView _grid = new();
    private readonly Button _refreshButton = new();
    private readonly Button _restoreSelectedButton = new();
    private readonly Button _restoreAllButton = new();
    private readonly Label _statusLabel = new();

    private List<ServiceRepairRow> _rows = [];

    public ServiceRepairForm(AppSession session)
    {
        _session = session;
        BuildInterface();
        Shown += (_, _) => RefreshRows();
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - восстановление служб";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(920, 560);
        ClientSize = new Size(1080, 660);
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
        ConfigureButton(_restoreSelectedButton, "Восстановить выбранную", (_, _) => RestoreSelected());
        ConfigureButton(_restoreAllButton, "Восстановить все отличающиеся", (_, _) => RestoreAllChanged());
        toolbar.Controls.AddRange([_refreshButton, _restoreSelectedButton, _restoreAllButton]);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.DataBindingComplete += (_, _) => HighlightRows();
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        UiTheme.StyleGrid(_grid);
        AddColumn(nameof(ServiceRepairRow.Name), "Служба", 150);
        AddColumn(nameof(ServiceRepairRow.DisplayName), "Описание", 280);
        AddColumn(nameof(ServiceRepairRow.CurrentStart), "Сейчас", 110);
        AddColumn(nameof(ServiceRepairRow.RecommendedStart), "Рекомендуется", 120);
        AddColumn(nameof(ServiceRepairRow.Status), "Статус", 110);
        AddColumn(nameof(ServiceRepairRow.ImagePath), "Команда", 360);

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
            _rows = WindowsServiceRepairUtility.Scan(_session).ToList();
            _grid.DataSource = _rows;
            var changed = _rows.Count(row => row.Status == "отличается" || row.Status == "нет службы");
            _statusLabel.Text = $"Проверено служб: {_rows.Count}. Отличается/нет: {changed}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Восстановление служб", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _refreshButton.Enabled = true;
            Cursor = Cursors.Default;
            UpdateButtons();
        }
    }

    private void RestoreSelected()
    {
        if (_grid.CurrentRow?.DataBoundItem is not ServiceRepairRow row)
        {
            return;
        }

        var definition = WindowsServiceRepairUtility.FindDefinition(row.Name);
        if (definition is null)
        {
            return;
        }

        RestoreDefinitions([definition], $"Восстановить тип запуска для {row.Name}?");
    }

    private void RestoreAllChanged()
    {
        var definitions = _rows
            .Where(row => row.Status == "отличается")
            .Select(row => WindowsServiceRepairUtility.FindDefinition(row.Name))
            .Where(definition => definition is not null)
            .Cast<ServiceRepairDefinition>()
            .ToList();
        if (definitions.Count == 0)
        {
            MessageBox.Show(this, "Отличающихся служб не найдено.", "Восстановление служб", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        RestoreDefinitions(definitions, $"Восстановить тип запуска для служб: {definitions.Count}?");
    }

    private void RestoreDefinitions(IReadOnlyList<ServiceRepairDefinition> definitions, string confirmation)
    {
        if (MessageBox.Show(this, confirmation, "Восстановление служб", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            WindowsServiceRepairUtility.Restore(_session, definitions);
            RefreshRows();
            _statusLabel.Text = $"Восстановлено служб: {definitions.Count}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Восстановление служб", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void HighlightRows()
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is not ServiceRepairRow item)
            {
                continue;
            }

            if (item.Status == "отличается")
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 220);
            }
            else if (item.Status == "нет службы")
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 238, 238);
            }
        }
    }

    private void UpdateButtons()
    {
        var selected = _grid.CurrentRow?.DataBoundItem as ServiceRepairRow;
        _restoreSelectedButton.Enabled = selected is not null && selected.Status == "отличается";
        _restoreAllButton.Enabled = _rows.Any(row => row.Status == "отличается");
    }
}
