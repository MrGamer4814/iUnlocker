namespace IUnlocker;

public sealed class QuarantineForm : Form
{
    private readonly AppSession _session;
    private readonly DataGridView _grid = new();
    private readonly Button _refreshButton = new();
    private readonly Button _restoreButton = new();
    private readonly Button _deleteButton = new();
    private readonly Button _openButton = new();
    private readonly Label _statusLabel = new();

    public QuarantineForm(AppSession session)
    {
        _session = session;
        BuildInterface();
        Load += (_, _) => LoadItems();
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - карантин";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 520);
        ClientSize = new Size(1100, 620);
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

        ConfigureButton(_refreshButton, "Обновить", (_, _) => LoadItems(), primary: true);
        ConfigureButton(_restoreButton, "Восстановить", (_, _) => RestoreSelected());
        ConfigureButton(_deleteButton, "Удалить из карантина", (_, _) => DeleteSelected());
        ConfigureButton(_openButton, "Открыть папку карантина", (_, _) => OpenQuarantineFolder());

        toolbar.Controls.AddRange([_refreshButton, _restoreButton, _deleteButton, _openButton]);

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

        AddColumn(nameof(QuarantineItem.Name), "Файл", 180);
        AddColumn(nameof(QuarantineItem.Reason), "Причина", 260);
        AddColumn(nameof(QuarantineItem.Source), "Источник", 150);
        AddColumn(nameof(QuarantineItem.CreatedAtLocal), "Дата", 140);
        AddColumn(nameof(QuarantineItem.OriginalPath), "Исходный путь", 520);

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

    private void LoadItems()
    {
        var items = QuarantineManager.LoadItems().ToList();
        _grid.DataSource = items;
        _statusLabel.Text = $"В карантине: {items.Count}. Папка: {QuarantineManager.QuarantineDirectory}";
        UpdateButtons();
    }

    private QuarantineItem? GetSelectedItem()
    {
        return _grid.CurrentRow?.DataBoundItem as QuarantineItem;
    }

    private void RestoreSelected()
    {
        var item = GetSelectedItem();
        if (item is null)
        {
            return;
        }

        if (MessageBox.Show(this, $"Восстановить файл?\r\n\r\n{item.OriginalPath}", "Карантин", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            QuarantineManager.Restore(item);
            LoadItems();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Карантин", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void DeleteSelected()
    {
        var item = GetSelectedItem();
        if (item is null)
        {
            return;
        }

        if (MessageBox.Show(this, $"Удалить файл из карантина без восстановления?\r\n\r\n{item.Name}", "Карантин", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            QuarantineManager.Delete(item);
            LoadItems();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Карантин", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenQuarantineFolder()
    {
        Directory.CreateDirectory(QuarantineManager.QuarantineDirectory);
        var explorer = new FileExplorerForm(_session, QuarantineManager.QuarantineDirectory);
        explorer.Show(this);
    }

    private void UpdateButtons()
    {
        var hasSelection = GetSelectedItem() is not null;
        _restoreButton.Enabled = hasSelection;
        _deleteButton.Enabled = hasSelection;
    }
}
