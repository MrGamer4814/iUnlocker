namespace IUnlocker;

public sealed class BcdCheckForm : Form
{
    private readonly AppSession _session;
    private readonly ListView _entries = new();
    private readonly TextBox _detailsBox = new();
    private readonly Label _statusLabel = new();
    private readonly Button _refreshButton = new();
    private readonly Button _copyButton = new();
    private readonly CheckBox _showTechnicalBox = new();

    private IReadOnlyList<BcdEntry> _allEntries = [];
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

        _showTechnicalBox.Text = "Показать служебные записи";
        _showTechnicalBox.AutoSize = true;
        _showTechnicalBox.Margin = new Padding(12, 5, 0, 0);
        _showTechnicalBox.CheckedChanged += (_, _) => ApplyEntryFilter();

        toolbar.Controls.Add(_refreshButton);
        toolbar.Controls.Add(_copyButton);
        toolbar.Controls.Add(_showTechnicalBox);

        _entries.Dock = DockStyle.Fill;
        _entries.View = View.Details;
        _entries.FullRowSelect = true;
        _entries.HideSelection = false;
        _entries.Columns.Add("Запись", 240);
        _entries.Columns.Add("ID", 150);
        _entries.Columns.Add("Файл", 300);
        _entries.Columns.Add("Безопасный режим", 130);
        _entries.Columns.Add("Тестовый режим", 120);
        _entries.Columns.Add("Recovery", 100);
        _entries.Columns.Add("Политика", 150);
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

            _allEntries = BcdUtility.ParseEntries(result.Output);
            ApplyEntryFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Проверка BCD", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _refreshButton.Enabled = true;
            ShowSelectedDetails();
        }
    }

    private void ApplyEntryFilter()
    {
        var entries = _showTechnicalBox.Checked
            ? _allEntries
            : _allEntries.Where(IsUserFacingEntry).ToList();

        _entries.BeginUpdate();
        _entries.Items.Clear();
        foreach (var entry in entries)
        {
            var item = new ListViewItem(GetDisplayName(entry));
            item.SubItems.Add(string.IsNullOrWhiteSpace(entry.Identifier) ? "-" : entry.Identifier);
            item.SubItems.Add(string.IsNullOrWhiteSpace(entry.Path) ? "-" : entry.Path);
            item.SubItems.Add(NormalizeState(entry.SafeBoot));
            item.SubItems.Add(NormalizeBoolean(entry.TestSigning));
            item.SubItems.Add(NormalizeBoolean(entry.RecoveryEnabled));
            item.SubItems.Add(string.IsNullOrWhiteSpace(entry.BootStatusPolicy) ? "-" : entry.BootStatusPolicy);
            item.Tag = entry;

            if (!string.IsNullOrWhiteSpace(entry.SafeBoot) ||
                IsEnabledValue(entry.TestSigning))
            {
                item.BackColor = Color.FromArgb(255, 248, 220);
            }

            if (IsDisabledValue(entry.RecoveryEnabled))
            {
                item.ForeColor = Color.FromArgb(160, 40, 40);
            }

            _entries.Items.Add(item);
        }

        _entries.EndUpdate();
        _statusLabel.Text = $"{BcdUtility.GetTargetText(_session)}. Показано: {_entries.Items.Count} из {_allEntries.Count}.";
        ShowSelectedDetails();
    }

    private void ShowSelectedDetails()
    {
        _detailsBox.Text = _entries.SelectedItems.Count == 0
            ? _rawOutput
            : (_entries.SelectedItems[0].Tag as BcdEntry)?.Raw ?? string.Empty;
    }

    private static bool IsUserFacingEntry(BcdEntry entry)
    {
        var text = $"{entry.Section} {entry.Identifier} {entry.Description} {entry.Path}";
        if (IsTechnicalEntry(text))
        {
            return false;
        }

        return entry.Identifier.Equals("{bootmgr}", StringComparison.OrdinalIgnoreCase) ||
               entry.Identifier.Equals("{current}", StringComparison.OrdinalIgnoreCase) ||
               entry.Identifier.Equals("{default}", StringComparison.OrdinalIgnoreCase) ||
               ContainsAny(text, "Windows", "Загрузка Windows", "Диспетчер загрузки", "Recovery", "Восстановление", "Resume", "Возобновление", "winload", "winresume", "bootmgfw");
    }

    private static bool IsTechnicalEntry(string text)
    {
        return ContainsAny(
            text,
            "{emssettings}",
            "{dbgsettings}",
            "{hypervisorsettings}",
            "{globalsettings}",
            "{badmemory}",
            "{ramdiskoptions}",
            "{memdiag}",
            "EMS Settings",
            "Debugger Settings",
            "Hypervisor Settings",
            "Global Settings",
            "Bad Memory",
            "Memory Tester");
    }

    private static string GetDisplayName(BcdEntry entry)
    {
        var text = $"{entry.Section} {entry.Description} {entry.Path}";
        if (entry.Identifier.Equals("{bootmgr}", StringComparison.OrdinalIgnoreCase) ||
            entry.Path.Contains("bootmgfw", StringComparison.OrdinalIgnoreCase))
        {
            return "Диспетчер загрузки Windows";
        }

        if (entry.Path.Contains("winload", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(entry.Description)
                ? "Загрузчик Windows"
                : entry.Description;
        }

        if (entry.Path.Contains("winresume", StringComparison.OrdinalIgnoreCase) ||
            ContainsAny(text, "Resume", "Возобновление"))
        {
            return string.IsNullOrWhiteSpace(entry.Description)
                ? "Возобновление Windows"
                : entry.Description;
        }

        if (ContainsAny(text, "Recovery", "Восстановление"))
        {
            return string.IsNullOrWhiteSpace(entry.Description)
                ? "Среда восстановления Windows"
                : entry.Description;
        }

        if (!string.IsNullOrWhiteSpace(entry.Description))
        {
            return entry.Description;
        }

        if (!string.IsNullOrWhiteSpace(entry.Section))
        {
            return entry.Section;
        }

        return string.IsNullOrWhiteSpace(entry.Identifier) ? "Запись BCD" : entry.Identifier;
    }

    private static string NormalizeState(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private static string NormalizeBoolean(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        if (IsEnabledValue(value))
        {
            return "Включено";
        }

        if (IsDisabledValue(value))
        {
            return "Отключено";
        }

        return value;
    }

    private static bool IsEnabledValue(string value)
    {
        return value.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Да", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("On", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("True", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDisabledValue(string value)
    {
        return value.Equals("No", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Нет", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Off", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("False", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }
}
