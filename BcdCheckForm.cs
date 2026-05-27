namespace IUnlocker;

public sealed class BcdCheckForm : Form
{
    private readonly AppSession _session;
    private readonly ListView _entries = new();
    private readonly TextBox _detailsBox = new();
    private readonly Label _statusLabel = new();
    private readonly Button _refreshButton = new();
    private readonly Button _copyButton = new();

    private string _rawOutput = string.Empty;

    public BcdCheckForm(AppSession session)
    {
        _session = session;
        BuildInterface();
        Shown += (_, _) => RefreshBcd();
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - проверка BCD";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(940, 560);
        ClientSize = new Size(1120, 700);
        UiTheme.ApplyForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        };

        _refreshButton.Text = "Обновить";
        _refreshButton.AutoSize = true;
        _refreshButton.Margin = new Padding(0, 0, 8, 0);
        _refreshButton.Click += (_, _) => RefreshBcd();
        UiTheme.StyleButton(_refreshButton, primary: true);

        _copyButton.Text = "Копировать вывод";
        _copyButton.AutoSize = true;
        _copyButton.Margin = new Padding(0, 0, 8, 0);
        _copyButton.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_rawOutput))
            {
                Clipboard.SetText(_rawOutput);
            }
        };
        UiTheme.StyleButton(_copyButton);
        toolbar.Controls.Add(_refreshButton);
        toolbar.Controls.Add(_copyButton);

        _entries.Dock = DockStyle.Fill;
        _entries.View = View.Details;
        _entries.FullRowSelect = true;
        _entries.HideSelection = false;
        _entries.Columns.Add("Идентификатор", 160);
        _entries.Columns.Add("Описание", 250);
        _entries.Columns.Add("Путь", 260);
        _entries.Columns.Add("SafeBoot", 100);
        _entries.Columns.Add("Test", 80);
        _entries.Columns.Add("Recovery", 90);
        _entries.Columns.Add("BootStatusPolicy", 160);
        _entries.SelectedIndexChanged += (_, _) => ShowSelectedDetails();
        UiTheme.StyleListView(_entries);

        _detailsBox.Dock = DockStyle.Fill;
        _detailsBox.Multiline = true;
        _detailsBox.ReadOnly = true;
        _detailsBox.ScrollBars = ScrollBars.Both;
        _detailsBox.WordWrap = false;
        UiTheme.StyleTextBox(_detailsBox);

        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = UiTheme.MutedText;
        _statusLabel.Padding = new Padding(0, 10, 0, 0);

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_entries, 0, 1);
        root.Controls.Add(_detailsBox, 0, 2);
        root.Controls.Add(_statusLabel, 0, 3);
        Controls.Add(root);
    }

    private void RefreshBcd()
    {
        _refreshButton.Enabled = false;
        try
        {
            var arguments = BcdUtility.GetEnumAllArguments(_session);
            var result = BcdUtility.RunBcdEdit(arguments);
            _rawOutput = result.Output;
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Output)
                    ? "bcdedit завершился с ошибкой."
                    : result.Output.Trim());
            }

            var entries = BcdUtility.ParseEntries(result.Output);
            _entries.BeginUpdate();
            _entries.Items.Clear();
            foreach (var entry in entries)
            {
                var item = new ListViewItem(string.IsNullOrWhiteSpace(entry.Identifier) ? entry.Section : entry.Identifier);
                item.SubItems.Add(entry.Description);
                item.SubItems.Add(entry.Path);
                item.SubItems.Add(entry.SafeBoot);
                item.SubItems.Add(entry.TestSigning);
                item.SubItems.Add(entry.RecoveryEnabled);
                item.SubItems.Add(entry.BootStatusPolicy);
                item.Tag = entry;

                if (!string.IsNullOrWhiteSpace(entry.SafeBoot) ||
                    entry.TestSigning.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
                    entry.TestSigning.Equals("Да", StringComparison.OrdinalIgnoreCase))
                {
                    item.BackColor = Color.FromArgb(255, 248, 220);
                }

                _entries.Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Проверка BCD", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _entries.EndUpdate();
            _refreshButton.Enabled = true;
            _statusLabel.Text = $"{BcdUtility.GetTargetText(_session)}. Записей: {_entries.Items.Count}.";
            ShowSelectedDetails();
        }
    }

    private void ShowSelectedDetails()
    {
        _detailsBox.Text = _entries.SelectedItems.Count == 0
            ? _rawOutput
            : (_entries.SelectedItems[0].Tag as BcdEntry)?.Raw ?? string.Empty;
    }
}
