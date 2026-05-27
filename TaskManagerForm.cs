using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace IUnlocker;

public sealed class TaskManagerForm : Form
{
    private const int RootParentPid = -1;
    private const int MetadataRefreshSeconds = 15;
    private const uint ProcessSuspendResume = 0x0800;
    private const uint ProcessSetInformation = 0x0200;
    private const int ProcessBreakOnTermination = 29;
    private const uint SeDebugPrivilege = 20;

    private readonly AppSession _session;
    private readonly DataGridView _grid = new();
    private readonly TextBox _searchBox = new();
    private readonly CheckBox _hideSignedBox = new();
    private readonly Button _refreshButton = new();
    private readonly Label _statusLabel = new();
    private readonly ContextMenuStrip _processMenu = new();
    private readonly ToolStripMenuItem _terminateMenuItem = new("Завершить") { ShortcutKeyDisplayString = "Del" };
    private readonly ToolStripMenuItem _terminateTreeMenuItem = new("Завершить дерево") { ShortcutKeyDisplayString = "Shift+Del" };
    private readonly ToolStripMenuItem _suspendMenuItem = new("Приостановить");
    private readonly ToolStripMenuItem _suspendTreeMenuItem = new("Приостановить дерево");
    private readonly ToolStripMenuItem _criticalMenuItem = new("Сделать критичным");
    private readonly ToolStripMenuItem _openLocationMenuItem = new("Открыть расположение в iUnlocker") { ShortcutKeyDisplayString = "Ctrl+Enter" };
    private readonly ToolStripMenuItem _propertiesMenuItem = new("Свойства") { ShortcutKeyDisplayString = "Enter" };
    private readonly ToolStripMenuItem _copyMenuItem = new("Копировать") { ShortcutKeyDisplayString = "Ctrl+C" };
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly ToolTip _processToolTip = new() { AutomaticDelay = 250, AutoPopDelay = 12000, ReshowDelay = 100 };
    private readonly Dictionary<int, ulong> _lastProcessorTimes = [];
    private readonly Dictionary<int, double> _cpuLoad = [];
    private Dictionary<int, ProcessMetadata> _metadataByPid = [];
    private readonly HashSet<int> _expandedPids = [];
    private readonly HashSet<int> _collapsedPids = [];
    private readonly HashSet<int> _suspendedPids = [];
    private readonly Dictionary<string, Image> _processIconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly BindingList<ProcessDisplayRow> _displayRows = [];

    private List<ProcessRow> _rows = [];
    private Dictionary<int, List<ProcessRow>> _childrenByParent = [];
    private DateTime _lastSampleTimeUtc = DateTime.UtcNow;
    private DateTime _lastMetadataRefreshUtc = DateTime.MinValue;
    private bool _refreshing;
    private bool _contextMenuOpenedOnTreeCell;
    private Point _lastMouseLocation;
    private int _lastTooltipPid = int.MinValue;
    private string _lastTooltipText = string.Empty;
    private string? _sortProperty = nameof(ProcessDisplayRow.Pid);
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;

    public TaskManagerForm(AppSession session)
    {
        _session = session;
        BuildInterface();
        Load += (_, _) => BeginInvoke(new Action(RefreshProcesses));
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - диспетчер задач";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 560);
        ClientSize = new Size(1220, 720);
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

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _searchBox.PlaceholderText = "Поиск по PID или имени процесса";
        _searchBox.Dock = DockStyle.Fill;
        _searchBox.Margin = new Padding(0, 0, 8, 0);
        _searchBox.TextChanged += (_, _) => ApplyView();
        UiTheme.StyleTextBox(_searchBox);

        _hideSignedBox.Text = "Скрыть подписанные";
        _hideSignedBox.AutoSize = true;
        _hideSignedBox.Anchor = AnchorStyles.Left;
        _hideSignedBox.Margin = new Padding(0, 3, 12, 0);
        _hideSignedBox.CheckedChanged += (_, _) => ApplyView();
        UiTheme.StyleCheckBox(_hideSignedBox);

        _refreshButton.Text = "Обновить";
        _refreshButton.AutoSize = true;
        _refreshButton.Margin = new Padding(0);
        _refreshButton.Click += (_, _) => RefreshProcesses();
        UiTheme.StyleButton(_refreshButton, primary: true);
        toolbar.Controls.Add(_searchBox, 0, 0);
        toolbar.Controls.Add(_hideSignedBox, 1, 0);
        toolbar.Controls.Add(_refreshButton, 2, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AllowUserToOrderColumns = true;
        _grid.AllowUserToResizeColumns = true;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        UiTheme.StyleGrid(_grid);
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _grid.MultiSelect = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.RowTemplate.Height = 24;
        _grid.ScrollBars = ScrollBars.Both;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.ShowCellToolTips = false;
        _grid.DataSource = _displayRows;
        _grid.CellClick += GridCellClick;
        _grid.CellDoubleClick += GridCellDoubleClick;
        _grid.CellPainting += GridCellPainting;
        _grid.CellToolTipTextNeeded += GridCellToolTipTextNeeded;
        _grid.ColumnHeaderMouseClick += GridColumnHeaderMouseClick;
        _grid.CellMouseDown += GridCellMouseDown;
        _grid.CellMouseMove += GridCellMouseMove;
        _grid.MouseLeave += (_, _) => HideProcessToolTip();
        _grid.KeyDown += GridKeyDown;
        _grid.DataBindingComplete += (_, _) => HighlightRows();
        EnableDoubleBuffering(_grid);

        AddColumn(nameof(ProcessDisplayRow.Name), "Имя процесса", 280);
        AddColumn(nameof(ProcessDisplayRow.Pid), "PID", 80);
        AddColumn(nameof(ProcessDisplayRow.Criticality), "Критичность", 130);
        AddColumn(nameof(ProcessDisplayRow.Load), "Нагрузка", 95);
        AddColumn(nameof(ProcessDisplayRow.Memory), "Память", 95);
        AddColumn(nameof(ProcessDisplayRow.Description), "Описание", 300);
        AddColumn(nameof(ProcessDisplayRow.SignatureStatus), "Подпись", 140);
        AddColumn(nameof(ProcessDisplayRow.SignaturePublisher), "Издатель", 190);
        AddColumn(nameof(ProcessDisplayRow.FilePath), "Путь файла", 520);

        _terminateMenuItem.Click += (_, _) => TerminateSelectedProcess(tree: false);
        _terminateTreeMenuItem.Click += (_, _) => TerminateSelectedProcess(tree: true);
        _suspendMenuItem.Click += (_, _) => SuspendSelectedProcess(tree: false);
        _suspendTreeMenuItem.Click += (_, _) => SuspendSelectedProcess(tree: true);
        _criticalMenuItem.Click += (_, _) => ToggleSelectedCriticality();
        _openLocationMenuItem.Click += (_, _) => OpenSelectedLocationInIUnlocker();
        _propertiesMenuItem.Click += (_, _) => ShowSelectedProperties();
        _copyMenuItem.Click += (_, _) => CopySelectedProcess();
        _processMenu.Opening += (_, e) =>
        {
            UpdateProcessMenu();
            UiTheme.HideUnavailableContextMenuItems(_processMenu);
        };
        _processMenu.Items.AddRange(new ToolStripItem[]
        {
            _terminateMenuItem,
            _terminateTreeMenuItem,
            _suspendMenuItem,
            _suspendTreeMenuItem,
            new ToolStripSeparator(),
            _criticalMenuItem,
            new ToolStripSeparator(),
            new ToolStripSeparator(),
            _openLocationMenuItem,
            _propertiesMenuItem,
            _copyMenuItem,
        });
        _grid.ContextMenuStrip = _processMenu;

        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(0, 10, 0, 0);
        _statusLabel.ForeColor = UiTheme.MutedText;
        _statusLabel.Text = string.Empty;
        _statusLabel.Visible = false;

        _refreshTimer.Interval = 1000;
        _refreshTimer.Tick += (_, _) => RefreshProcesses();
        _refreshTimer.Start();

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(_statusLabel, 0, 2);
        Controls.Add(root);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refreshTimer.Stop();
        _processToolTip.Dispose();
        foreach (var image in _processIconCache.Values)
        {
            image.Dispose();
        }

        _processIconCache.Clear();
        base.OnFormClosed(e);
    }

    private void AddColumn(string property, string header, int width)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            Name = property,
            Width = width,
            Resizable = DataGridViewTriState.True,
            SortMode = DataGridViewColumnSortMode.Programmatic,
        });
    }

    private static void EnableDoubleBuffering(DataGridView grid)
    {
        typeof(DataGridView)
            .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(grid, true, null);
    }

    private async void RefreshProcesses()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        _refreshButton.Enabled = false;
        _statusLabel.Visible = false;
        _statusLabel.Text = string.Empty;

        try
        {
            var now = DateTime.UtcNow;
            var elapsedSeconds = Math.Max(0.1, (now - _lastSampleTimeUtc).TotalSeconds);
            var refreshMetadata = _metadataByPid.Count == 0 ||
                                  (now - _lastMetadataRefreshUtc).TotalSeconds >= MetadataRefreshSeconds;
            var cachedMetadata = new Dictionary<int, ProcessMetadata>(_metadataByPid);
            var snapshot = await Task.Run(() => ReadProcessInfos(cachedMetadata, refreshMetadata));
            var processInfos = snapshot.Processes;
            var currentPids = processInfos.Select(info => info.Pid).ToHashSet();

            if (snapshot.UpdatedMetadata is not null)
            {
                _metadataByPid = snapshot.UpdatedMetadata
                    .Where(pair => currentPids.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
                _lastMetadataRefreshUtc = now;
            }

            foreach (var info in processInfos)
            {
                if (_lastProcessorTimes.TryGetValue(info.Pid, out var previousProcessorTime) &&
                    info.ProcessorTime >= previousProcessorTime)
                {
                    var processorSeconds = (info.ProcessorTime - previousProcessorTime) / 10_000_000d;
                    _cpuLoad[info.Pid] = processorSeconds / elapsedSeconds / Math.Max(1, Environment.ProcessorCount) * 100;
                }
                else
                {
                    _cpuLoad.TryAdd(info.Pid, 0);
                }

                _lastProcessorTimes[info.Pid] = info.ProcessorTime;
            }

            foreach (var stalePid in _lastProcessorTimes.Keys.Where(pid => !currentPids.Contains(pid)).ToList())
            {
                _lastProcessorTimes.Remove(stalePid);
                _cpuLoad.Remove(stalePid);
                _expandedPids.Remove(stalePid);
                _collapsedPids.Remove(stalePid);
                _suspendedPids.Remove(stalePid);
            }

            _lastSampleTimeUtc = now;
            _rows = processInfos
                .Select(info =>
                {
                    var suspicious = IsSuspicious(info.Name, info.FilePath) || IsBadSignature(info.SignatureStatus);
                    var criticality = suspicious
                        ? "Подозрительный"
                        : GetCriticality(info.Name, info.FilePath, info.IsCritical, info.IsProtected);
                    return new ProcessRow(
                        info.Name,
                        info.Pid,
                        info.ParentPid,
                        criticality,
                        $"{_cpuLoad.GetValueOrDefault(info.Pid):0.0}%",
                        info.WorkingSetBytes,
                        FormatBytes(info.WorkingSetBytes),
                        info.Description,
                        info.SignatureStatus,
                        info.SignaturePublisher,
                        info.FilePath,
                        info.Company,
                        info.IsCritical,
                        suspicious);
                })
                .ToList();

            RebuildTreeIndex();
            ApplyView();
        }
        catch (Exception ex)
        {
            _statusLabel.Visible = true;
            _statusLabel.Text = $"Не удалось обновить процессы: {ex.Message}";
        }
        finally
        {
            _refreshButton.Enabled = true;
            _refreshing = false;
        }
    }

    private void RebuildTreeIndex()
    {
        var existingRowsByPid = _rows.ToDictionary(row => row.Pid);
        _childrenByParent = _rows
            .GroupBy(row => GetSafeParentPid(row, existingRowsByPid))
            .ToDictionary(group => group.Key, group => SortSiblings(group).ToList());

        foreach (var parentPid in _childrenByParent.Keys.Where(pid => pid > 0))
        {
            if (!_collapsedPids.Contains(parentPid))
            {
                _expandedPids.Add(parentPid);
            }
        }
    }

    private static int GetSafeParentPid(ProcessRow row, Dictionary<int, ProcessRow> existingRowsByPid)
    {
        if (row.Pid <= 0 ||
            row.Name.Equals("Idle", StringComparison.OrdinalIgnoreCase) ||
            row.Name.Equals("System Idle Process", StringComparison.OrdinalIgnoreCase) ||
            row.ParentPid is not int parentPid ||
            parentPid <= 0 ||
            parentPid == row.Pid)
        {
            return RootParentPid;
        }

        if (!existingRowsByPid.TryGetValue(parentPid, out var parent) ||
            parent.Pid <= 0 ||
            parent.Name.Equals("Idle", StringComparison.OrdinalIgnoreCase) ||
            parent.Name.Equals("System Idle Process", StringComparison.OrdinalIgnoreCase))
        {
            return RootParentPid;
        }

        return parentPid;
    }

    private void ApplyView()
    {
        var selectedPid = GetSelectedPid();
        var firstVisiblePid = GetFirstVisiblePid();
        var firstVisibleIndex = GetFirstVisibleIndex();

        var displayRows = new List<ProcessDisplayRow>();
        var visiblePids = GetFilterVisiblePids();
        if (_childrenByParent.TryGetValue(RootParentPid, out var roots))
        {
            foreach (var row in roots)
            {
                AddDisplayRows(displayRows, row, depth: 0, [], visiblePids);
            }
        }

        UpdateDisplayRows(displayRows);
        UpdateSortGlyph();
        RestoreGridPosition(displayRows, selectedPid, firstVisiblePid, firstVisibleIndex);
        RefreshTooltipUnderMouse();
        _statusLabel.Visible = false;
        _statusLabel.Text = string.Empty;
    }

    private void UpdateDisplayRows(IReadOnlyList<ProcessDisplayRow> displayRows)
    {
        _displayRows.RaiseListChangedEvents = false;
        try
        {
            _displayRows.Clear();
            foreach (var row in displayRows)
            {
                _displayRows.Add(row);
            }
        }
        finally
        {
            _displayRows.RaiseListChangedEvents = true;
            _displayRows.ResetBindings();
        }
    }

    private int? GetSelectedPid()
    {
        return _grid.CurrentRow?.DataBoundItem is ProcessDisplayRow row ? row.Pid : null;
    }

    private int? GetFirstVisiblePid()
    {
        if (_grid.Rows.Count == 0)
        {
            return null;
        }

        var index = _grid.FirstDisplayedScrollingRowIndex;
        return index >= 0 &&
               index < _grid.Rows.Count &&
               _grid.Rows[index].DataBoundItem is ProcessDisplayRow row
            ? row.Pid
            : null;
    }

    private int GetFirstVisibleIndex()
    {
        return _grid.Rows.Count == 0 ? 0 : Math.Max(0, _grid.FirstDisplayedScrollingRowIndex);
    }

    private void RestoreGridPosition(
        IReadOnlyList<ProcessDisplayRow> displayRows,
        int? selectedPid,
        int? firstVisiblePid,
        int firstVisibleIndex)
    {
        if (_grid.Rows.Count == 0)
        {
            return;
        }

        var visibleIndex = firstVisiblePid is int pid
            ? displayRows.ToList().FindIndex(row => row.Pid == pid)
            : -1;
        if (visibleIndex < 0)
        {
            visibleIndex = Math.Min(firstVisibleIndex, _grid.Rows.Count - 1);
        }

        try
        {
            _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, visibleIndex);
        }
        catch
        {
            // Row can become temporarily invisible while DataGridView refreshes.
        }

        if (selectedPid is int selected)
        {
            var selectedIndex = displayRows.ToList().FindIndex(row => row.Pid == selected);
            if (selectedIndex >= 0 && selectedIndex < _grid.Rows.Count)
            {
                _grid.ClearSelection();
                _grid.Rows[selectedIndex].Selected = true;
                if (selectedIndex >= visibleIndex &&
                    selectedIndex < visibleIndex + Math.Max(1, _grid.DisplayedRowCount(includePartialRow: true)))
                {
                    _grid.CurrentCell = _grid.Rows[selectedIndex].Cells[0];
                }
            }
        }
    }

    private HashSet<int>? GetFilterVisiblePids()
    {
        var query = _searchBox.Text.Trim();
        var hideSigned = _hideSignedBox.Checked;
        if (string.IsNullOrWhiteSpace(query) && !hideSigned)
        {
            return null;
        }

        var matches = _rows
            .Where(row =>
                (string.IsNullOrWhiteSpace(query) ||
                 row.Pid.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 row.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)) &&
                (!hideSigned || !IsValidSignature(row.SignatureStatus)))
            .Select(row => row.Pid)
            .ToHashSet();

        if (matches.Count == 0)
        {
            return [];
        }

        var rowsByPid = _rows.ToDictionary(row => row.Pid);
        var visible = new HashSet<int>(matches);
        foreach (var pid in matches)
        {
            var currentPid = pid;
            while (rowsByPid.TryGetValue(currentPid, out var row) &&
                   row.ParentPid is int parentPid &&
                   rowsByPid.ContainsKey(parentPid) &&
                   visible.Add(parentPid))
            {
                currentPid = parentPid;
            }
        }

        return visible;
    }

    private static bool IsValidSignature(string signatureStatus)
    {
        return signatureStatus.Equals("Действительна", StringComparison.OrdinalIgnoreCase);
    }

    private void AddDisplayRows(
        List<ProcessDisplayRow> displayRows,
        ProcessRow row,
        int depth,
        HashSet<int> visited,
        HashSet<int>? visiblePids)
    {
        if (!visited.Add(row.Pid))
        {
            return;
        }

        if (visiblePids is not null && !visiblePids.Contains(row.Pid))
        {
            return;
        }

        var hasChildren = row.Pid > 0 && _childrenByParent.ContainsKey(row.Pid);
        var expanded = visiblePids is not null || (hasChildren && _expandedPids.Contains(row.Pid));
        displayRows.Add(ProcessDisplayRow.From(row, depth, hasChildren, expanded));

        if (!expanded || !_childrenByParent.TryGetValue(row.Pid, out var children))
        {
            return;
        }

        foreach (var child in children)
        {
            AddDisplayRows(displayRows, child, depth + 1, visited, visiblePids);
        }
    }

    private IEnumerable<ProcessRow> SortSiblings(IEnumerable<ProcessRow> rows)
    {
        if (string.IsNullOrWhiteSpace(_sortProperty))
        {
            return rows.OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(row => row.Pid);
        }

        return _sortProperty switch
        {
            nameof(ProcessDisplayRow.Pid) => _sortDirection == ListSortDirection.Ascending
                ? rows.OrderBy(row => row.Pid)
                : rows.OrderByDescending(row => row.Pid),
            nameof(ProcessDisplayRow.Memory) => _sortDirection == ListSortDirection.Ascending
                ? rows.OrderBy(row => row.MemoryBytes)
                : rows.OrderByDescending(row => row.MemoryBytes),
            _ => _sortDirection == ListSortDirection.Ascending
                ? rows.OrderBy(GetSortValue, StringComparer.CurrentCultureIgnoreCase).ThenBy(row => row.Pid)
                : rows.OrderByDescending(GetSortValue, StringComparer.CurrentCultureIgnoreCase).ThenBy(row => row.Pid),
        };
    }

    private string GetSortValue(ProcessRow row)
    {
        return _sortProperty switch
        {
            nameof(ProcessDisplayRow.Name) => row.Name,
            nameof(ProcessDisplayRow.Criticality) => row.Criticality,
            nameof(ProcessDisplayRow.Load) => row.Load,
            nameof(ProcessDisplayRow.Description) => row.Description,
            nameof(ProcessDisplayRow.SignatureStatus) => row.SignatureStatus,
            nameof(ProcessDisplayRow.SignaturePublisher) => row.SignaturePublisher,
            nameof(ProcessDisplayRow.FilePath) => row.FilePath,
            _ => string.Empty,
        };
    }

    private void GridCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != 0 ||
            _grid.Rows[e.RowIndex].DataBoundItem is not ProcessDisplayRow row ||
            !row.HasChildren)
        {
            return;
        }

        if (!_expandedPids.Add(row.Pid))
        {
            _expandedPids.Remove(row.Pid);
            _collapsedPids.Add(row.Pid);
        }
        else
        {
            _collapsedPids.Remove(row.Pid);
        }

        ApplyView();
    }

    private void GridCellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || e.RowIndex < 0)
        {
            return;
        }

        _grid.ClearSelection();
        _grid.Rows[e.RowIndex].Selected = true;
        _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
        _contextMenuOpenedOnTreeCell = e.ColumnIndex == 0;
    }

    private void GridCellMouseMove(object? sender, DataGridViewCellMouseEventArgs e)
    {
        _lastMouseLocation = _grid.PointToClient(Cursor.Position);
        if (e.RowIndex < 0 ||
            e.RowIndex >= _grid.Rows.Count ||
            _grid.Rows[e.RowIndex].DataBoundItem is not ProcessDisplayRow row)
        {
            HideProcessToolTip();
            return;
        }

        ShowProcessToolTip(row, _lastMouseLocation);
    }

    private void GridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete && e.Shift)
        {
            TerminateSelectedProcess(tree: true);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Delete)
        {
            TerminateSelectedProcess(tree: false);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter && e.Control)
        {
            OpenSelectedLocationInIUnlocker();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter)
        {
            ShowSelectedProperties();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.C && e.Control)
        {
            CopySelectedProcess();
            e.Handled = true;
        }
    }

    private ProcessDisplayRow? GetSelectedDisplayRow()
    {
        return _grid.CurrentRow?.DataBoundItem as ProcessDisplayRow;
    }

    private ProcessRow? GetProcessRow(int pid)
    {
        return _rows.FirstOrDefault(row => row.Pid == pid);
    }

    private void UpdateProcessMenu()
    {
        var row = GetSelectedDisplayRow();
        var hasProcess = row is not null && row.Pid > 0;
        var hasPath = hasProcess && !string.IsNullOrWhiteSpace(row!.FilePath) && File.Exists(row.FilePath);
        var hasChildren = hasProcess && _childrenByParent.ContainsKey(row!.Pid);
        var treeActionsAvailable = hasChildren && _contextMenuOpenedOnTreeCell;
        var processSuspended = hasProcess && _suspendedPids.Contains(row!.Pid);
        var treeSuspended = hasProcess && hasChildren && IsProcessTreeSuspended(row!.Pid);

        _terminateMenuItem.Enabled = hasProcess;
        _terminateTreeMenuItem.Enabled = hasProcess && treeActionsAvailable;
        _suspendMenuItem.Text = processSuspended ? "Возобновить" : "Приостановить";
        _suspendTreeMenuItem.Text = treeSuspended ? "Возобновить дерево" : "Приостановить дерево";
        _criticalMenuItem.Text = hasProcess && row!.IsCritical ? "Сделать не критичным" : "Сделать критичным";
        _suspendMenuItem.Enabled = hasProcess;
        _suspendTreeMenuItem.Enabled = hasProcess && treeActionsAvailable;
        _criticalMenuItem.Enabled = hasProcess;
        _openLocationMenuItem.Enabled = hasPath;
        _propertiesMenuItem.Enabled = hasPath;
        _copyMenuItem.Enabled = row is not null;
    }

    private void TerminateSelectedProcess(bool tree)
    {
        var row = GetSelectedDisplayRow();
        if (row is null || row.Pid <= 0)
        {
            return;
        }

        var message = tree
            ? $"Завершить процесс \"{row.Name}\" и все дочерние процессы?"
            : $"Завершить процесс \"{row.Name}\"?";

        if (MessageBox.Show(this, message, "Завершение процесса", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var pids = tree ? GetProcessTreePids(row.Pid).Reverse().ToList() : [row.Pid];
            foreach (var pid in pids)
            {
                TryTerminateProcess(pid);
                _suspendedPids.Remove(pid);
            }

            RefreshProcesses();
        }
        catch (Exception ex)
        {
            ShowProcessActionError(ex);
        }
    }

    private void SuspendSelectedProcess(bool tree)
    {
        var row = GetSelectedDisplayRow();
        if (row is null || row.Pid <= 0)
        {
            return;
        }

        try
        {
            var pids = tree ? GetProcessTreePids(row.Pid).ToList() : [row.Pid];
            var resume = tree ? IsProcessTreeSuspended(row.Pid) : _suspendedPids.Contains(row.Pid);
            foreach (var pid in pids)
            {
                if (resume)
                {
                    if (TryResumeProcess(pid))
                    {
                        _suspendedPids.Remove(pid);
                    }
                }
                else if (TrySuspendProcess(pid))
                {
                    _suspendedPids.Add(pid);
                }
            }

            RefreshProcesses();
        }
        catch (Exception ex)
        {
            ShowProcessActionError(ex);
        }
    }

    private void ToggleSelectedCriticality()
    {
        var row = GetSelectedDisplayRow();
        if (row is null || row.Pid <= 0)
        {
            return;
        }

        var makeCritical = !row.IsCritical;
        if (makeCritical)
        {
            var result = MessageBox.Show(
                this,
                $"Сделать процесс \"{row.Name}\" критичным?\r\n\r\nЕсли такой процесс завершится, Windows может аварийно завершить работу.",
                "Критичность процесса",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
            {
                return;
            }
        }

        try
        {
            SetProcessCriticality(row.Pid, makeCritical);
            if (_metadataByPid.TryGetValue(row.Pid, out var metadata))
            {
                _metadataByPid[row.Pid] = metadata with { IsCritical = makeCritical };
            }

            RefreshProcesses();
        }
        catch (Exception ex)
        {
            ShowProcessActionError(ex);
        }
    }

    private bool IsProcessTreeSuspended(int rootPid)
    {
        var pids = GetProcessTreePids(rootPid).Where(pid => pid > 0).ToList();
        return pids.Count > 0 && pids.All(_suspendedPids.Contains);
    }

    private IEnumerable<int> GetProcessTreePids(int rootPid)
    {
        yield return rootPid;

        if (!_childrenByParent.TryGetValue(rootPid, out var children))
        {
            yield break;
        }

        foreach (var child in children)
        {
            foreach (var pid in GetProcessTreePids(child.Pid))
            {
                yield return pid;
            }
        }
    }

    private static void TryTerminateProcess(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill();
        }
        catch
        {
            // Process may already be gone or protected.
        }
    }

    private static bool TrySuspendProcess(int pid)
    {
        var handle = OpenProcess(ProcessSuspendResume, false, pid);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return NtSuspendProcess(handle) >= 0;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static bool TryResumeProcess(int pid)
    {
        var handle = OpenProcess(ProcessSuspendResume, false, pid);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return NtResumeProcess(handle) >= 0;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static void SetProcessCriticality(int pid, bool critical)
    {
        RtlAdjustPrivilege(SeDebugPrivilege, true, false, out _);

        var handle = OpenProcess(ProcessSetInformation, false, pid);
        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var value = critical ? 1 : 0;
            var status = NtSetInformationProcess(
                handle,
                ProcessBreakOnTermination,
                ref value,
                sizeof(int));
            if (status < 0)
            {
                throw new InvalidOperationException($"NtSetInformationProcess вернул 0x{status:X8}.");
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private void OpenSelectedLocationInIUnlocker()
    {
        var row = GetSelectedDisplayRow();
        if (row is null || string.IsNullOrWhiteSpace(row.FilePath) || !File.Exists(row.FilePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(row.FilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var explorer = new FileExplorerForm(_session, directory, row.FilePath);
        explorer.Show(this);
    }

    private void ShowSelectedProperties()
    {
        var row = GetSelectedDisplayRow();
        if (row is null || string.IsNullOrWhiteSpace(row.FilePath) || !File.Exists(row.FilePath))
        {
            return;
        }

        try
        {
            ShellPropertyDialog.Show(Handle, row.FilePath);
        }
        catch (Exception ex)
        {
            ShowProcessActionError(ex);
        }
    }

    private void CopySelectedProcess()
    {
        var row = GetSelectedDisplayRow();
        if (row is null)
        {
            return;
        }

        Clipboard.SetText($"{row.Name}\t{row.Pid}\t{row.Criticality}\t{row.Load}\t{row.Memory}\t{row.Description}\t{row.SignatureStatus}\t{row.SignaturePublisher}\t{row.Company}\t{row.FilePath}");
    }

    private void ShowProcessActionError(Exception ex)
    {
        MessageBox.Show(this, ex.Message, "Диспетчер задач", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void GridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        GridCellClick(sender, e);
    }

    private void GridCellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 ||
            e.RowIndex >= _grid.Rows.Count ||
            _grid.Rows[e.RowIndex].DataBoundItem is not ProcessDisplayRow row)
        {
            return;
        }

        e.ToolTipText = BuildProcessToolTipText(row);
    }

    private void RefreshTooltipUnderMouse()
    {
        if (!_grid.ClientRectangle.Contains(_lastMouseLocation))
        {
            return;
        }

        var hit = _grid.HitTest(_lastMouseLocation.X, _lastMouseLocation.Y);
        if (hit.RowIndex < 0 ||
            hit.RowIndex >= _grid.Rows.Count ||
            _grid.Rows[hit.RowIndex].DataBoundItem is not ProcessDisplayRow row)
        {
            HideProcessToolTip();
            return;
        }

        ShowProcessToolTip(row, _lastMouseLocation);
    }

    private void ShowProcessToolTip(ProcessDisplayRow row, Point location)
    {
        var text = BuildProcessToolTipText(row);
        var tooltipLocation = new Point(location.X + 18, location.Y + 18);
        if (_lastTooltipPid == row.Pid && _lastTooltipText == text)
        {
            return;
        }

        _lastTooltipPid = row.Pid;
        _lastTooltipText = text;
        _processToolTip.Show(text, _grid, tooltipLocation, 12000);
    }

    private void HideProcessToolTip()
    {
        _lastTooltipPid = int.MinValue;
        _lastTooltipText = string.Empty;
        _processToolTip.Hide(_grid);
    }

    private static string BuildProcessToolTipText(ProcessDisplayRow row)
    {
        return
            $"Path: {GetTooltipValue(row.FilePath)}\r\n" +
            $"Description: {GetTooltipValue(row.Description)}\r\n" +
            $"Подпись: {GetTooltipValue(row.SignatureStatus)}\r\n" +
            $"Издатель подписи: {GetTooltipValue(row.SignaturePublisher)}\r\n" +
            $"Компания файла: {GetTooltipValue(row.Company)}";
    }

    private static string GetTooltipValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(нет данных)" : value;
    }

    private void GridCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != 0 ||
            _grid.Rows[e.RowIndex].DataBoundItem is not ProcessDisplayRow row)
        {
            return;
        }

        var graphics = e.Graphics!;
        var cellStyle = e.CellStyle!;
        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        var background = selected ? SystemColors.Highlight : cellStyle.BackColor;
        var foreground = selected ? SystemColors.HighlightText : cellStyle.ForeColor;

        using var backgroundBrush = new SolidBrush(background);
        graphics.FillRectangle(backgroundBrush, e.CellBounds);
        e.Paint(e.CellBounds, DataGridViewPaintParts.Border);

        var y = e.CellBounds.Top + (e.CellBounds.Height - 14) / 2;
        var x = e.CellBounds.Left + 5 + row.Depth * 18;

        if (row.HasChildren)
        {
            DrawExpander(graphics, new Rectangle(x, y + 2, 10, 10), row.Expanded, foreground);
        }

        x += 14;
        DrawProcessIcon(graphics, new Rectangle(x, y, 14, 14), row);
        x += 18;

        var textBounds = new Rectangle(
            x,
            e.CellBounds.Top + 2,
            Math.Max(0, e.CellBounds.Right - x - 4),
            e.CellBounds.Height - 4);

        TextRenderer.DrawText(
            graphics,
            row.Name,
            cellStyle.Font,
            textBounds,
            foreground,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        e.Handled = true;
    }

    private static void DrawExpander(Graphics graphics, Rectangle bounds, bool expanded, Color color)
    {
        var points = expanded
            ? new[]
            {
                new Point(bounds.Left + 1, bounds.Top + 3),
                new Point(bounds.Right - 1, bounds.Top + 3),
                new Point(bounds.Left + bounds.Width / 2, bounds.Bottom - 2),
            }
            : new[]
            {
                new Point(bounds.Left + 3, bounds.Top + 1),
                new Point(bounds.Left + 3, bounds.Bottom - 1),
                new Point(bounds.Right - 2, bounds.Top + bounds.Height / 2),
            };

        using var brush = new SolidBrush(color);
        graphics.FillPolygon(brush, points);
    }

    private void DrawProcessIcon(Graphics graphics, Rectangle bounds, ProcessDisplayRow row)
    {
        var icon = GetProcessIcon(row.FilePath);
        if (icon is not null)
        {
            graphics.DrawImage(icon, bounds);
            return;
        }

        DrawFallbackProcessIcon(graphics, bounds, row);
    }

    private Image? GetProcessIcon(string filePath)
    {
        var key = string.IsNullOrWhiteSpace(filePath) ? "<default>" : filePath;
        if (_processIconCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Image? image = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                using var icon = Icon.ExtractAssociatedIcon(filePath);
                image = icon?.ToBitmap();
            }
        }
        catch
        {
            image = null;
        }

        image ??= SystemIcons.Application.ToBitmap();
        _processIconCache[key] = image;
        return image;
    }

    private static void DrawFallbackProcessIcon(Graphics graphics, Rectangle bounds, ProcessDisplayRow row)
    {
        var accent = row.Criticality switch
        {
            "Критичный" => Color.FromArgb(222, 67, 67),
            "Защищённый" => Color.FromArgb(136, 78, 185),
            "Подозрительный" => Color.FromArgb(214, 176, 0),
            "Системный" => Color.FromArgb(42, 157, 210),
            _ => Color.FromArgb(55, 120, 210),
        };

        using var fillBrush = new SolidBrush(Color.White);
        using var accentBrush = new SolidBrush(accent);
        using var borderPen = new Pen(Color.FromArgb(95, 110, 130));

        graphics.FillRectangle(fillBrush, bounds);
        graphics.FillRectangle(accentBrush, bounds.Left + 2, bounds.Top + 2, bounds.Width - 4, 4);
        graphics.DrawRectangle(borderPen, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
        graphics.DrawLine(borderPen, bounds.Left + 3, bounds.Top + 8, bounds.Right - 3, bounds.Top + 8);
        graphics.DrawLine(borderPen, bounds.Left + 3, bounds.Top + 11, bounds.Right - 4, bounds.Top + 11);
    }

    private void GridColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex < 0)
        {
            return;
        }

        var property = _grid.Columns[e.ColumnIndex].DataPropertyName;
        if (string.IsNullOrWhiteSpace(property))
        {
            return;
        }

        if (string.Equals(_sortProperty, property, StringComparison.OrdinalIgnoreCase))
        {
            _sortDirection = _sortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        }
        else
        {
            _sortProperty = property;
            _sortDirection = ListSortDirection.Ascending;
        }

        RebuildTreeIndex();
        ApplyView();
    }

    private void UpdateSortGlyph()
    {
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            column.HeaderCell.SortGlyphDirection = string.Equals(column.DataPropertyName, _sortProperty, StringComparison.OrdinalIgnoreCase)
                ? _sortDirection == ListSortDirection.Ascending
                    ? SortOrder.Ascending
                    : SortOrder.Descending
                : SortOrder.None;
        }
    }

    private void HighlightRows()
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is not ProcessDisplayRow processRow)
            {
                continue;
            }

            row.DefaultCellStyle.Padding = new Padding(0);

            if (processRow.Criticality == "Критичный")
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 190, 202);
                row.DefaultCellStyle.ForeColor = Color.FromArgb(100, 0, 0);
            }
            else if (processRow.Suspicious)
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 255, 170);
                row.DefaultCellStyle.ForeColor = Color.FromArgb(80, 55, 0);
            }
            else if (processRow.Criticality == "Системный")
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(205, 255, 252);
                row.DefaultCellStyle.ForeColor = SystemColors.ControlText;
            }
            else if (processRow.Criticality == "Защищённый")
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(232, 222, 246);
                row.DefaultCellStyle.ForeColor = Color.FromArgb(60, 32, 95);
            }
        }
    }

    private static ProcessSnapshot ReadProcessInfos(Dictionary<int, ProcessMetadata> cachedMetadata, bool refreshMetadata)
    {
        try
        {
            return NativeProcessReader.Read(cachedMetadata, refreshMetadata);
        }
        catch
        {
            return new ProcessSnapshot(ReadBasicProcessInfos(cachedMetadata), null);
        }
    }

    private static List<ProcessInfo> ReadBasicProcessInfos(Dictionary<int, ProcessMetadata> metadataByPid)
    {
        var rows = new List<ProcessInfo>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var pid = process.Id;
                metadataByPid.TryGetValue(pid, out var metadata);
                rows.Add(new ProcessInfo(
                    process.ProcessName,
                    pid,
                    metadata.ParentPid,
                    metadata.Description ?? string.Empty,
                    metadata.SignatureStatus ?? string.Empty,
                    metadata.SignaturePublisher ?? string.Empty,
                    metadata.FilePath ?? string.Empty,
                    metadata.Company ?? string.Empty,
                    0,
                    0,
                    metadata.IsCritical,
                    metadata.IsProtected));
            }
            catch
            {
                // Process may exit while the list is being read.
            }
            finally
            {
                process.Dispose();
            }
        }

        return rows;
    }

    private static string FormatBytes(ulong bytes)
    {
        if (bytes == 0)
        {
            return string.Empty;
        }

        var mb = bytes / 1024d / 1024d;
        return mb >= 100
            ? $"{mb:0} МБ"
            : $"{mb:0.##} МБ";
    }

    private static string GetCriticality(string processName, string filePath, bool isCritical, bool isProtected)
    {
        if (isCritical)
        {
            return "Критичный";
        }

        if (isProtected)
        {
            return "Защищённый";
        }

        if (IsWindowsSystemPath(filePath))
        {
            return "Системный";
        }

        return "Обычный";
    }

    private static bool IsSuspicious(string processName, string filePath)
    {
        var lowerPath = filePath.ToLowerInvariant();
        var suspiciousNames = new[] { "powershell", "pwsh", "wscript", "cscript", "mshta", "rundll32", "regsvr32" };
        var suspiciousLocations = new[] { @"\appdata\", @"\temp\", @"\downloads\", @"\users\public\", @"\$recycle.bin\" };

        return suspiciousNames.Any(name => processName.Contains(name, StringComparison.OrdinalIgnoreCase)) ||
               suspiciousLocations.Any(lowerPath.Contains) ||
               (!string.IsNullOrWhiteSpace(filePath) && !IsWindowsSystemPath(filePath) && filePath.EndsWith(".scr", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBadSignature(string signatureStatus)
    {
        return signatureStatus.Contains("поврежд", StringComparison.OrdinalIgnoreCase) ||
               signatureStatus.Contains("Запрещ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWindowsSystemPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return filePath.StartsWith(Path.Combine(windowsPath, "System32"), StringComparison.OrdinalIgnoreCase) ||
               filePath.StartsWith(Path.Combine(windowsPath, "SysWOW64"), StringComparison.OrdinalIgnoreCase);
    }

    private static class NativeProcessReader
    {
        private const int SystemProcessInformation = 5;
        private const int ProcessBreakOnTermination = 29;
        private const int ProcessProtectionInformation = 61;
        private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
        private const uint ProcessQueryInformation = 0x0400;
        private const uint ProcessQueryLimitedInformation = 0x1000;

        public static ProcessSnapshot Read(Dictionary<int, ProcessMetadata> cachedMetadata, bool refreshMetadata)
        {
            var rows = new List<ProcessInfo>();
            Dictionary<int, ProcessMetadata>? updatedMetadata = refreshMetadata ? [] : null;

            var bufferLength = 1024 * 1024;
            IntPtr buffer = IntPtr.Zero;

            try
            {
                while (true)
                {
                    buffer = Marshal.AllocHGlobal(bufferLength);
                    var status = NtQuerySystemInformation(SystemProcessInformation, buffer, bufferLength, out var returnLength);
                    if (status == StatusInfoLengthMismatch)
                    {
                        Marshal.FreeHGlobal(buffer);
                        buffer = IntPtr.Zero;
                        bufferLength = Math.Max(bufferLength * 2, returnLength + 64 * 1024);
                        continue;
                    }

                    if (status < 0)
                    {
                        throw new InvalidOperationException($"NtQuerySystemInformation вернул 0x{status:X8}.");
                    }

                    break;
                }

                var current = buffer;
                while (true)
                {
                    var item = Marshal.PtrToStructure<SystemProcessInformationEntry>(current);
                    var pid = ToInt32(item.UniqueProcessId);
                    var parentPid = ToInt32(item.InheritedFromUniqueProcessId);
                    var name = ReadProcessName(item.ImageName, pid);

                    var metadata = GetMetadata(pid, cachedMetadata, refreshMetadata);
                    if (updatedMetadata is not null)
                    {
                        updatedMetadata[pid] = metadata;
                    }

                    rows.Add(new ProcessInfo(
                        name,
                        pid,
                        parentPid <= 0 ? null : parentPid,
                        metadata.Description,
                        metadata.SignatureStatus,
                        metadata.SignaturePublisher,
                        metadata.FilePath,
                        metadata.Company,
                        ToUnsignedTicks(item.KernelTime) + ToUnsignedTicks(item.UserTime),
                        ToUInt64(item.WorkingSetSize),
                        metadata.IsCritical,
                        metadata.IsProtected));

                    if (item.NextEntryOffset == 0)
                    {
                        break;
                    }

                    current = IntPtr.Add(current, checked((int)item.NextEntryOffset));
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            return new ProcessSnapshot(rows, updatedMetadata);
        }

        private static ProcessMetadata GetMetadata(
            int pid,
            Dictionary<int, ProcessMetadata> cachedMetadata,
            bool refreshMetadata)
        {
            if (!refreshMetadata && cachedMetadata.TryGetValue(pid, out var cached))
            {
                return cached;
            }

            var (isCritical, isProtected) = TryGetProcessFlags(pid);
            var filePath = TryGetProcessImagePath(pid);
            if (string.IsNullOrWhiteSpace(filePath) &&
                cachedMetadata.TryGetValue(pid, out cached))
            {
                return cached with
                {
                    IsCritical = isCritical || cached.IsCritical,
                    IsProtected = isProtected || cached.IsProtected,
                };
            }

            var versionMetadata = TryGetFileVersionMetadata(filePath);
            var signature = FileSignatureVerifier.Verify(filePath);
            return new ProcessMetadata(
                null,
                versionMetadata.Description,
                signature.Status,
                signature.Publisher,
                filePath,
                versionMetadata.Company,
                isCritical,
                isProtected);
        }

        private static (bool IsCritical, bool IsProtected) TryGetProcessFlags(int pid)
        {
            if (pid <= 0)
            {
                return (false, false);
            }

            var handle = OpenProcess(ProcessQueryInformation | ProcessQueryLimitedInformation, false, pid);
            if (handle == IntPtr.Zero)
            {
                handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            }

            if (handle == IntPtr.Zero)
            {
                return (false, false);
            }

            try
            {
                return (TryQueryBreakOnTermination(handle), TryQueryProtection(handle));
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static bool TryQueryBreakOnTermination(IntPtr processHandle)
        {
            var value = 0;
            var status = NtQueryInformationProcess(
                processHandle,
                ProcessBreakOnTermination,
                ref value,
                sizeof(int),
                out _);
            return status >= 0 && value != 0;
        }

        private static bool TryQueryProtection(IntPtr processHandle)
        {
            byte protection = 0;
            var status = NtQueryInformationProcess(
                processHandle,
                ProcessProtectionInformation,
                ref protection,
                sizeof(byte),
                out _);
            if (status >= 0)
            {
                return protection != 0;
            }

            var protectionLevel = 0;
            status = NtQueryInformationProcess(
                processHandle,
                ProcessProtectionInformation,
                ref protectionLevel,
                sizeof(int),
                out _);
            return status >= 0 && protectionLevel != 0;
        }

        private static string TryGetProcessImagePath(int pid)
        {
            if (pid <= 4)
            {
                return string.Empty;
            }

            var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (handle == IntPtr.Zero)
            {
                return string.Empty;
            }

            try
            {
                var buffer = new StringBuilder(32768);
                var size = buffer.Capacity;
                return QueryFullProcessImageName(handle, 0, buffer, ref size)
                    ? buffer.ToString()
                    : string.Empty;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static (string Description, string Company) TryGetFileVersionMetadata(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return (string.Empty, string.Empty);
            }

            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(filePath);
                return (
                    versionInfo.FileDescription ?? string.Empty,
                    versionInfo.CompanyName ?? string.Empty);
            }
            catch
            {
                return (string.Empty, string.Empty);
            }
        }

        private static string ReadProcessName(UnicodeString imageName, int pid)
        {
            if (imageName.Buffer != IntPtr.Zero && imageName.Length > 0)
            {
                var name = Marshal.PtrToStringUni(imageName.Buffer, imageName.Length / 2);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }

            return pid switch
            {
                0 => "System Idle Process",
                4 => "System",
                _ => $"PID {pid}",
            };
        }

        private static int ToInt32(IntPtr value)
        {
            return unchecked((int)value.ToInt64());
        }

        private static ulong ToUInt64(UIntPtr value)
        {
            return Environment.Is64BitProcess ? value.ToUInt64() : value.ToUInt32();
        }

        private static ulong ToUnsignedTicks(long value)
        {
            return value <= 0 ? 0 : (ulong)value;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(
            int systemInformationClass,
            IntPtr systemInformation,
            int systemInformationLength,
            out int returnLength);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(
            IntPtr processHandle,
            int processInformationClass,
            ref int processInformation,
            int processInformationLength,
            out int returnLength);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(
            IntPtr processHandle,
            int processInformationClass,
            ref byte processInformation,
            int processInformationLength,
            out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(
            IntPtr process,
            int flags,
            StringBuilder exeName,
            ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct UnicodeString
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemProcessInformationEntry
        {
            public uint NextEntryOffset;
            public uint NumberOfThreads;
            public long WorkingSetPrivateSize;
            public uint HardFaultCount;
            public uint NumberOfThreadsHighWatermark;
            public ulong CycleTime;
            public long CreateTime;
            public long UserTime;
            public long KernelTime;
            public UnicodeString ImageName;
            public int BasePriority;
            public IntPtr UniqueProcessId;
            public IntPtr InheritedFromUniqueProcessId;
            public uint HandleCount;
            public uint SessionId;
            public UIntPtr UniqueProcessKey;
            public UIntPtr PeakVirtualSize;
            public UIntPtr VirtualSize;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
        }
    }

    private readonly record struct ProcessInfo(
        string Name,
        int Pid,
        int? ParentPid,
        string Description,
        string SignatureStatus,
        string SignaturePublisher,
        string FilePath,
        string Company,
        ulong ProcessorTime,
        ulong WorkingSetBytes,
        bool IsCritical,
        bool IsProtected);

    private sealed record ProcessSnapshot(
        List<ProcessInfo> Processes,
        Dictionary<int, ProcessMetadata>? UpdatedMetadata);

    private readonly record struct ProcessMetadata(
        int? ParentPid,
        string Description,
        string SignatureStatus,
        string SignaturePublisher,
        string FilePath,
        string Company,
        bool IsCritical,
        bool IsProtected);

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref int processInformation,
        int processInformationLength);

    [DllImport("ntdll.dll")]
    private static extern int RtlAdjustPrivilege(
        uint privilege,
        bool enable,
        bool currentThread,
        out bool enabled);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private static class ShellPropertyDialog
    {
        private const uint SeeMaskInvokeIdList = 0x0000000C;
        private const int SwShow = 5;

        public static void Show(IntPtr ownerHandle, string path)
        {
            var info = new ShellExecuteInfo
            {
                Size = Marshal.SizeOf<ShellExecuteInfo>(),
                Mask = SeeMaskInvokeIdList,
                OwnerHandle = ownerHandle,
                Verb = "properties",
                File = path,
                Directory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
                Show = SwShow,
            };

            if (!ShellExecuteEx(ref info))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ShellExecuteEx(ref ShellExecuteInfo executeInfo);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShellExecuteInfo
        {
            public int Size;
            public uint Mask;
            public IntPtr OwnerHandle;
            public string? Verb;
            public string? File;
            public string? Parameters;
            public string? Directory;
            public int Show;
            public IntPtr InstanceHandle;
            public IntPtr IdList;
            public string? Class;
            public IntPtr KeyClass;
            public uint HotKey;
            public IntPtr Icon;
            public IntPtr ProcessHandle;
        }
    }

    private sealed record ProcessRow(
        string Name,
        int Pid,
        int? ParentPid,
        string Criticality,
        string Load,
        ulong MemoryBytes,
        string Memory,
        string Description,
        string SignatureStatus,
        string SignaturePublisher,
        string FilePath,
        string Company,
        bool IsCritical,
        bool Suspicious);

    private sealed record ProcessDisplayRow(
        string Name,
        int Pid,
        string Criticality,
        string Load,
        string Memory,
        string Description,
        string SignatureStatus,
        string SignaturePublisher,
        string FilePath,
        string Company,
        bool IsCritical,
        bool Suspicious,
        bool HasChildren,
        bool Expanded,
        int Depth)
    {
        public static ProcessDisplayRow From(ProcessRow row, int depth, bool hasChildren, bool expanded)
        {
            return new ProcessDisplayRow(
                row.Name,
                row.Pid,
                row.Criticality,
                row.Load,
                row.Memory,
                row.Description,
                row.SignatureStatus,
                row.SignaturePublisher,
                row.FilePath,
                row.Company,
                row.IsCritical,
                row.Suspicious,
                hasChildren,
                expanded,
                depth);
        }
    }
}
