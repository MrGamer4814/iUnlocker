using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;

namespace IUnlocker;

public sealed class EventLogViewerForm : Form
{
    private readonly AppSession _session;
    private readonly SplitContainer _split = new();
    private readonly TreeView _logsTree = new();
    private readonly DataGridView _eventsGrid = new();
    private readonly Label _statusLabel = new();
    private readonly Button _refreshButton = new();
    private readonly NumericUpDown _limitBox = new();

    private EventLogSource? _selectedLog;

    public EventLogViewerForm(AppSession session)
    {
        _session = session;
        BuildInterface();
        Shown += (_, _) =>
        {
            SetSafeSplitterDistance();
            LoadLogsTree();
        };
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - журнал событий";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(980, 620);
        ClientSize = new Size(1180, 720);
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

        _refreshButton.Text = "Обновить";
        _refreshButton.AutoSize = true;
        _refreshButton.Margin = new Padding(0, 0, 10, 0);
        _refreshButton.Click += (_, _) => LoadSelectedLog();
        UiTheme.StyleButton(_refreshButton, primary: true);

        var limitLabel = new Label
        {
            Text = "Записей:",
            AutoSize = true,
            Margin = new Padding(0, 6, 6, 0),
        };
        _limitBox.Minimum = 50;
        _limitBox.Maximum = 2000;
        _limitBox.Value = 300;
        _limitBox.Increment = 50;
        _limitBox.Width = 80;
        _limitBox.Margin = new Padding(0, 2, 12, 0);

        toolbar.Controls.Add(_refreshButton);
        toolbar.Controls.Add(limitLabel);
        toolbar.Controls.Add(_limitBox);

        _logsTree.Dock = DockStyle.Fill;
        _logsTree.HideSelection = false;
        _logsTree.AfterSelect += (_, args) =>
        {
            _selectedLog = args.Node?.Tag as EventLogSource;
            LoadSelectedLog();
        };
        UiTheme.StyleTree(_logsTree);

        _eventsGrid.Dock = DockStyle.Fill;
        _eventsGrid.AllowUserToAddRows = false;
        _eventsGrid.AllowUserToDeleteRows = false;
        _eventsGrid.AllowUserToResizeRows = false;
        _eventsGrid.ReadOnly = true;
        _eventsGrid.RowHeadersVisible = false;
        _eventsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _eventsGrid.MultiSelect = false;
        _eventsGrid.AutoGenerateColumns = false;
        _eventsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _eventsGrid.DataBindingComplete += (_, _) => HighlightRows();
        UiTheme.StyleGrid(_eventsGrid);
        AddColumn("TimeCreated", "Время", 150);
        AddColumn("Level", "Уровень", 110);
        AddColumn("EventId", "ID", 70);
        AddColumn("Provider", "Источник", 220);
        AddColumn("Message", "Сообщение", 560);

        _split.Dock = DockStyle.Fill;
        _split.Orientation = Orientation.Vertical;
        _split.FixedPanel = FixedPanel.Panel1;
        _split.SizeChanged += (_, _) => SetSafeSplitterDistance();
        _split.Panel1.Controls.Add(_logsTree);
        _split.Panel2.Controls.Add(_eventsGrid);

        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = UiTheme.MutedText;
        _statusLabel.Padding = new Padding(0, 10, 0, 0);

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_split, 0, 1);
        root.Controls.Add(_statusLabel, 0, 2);
        Controls.Add(root);
    }

    private void SetSafeSplitterDistance()
    {
        if (_split.Width <= 0)
        {
            return;
        }

        const int panel1MinSize = 120;
        const int panel2MinSize = 220;
        var min = Math.Max(0, panel1MinSize);
        var max = Math.Max(min, _split.Width - _split.SplitterWidth - panel2MinSize);
        if (max <= min)
        {
            return;
        }

        var desired = Math.Min(260, max);
        _split.SplitterDistance = Math.Clamp(desired, min, max);
    }

    private void AddColumn(string property, string header, int width)
    {
        _eventsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            Name = property,
            Width = width,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });
    }

    private void LoadLogsTree()
    {
        _logsTree.BeginUpdate();
        try
        {
            _logsTree.Nodes.Clear();
            var root = new TreeNode("Журналы Windows");
            foreach (var log in GetKnownLogs())
            {
                if (!log.IsLive && !File.Exists(log.Path))
                {
                    continue;
                }

                root.Nodes.Add(new TreeNode(log.Name)
                {
                    Tag = log,
                });
            }

            _logsTree.Nodes.Add(root);
            root.Expand();
            if (root.Nodes.Count > 0)
            {
                _logsTree.SelectedNode = root.Nodes[0];
            }

            _statusLabel.Text = root.Nodes.Count == 0
                ? "Файлы журналов событий не найдены."
                : $"Найдено журналов: {root.Nodes.Count}.";
        }
        finally
        {
            _logsTree.EndUpdate();
        }
    }

    private IEnumerable<EventLogSource> GetKnownLogs()
    {
        var logsRoot = _session.WindowsPath is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "winevt", "Logs")
            : Path.Combine(_session.WindowsPath, "System32", "winevt", "Logs");

        var live = !_session.IsWinPe;
        yield return new EventLogSource("System", Path.Combine(logsRoot, "System.evtx"), live);
        yield return new EventLogSource("Application", Path.Combine(logsRoot, "Application.evtx"), live);
        yield return new EventLogSource("Security", Path.Combine(logsRoot, "Security.evtx"), live);
        yield return new EventLogSource("Setup", Path.Combine(logsRoot, "Setup.evtx"), live);
    }

    private void LoadSelectedLog()
    {
        if (_selectedLog is null)
        {
            return;
        }

        Cursor = Cursors.WaitCursor;
        _refreshButton.Enabled = false;
        try
        {
            var rows = QueryEvents(_selectedLog, (int)_limitBox.Value);
            _eventsGrid.DataSource = rows;
            _statusLabel.Text = $"{_selectedLog.Name}: показано {rows.Count} последних записей.";
        }
        catch (Exception ex)
        {
            _eventsGrid.DataSource = Array.Empty<EventLogRow>();
            _statusLabel.Text = $"Ошибка чтения журнала: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Журнал событий", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _refreshButton.Enabled = true;
            Cursor = Cursors.Default;
        }
    }

    private static List<EventLogRow> QueryEvents(EventLogSource source, int limit)
    {
        try
        {
            return QueryEventsWithReader(source, limit);
        }
        catch when (source.IsLive)
        {
            return QueryLiveClassicEventLog(source.Name, limit);
        }
    }

    private static List<EventLogRow> QueryEventsWithReader(EventLogSource source, int limit)
    {
        var rows = new List<EventLogRow>();
        var query = new EventLogQuery(
            source.IsLive ? source.Name : source.Path,
            source.IsLive ? PathType.LogName : PathType.FilePath)
        {
            ReverseDirection = true,
        };

        using var reader = new EventLogReader(query);
        while (rows.Count < limit && reader.ReadEvent() is { } record)
        {
            using (record)
            {
                rows.Add(ReadEventRecordSafely(record));
            }
        }

        return rows;
    }

    private static List<EventLogRow> QueryLiveClassicEventLog(string logName, int limit)
    {
        using var log = new EventLog(logName);
        var rows = new List<EventLogRow>();
        for (var index = log.Entries.Count - 1; index >= 0 && rows.Count < limit; index--)
        {
            var entry = log.Entries[index];
            rows.Add(new EventLogRow(
                entry.TimeGenerated.ToString("yyyy-MM-dd HH:mm:ss"),
                entry.EntryType.ToString(),
                entry.InstanceId.ToString(),
                entry.Source,
                NormalizeMessage(entry.Message ?? string.Empty)));
        }

        return rows;
    }

    private static EventLogRow ReadEventRecordSafely(EventRecord record)
    {
        return new EventLogRow(
            GetTimeText(record),
            GetLevelText(record),
            GetEventId(record),
            GetProviderName(record),
            GetEventMessage(record));
    }

    private static string GetTimeText(EventRecord record)
    {
        try
        {
            return record.TimeCreated?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetEventId(EventRecord record)
    {
        try
        {
            return record.Id.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetProviderName(EventRecord record)
    {
        try
        {
            return record.ProviderName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetLevelText(EventRecord record)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(record.LevelDisplayName))
            {
                return record.LevelDisplayName;
            }
        }
        catch
        {
            // Provider metadata can be missing in WinPE/offline logs.
        }

        try
        {
            return record.Level is null ? string.Empty : LevelFromCode(record.Level.Value.ToString());
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetEventMessage(EventRecord record)
    {
        try
        {
            var message = record.FormatDescription();
            if (!string.IsNullOrWhiteSpace(message))
            {
                return NormalizeMessage(message);
            }
        }
        catch
        {
            // Some offline logs do not have provider metadata available.
        }

        try
        {
            var values = record.Properties
                .Select(property => Convert.ToString(property.Value))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Take(10);
            var fallback = string.Join("; ", values);
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return NormalizeMessage(fallback);
            }
        }
        catch
        {
            // Keep going to XML fallback.
        }

        try
        {
            return NormalizeMessage(record.ToXml());
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizeMessage(string message)
    {
        return message
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static string LevelFromCode(string code)
    {
        return code switch
        {
            "1" => "Критический",
            "2" => "Ошибка",
            "3" => "Предупреждение",
            "4" => "Сведения",
            "5" => "Подробно",
            _ => code,
        };
    }

    private void HighlightRows()
    {
        foreach (DataGridViewRow row in _eventsGrid.Rows)
        {
            if (row.DataBoundItem is not EventLogRow entry)
            {
                continue;
            }

            if (entry.Level.Contains("Крит", StringComparison.OrdinalIgnoreCase) ||
                entry.Level.Contains("Critical", StringComparison.OrdinalIgnoreCase))
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 224, 224);
            }
            else if (entry.Level.Contains("Ошибка", StringComparison.OrdinalIgnoreCase) ||
                     entry.Level.Contains("Error", StringComparison.OrdinalIgnoreCase))
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 238, 238);
            }
            else if (entry.Level.Contains("Пред", StringComparison.OrdinalIgnoreCase) ||
                     entry.Level.Contains("Warn", StringComparison.OrdinalIgnoreCase))
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 220);
            }
        }
    }

    private sealed record EventLogRow(
        string TimeCreated,
        string Level,
        string EventId,
        string Provider,
        string Message);

    private sealed record EventLogSource(string Name, string Path, bool IsLive);
}
