using System.ComponentModel;
using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Win32;

namespace IUnlocker;

public partial class Form1 : Form
{
    private readonly AppSession _session;

    private readonly DataGridView _grid = new();
    private readonly TextBox _searchBox = new();
    private readonly Label _statusLabel = new();
    private readonly Button _refreshButton = new();
    private readonly ContextMenuStrip _contextMenu = new();
    private readonly SplitContainer _contentSplit = new();
    private readonly TreeView _taskFolderTree = new();
    private readonly ImageList _taskFolderImages = new();
    private readonly ToolStripMenuItem _copyCommandMenuItem = new("Копировать команду");
    private readonly ToolStripMenuItem _openLocationMenuItem = new("Открыть расположение");
    private readonly ToolStripMenuItem _copyRowMenuItem = new("Копировать строку");
    private readonly ToolStripMenuItem _editRegistryMenuItem = new("Изменить значение");
    private readonly ToolStripMenuItem _openInIUnlockerRegistryMenuItem = new("Открыть значение в реестре iUnlocker");
    private readonly ToolStripMenuItem _editScheduledTaskMenuItem = new("Изменить задачу");
    private readonly ToolStripMenuItem _deleteStartupMenuItem = new("Удалить запись");
    private readonly ToolStripMenuItem _openInIUnlockerExplorerMenuItem = new("Открыть файл в проводнике iUnlocker");
    private readonly FlowLayoutPanel _tabsPanel = new();

    private const string SuspiciousTab = "Подозрительное";
    private const string ScheduledTaskCategory = "Scheduled Task";
    private const string ScheduledTaskRootFolder = "\\";

    private List<StartupEntry> _entries = [];
    private List<string> _warnings = [];
    private List<string> _scheduledTaskFolders = [];
    private string _selectedCategory = "Все";
    private string _selectedTaskFolder = ScheduledTaskRootFolder;
    private string? _sortProperty;
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;
    private bool _updatingTaskFolderTree;

    public Form1(AppSession session)
    {
        _session = session;
        InitializeComponent();
        BuildInterface();
        Load += (_, _) => RefreshEntries();
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - автозагрузка";
        MinimumSize = new Size(920, 520);
        StartPosition = FormStartPosition.CenterScreen;
        UiTheme.ApplyForm(this);

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
        };
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _searchBox.PlaceholderText = "Поиск по названию, команде или источнику";
        _searchBox.Dock = DockStyle.Fill;
        _searchBox.Margin = new Padding(0, 0, 10, 10);
        _searchBox.TextChanged += (_, _) => ApplyFilter();
        UiTheme.StyleTextBox(_searchBox);

        _refreshButton.Text = "Обновить";
        _refreshButton.AutoSize = true;
        _refreshButton.Margin = new Padding(0, 0, 8, 10);
        _refreshButton.Click += (_, _) => RefreshEntries();
        UiTheme.StyleButton(_refreshButton, primary: true);

        toolbar.Controls.Add(_searchBox, 0, 0);
        toolbar.Controls.Add(_refreshButton, 1, 0);

        _tabsPanel.Dock = DockStyle.Top;
        _tabsPanel.AutoSize = true;
        _tabsPanel.WrapContents = true;
        _tabsPanel.Margin = new Padding(0, 0, 0, 10);
        _tabsPanel.Padding = new Padding(0);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToOrderColumns = true;
        _grid.AllowUserToResizeColumns = true;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        UiTheme.StyleGrid(_grid);
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.MultiSelect = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.ScrollBars = ScrollBars.Both;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.CellMouseDown += GridCellMouseDown;
        _grid.ColumnHeaderMouseClick += GridColumnHeaderMouseClick;
        _grid.DataBindingComplete += (_, _) => HighlightSuspiciousRows();
        _grid.SelectionChanged += (_, _) => UpdateActions();

        AddColumn("Name", "Название", 180);
        AddColumn("Type", "Тип", 110);
        AddColumn("Scope", "Область", 130);
        AddColumn("SignatureStatus", "Подпись", 140);
        AddColumn("SignaturePublisher", "Издатель", 190);
        AddColumn("Command", "Команда", 440);
        AddColumn("Location", "Расположение", 420);

        _copyCommandMenuItem.Click += (_, _) => CopySelectedCommand();
        _openLocationMenuItem.Click += (_, _) => OpenSelectedLocation();
        _copyRowMenuItem.Click += (_, _) => CopySelectedRow();
        _editRegistryMenuItem.Click += (_, _) => EditSelectedRegistryValue();
        _openInIUnlockerRegistryMenuItem.Click += (_, _) => OpenSelectedInIUnlockerRegistry();
        _editScheduledTaskMenuItem.Click += (_, _) => EditSelectedScheduledTask();
        _deleteStartupMenuItem.Click += (_, _) => DeleteSelectedStartupEntry();
        _openInIUnlockerExplorerMenuItem.Click += (_, _) => OpenSelectedInIUnlockerExplorer();
        _contextMenu.Opening += (_, e) =>
        {
            UpdateActions();
            UiTheme.HideUnavailableContextMenuItems(_contextMenu);
        };
        _contextMenu.Items.AddRange(new ToolStripItem[]
        {
            _copyCommandMenuItem,
            _openLocationMenuItem,
            _openInIUnlockerExplorerMenuItem,
            _copyRowMenuItem,
            new ToolStripSeparator(),
            _editRegistryMenuItem,
            _openInIUnlockerRegistryMenuItem,
            _editScheduledTaskMenuItem,
            _deleteStartupMenuItem,
        });
        _grid.ContextMenuStrip = _contextMenu;

        _taskFolderTree.Dock = DockStyle.Fill;
        _taskFolderTree.HideSelection = false;
        UiTheme.StyleTree(_taskFolderTree);
        _taskFolderTree.ImageList = _taskFolderImages;
        _taskFolderImages.ColorDepth = ColorDepth.Depth32Bit;
        _taskFolderImages.ImageSize = new Size(16, 16);
        _taskFolderImages.Images.Add("library", CreateTaskLibraryIcon());
        _taskFolderImages.Images.Add("folder", CreateTaskFolderIcon());
        _taskFolderTree.AfterSelect += (_, args) =>
        {
            if (_updatingTaskFolderTree || args.Node?.Tag is not string folder)
            {
                return;
            }

            _selectedTaskFolder = folder;
            ApplyFilter();
        };

        _contentSplit.Dock = DockStyle.Fill;
        _contentSplit.Orientation = Orientation.Vertical;
        _contentSplit.FixedPanel = FixedPanel.Panel1;
        _contentSplit.Panel1MinSize = 0;
        _contentSplit.Panel2MinSize = 0;
        _contentSplit.SizeChanged += (_, _) => UpdateTaskFolderSplitter();
        _contentSplit.Panel1.Controls.Add(_taskFolderTree);
        _contentSplit.Panel2.Controls.Add(_grid);
        _contentSplit.Panel1Collapsed = true;

        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(0, 10, 0, 0);
        _statusLabel.ForeColor = UiTheme.MutedText;

        main.Controls.Add(toolbar, 0, 0);
        main.Controls.Add(_tabsPanel, 0, 1);
        main.Controls.Add(_contentSplit, 0, 2);
        main.Controls.Add(_statusLabel, 0, 3);
        Controls.Add(main);

        UpdateCategoryTabs();
        UpdateActions();
    }

    private void AddColumn(
        string property,
        string header,
        int width,
        DataGridViewAutoSizeColumnMode autoSizeMode = DataGridViewAutoSizeColumnMode.None)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            Name = property,
            Width = width,
            AutoSizeMode = autoSizeMode,
            Resizable = DataGridViewTriState.True,
            SortMode = DataGridViewColumnSortMode.Programmatic,
        });
    }

    private static Bitmap CreateTaskLibraryIcon()
    {
        var bitmap = CreateTaskFolderIcon();
        using var graphics = Graphics.FromImage(bitmap);
        using var borderPen = new Pen(Color.FromArgb(72, 108, 156));
        using var fillBrush = new SolidBrush(Color.FromArgb(210, 230, 252));
        graphics.FillRectangle(fillBrush, 9, 2, 5, 5);
        graphics.DrawRectangle(borderPen, 9, 2, 5, 5);
        graphics.DrawLine(borderPen, 10, 4, 12, 4);
        graphics.DrawLine(borderPen, 10, 6, 13, 6);
        return bitmap;
    }

    private static Bitmap CreateTaskFolderIcon()
    {
        var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);

        using var tabBrush = new SolidBrush(Color.FromArgb(255, 218, 116));
        using var folderBrush = new SolidBrush(Color.FromArgb(255, 203, 76));
        using var highlightBrush = new SolidBrush(Color.FromArgb(255, 232, 151));
        using var borderPen = new Pen(Color.FromArgb(184, 135, 28));

        graphics.FillRectangle(tabBrush, 2, 4, 5, 3);
        graphics.FillRectangle(folderBrush, 1, 6, 14, 8);
        graphics.FillRectangle(highlightBrush, 2, 7, 12, 2);
        graphics.DrawRectangle(borderPen, 1, 6, 13, 7);
        graphics.DrawLine(borderPen, 2, 4, 6, 4);
        graphics.DrawLine(borderPen, 6, 4, 8, 6);

        return bitmap;
    }

    private void RefreshEntries()
    {
        Cursor = Cursors.WaitCursor;
        _refreshButton.Enabled = false;

        try
        {
            var result = _session.IsWinPe && _session.WindowsPath is not null && !IsWinPeDrive(_session.DriveRoot)
                ? OfflineStartupScanner.Scan(_session)
                : StartupScanner.Scan();
            _entries = result.Entries.Select(AddSignatureInfo).ToList();
            _warnings = result.Warnings.ToList();
            _scheduledTaskFolders = result.ScheduledTaskFolders.ToList();
            UpdateCategoryTabs();
            ApplyFilter();
        }
        finally
        {
            _refreshButton.Enabled = true;
            Cursor = Cursors.Default;
            UpdateActions();
        }
    }

    private static bool IsWinPeDrive(string driveRoot)
    {
        return driveRoot.StartsWith("X:", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyFilter()
    {
        UpdateTaskFolderPanel();

        var query = _searchBox.Text.Trim();
        IEnumerable<StartupEntry> filtered = _entries;

        if (_selectedCategory == SuspiciousTab)
        {
            filtered = filtered.Where(IsSuspicious);
        }
        else if (_selectedCategory != "Все")
        {
            filtered = filtered.Where(entry => entry.Category.Equals(_selectedCategory, StringComparison.CurrentCultureIgnoreCase));

            if (IsScheduledTaskTabSelected())
            {
                filtered = filtered.Where(entry =>
                    GetScheduledTaskFolder(entry).Equals(_selectedTaskFolder, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(entry =>
                Contains(entry.Name, query) ||
                Contains(entry.Category, query) ||
                Contains(entry.Type, query) ||
                Contains(entry.Scope, query) ||
                Contains(entry.Source, query) ||
                Contains(entry.SignatureStatus, query) ||
                Contains(entry.SignaturePublisher, query) ||
                Contains(entry.Command, query) ||
                Contains(entry.Location, query));
        }

        var visibleEntries = SortEntries(filtered).ToList();
        _grid.DataSource = visibleEntries;
        UpdateSortGlyph();
        UpdateStatus(visibleEntries.Count);
        UpdateActions();
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

        ApplyFilter();
    }

    private IEnumerable<StartupEntry> SortEntries(IEnumerable<StartupEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(_sortProperty))
        {
            return entries;
        }

        return _sortDirection == ListSortDirection.Ascending
            ? entries.OrderBy(GetSortValue, StringComparer.CurrentCultureIgnoreCase)
            : entries.OrderByDescending(GetSortValue, StringComparer.CurrentCultureIgnoreCase);
    }

    private string GetSortValue(StartupEntry entry)
    {
        return _sortProperty switch
        {
            nameof(StartupEntry.Name) => entry.Name,
            nameof(StartupEntry.Type) => entry.Type,
            nameof(StartupEntry.Scope) => entry.Scope,
            nameof(StartupEntry.SignatureStatus) => entry.SignatureStatus,
            nameof(StartupEntry.SignaturePublisher) => entry.SignaturePublisher,
            nameof(StartupEntry.Command) => entry.Command,
            nameof(StartupEntry.Location) => entry.Location,
            _ => string.Empty,
        };
    }

    private static StartupEntry AddSignatureInfo(StartupEntry entry)
    {
        var target = TryGetExistingTargetPath(entry);
        if (string.IsNullOrWhiteSpace(target) || !File.Exists(target))
        {
            return entry;
        }

        var signature = FileSignatureVerifier.Verify(target);
        return entry with
        {
            SignatureStatus = signature.Status,
            SignaturePublisher = signature.Publisher,
        };
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

    private void UpdateCategoryTabs()
    {
        _tabsPanel.SuspendLayout();
        _tabsPanel.Controls.Clear();

        var counts = _entries
            .GroupBy(entry => entry.Category)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.CurrentCultureIgnoreCase);

        AddCategoryButton("Все", _entries.Count);
        AddCategoryButton(SuspiciousTab, _entries.Count(IsSuspicious));

        foreach (var category in GetOrderedCategories(counts.Keys))
        {
            AddCategoryButton(category, counts[category]);
        }

        if (_selectedCategory != "Все" && _selectedCategory != SuspiciousTab && !counts.ContainsKey(_selectedCategory))
        {
            _selectedCategory = "Все";
        }

        HighlightSelectedCategory();
        UpdateTaskFolderTree();
        _tabsPanel.ResumeLayout();
    }

    private void AddCategoryButton(string category, int count)
    {
        var button = new Button
        {
            Text = $"{category} ({count})",
            Tag = category,
            AutoSize = true,
            Height = 30,
            Margin = new Padding(0, 0, 8, 8),
        };
        UiTheme.StyleButton(button);
        button.Click += (_, _) =>
        {
            _selectedCategory = category;
            if (IsScheduledTaskTabSelected())
            {
                _selectedTaskFolder = ScheduledTaskRootFolder;
            }

            HighlightSelectedCategory();
            ApplyFilter();
        };

        _tabsPanel.Controls.Add(button);
    }

    private void HighlightSelectedCategory()
    {
        foreach (Control control in _tabsPanel.Controls)
        {
            if (control is not Button button || button.Tag is not string category)
            {
                continue;
            }

            var selected = category.Equals(_selectedCategory, StringComparison.CurrentCultureIgnoreCase);
            button.BackColor = selected
                ? UiTheme.Accent
                : category == SuspiciousTab
                    ? Color.FromArgb(255, 245, 210)
                    : UiTheme.Surface;
            button.ForeColor = selected ? Color.White : UiTheme.Text;
            button.FlatAppearance.BorderColor = selected ? UiTheme.Accent : UiTheme.Border;
        }
    }

    private static IEnumerable<string> GetOrderedCategories(IEnumerable<string> categories)
    {
        var priority = new[]
        {
            "Run",
            "CMDLINE",
            "Winlogon",
            "Startup Folder",
            ScheduledTaskCategory,
            "Services",
            "Drivers",
            "BootExecute",
            "AppInit_DLLs",
            "IFEO",
            "Explorer",
            "Active Setup",
            "WMI",
            "LSA",
            "Print Monitor",
        };

        var categorySet = new HashSet<string>(categories, StringComparer.CurrentCultureIgnoreCase);

        foreach (var category in priority)
        {
            if (categorySet.Remove(category))
            {
                yield return category;
            }
        }

        foreach (var category in categorySet.OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase))
        {
            yield return category;
        }
    }

    private void UpdateStatus(int visibleCount)
    {
        var selectedText = _selectedCategory == "Все" ? "все категории" : _selectedCategory;
        if (IsScheduledTaskTabSelected() && _selectedTaskFolder != ScheduledTaskRootFolder)
        {
            selectedText = $"{selectedText}, папка: {_selectedTaskFolder}";
        }

        var warningText = _warnings.Count == 0
            ? string.Empty
            : $" Предупреждения: {string.Join(" | ", _warnings.Take(3))}";

        _statusLabel.Text = $"Показано: {visibleCount} из {_entries.Count}, вкладка: {selectedText}.{warningText}";
    }

    private void UpdateTaskFolderPanel()
    {
        _contentSplit.Panel1Collapsed = !IsScheduledTaskTabSelected();
        UpdateTaskFolderSplitter();
    }

    private void UpdateTaskFolderSplitter()
    {
        if (_contentSplit.Panel1Collapsed || _contentSplit.Width <= 0)
        {
            return;
        }

        var desired = Math.Min(260, Math.Max(160, _contentSplit.Width / 3));
        var maxDistance = Math.Max(0, _contentSplit.Width - _contentSplit.SplitterWidth);
        if (maxDistance <= 0)
        {
            return;
        }

        _contentSplit.SplitterDistance = Math.Clamp(desired, 0, maxDistance);
    }

    private void UpdateTaskFolderTree()
    {
        _updatingTaskFolderTree = true;

        try
        {
            _taskFolderTree.BeginUpdate();
            _taskFolderTree.Nodes.Clear();

            var scheduledEntries = _entries
                .Where(entry => entry.Category.Equals(ScheduledTaskCategory, StringComparison.CurrentCultureIgnoreCase))
                .ToList();

            var root = new TreeNode("Библиотека планировщика заданий")
            {
                Tag = ScheduledTaskRootFolder,
                ImageKey = "library",
                SelectedImageKey = "library",
            };

            var nodesByPath = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase)
            {
                [ScheduledTaskRootFolder] = root,
            };

            var folders = _scheduledTaskFolders.Count == 0
                ? scheduledEntries.Select(GetScheduledTaskFolder)
                : _scheduledTaskFolders;

            foreach (var folder in folders
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase))
            {
                AddTaskFolderNode(root, nodesByPath, folder);
            }

            _taskFolderTree.Nodes.Add(root);
            root.Expand();

            if (!nodesByPath.TryGetValue(_selectedTaskFolder, out var selectedNode))
            {
                _selectedTaskFolder = ScheduledTaskRootFolder;
                selectedNode = root;
            }

            _taskFolderTree.SelectedNode = selectedNode;
            selectedNode.EnsureVisible();
        }
        finally
        {
            _taskFolderTree.EndUpdate();
            _updatingTaskFolderTree = false;
            UpdateTaskFolderPanel();
        }
    }

    private static void AddTaskFolderNode(TreeNode root, Dictionary<string, TreeNode> nodesByPath, string folder)
    {
        if (folder == ScheduledTaskRootFolder)
        {
            return;
        }

        var parentPath = ScheduledTaskRootFolder;
        var parentNode = root;

        foreach (var part in folder.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var path = parentPath == ScheduledTaskRootFolder
                ? $@"\{part}"
                : $@"{parentPath}\{part}";

            if (!nodesByPath.TryGetValue(path, out var node))
            {
                node = new TreeNode(part)
                {
                    Tag = path,
                    ImageKey = "folder",
                    SelectedImageKey = "folder",
                };
                nodesByPath[path] = node;
                parentNode.Nodes.Add(node);
            }

            parentPath = path;
            parentNode = node;
        }
    }

    private static string GetScheduledTaskFolder(StartupEntry entry)
    {
        if (!entry.Category.Equals(ScheduledTaskCategory, StringComparison.CurrentCultureIgnoreCase))
        {
            return ScheduledTaskRootFolder;
        }

        if (!string.IsNullOrWhiteSpace(entry.ScheduledTaskPath))
        {
            var path = entry.ScheduledTaskPath.Replace('/', '\\').Trim();
            var lastSlash = path.LastIndexOf('\\');
            return lastSlash <= 0 ? ScheduledTaskRootFolder : path[..lastSlash];
        }

        var relativeName = entry.Name.Replace('/', '\\').Trim('\\');
        var folder = Path.GetDirectoryName(relativeName)?.Replace('/', '\\');
        return string.IsNullOrWhiteSpace(folder)
            ? ScheduledTaskRootFolder
            : $@"\{folder.Trim('\\')}";
    }

    private bool IsScheduledTaskTabSelected()
    {
        return _selectedCategory.Equals(ScheduledTaskCategory, StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool Contains(string value, string query)
    {
        return value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void HighlightSuspiciousRows()
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is not StartupEntry entry || !IsSuspicious(entry))
            {
                continue;
            }

            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 220);
            row.DefaultCellStyle.ForeColor = Color.FromArgb(90, 55, 0);
        }
    }

    private static bool IsSuspicious(StartupEntry entry)
    {
        var text = $"{entry.Command} {entry.Location}";
        var lower = text.ToLowerInvariant();
        var riskyLocations = new[]
        {
            @"\appdata\",
            @"\temp\",
            @"\downloads\",
            @"\users\public\",
            @"\programdata\",
            @"\recycler\",
            @"\$recycle.bin\",
        };
        var riskyTools = new[]
        {
            "powershell",
            "pwsh",
            "wscript",
            "cscript",
            "mshta",
            "rundll32",
            "regsvr32",
            "cmd.exe /c",
        };
        var scriptExtensions = new[] { ".ps1", ".vbs", ".js", ".jse", ".wsf", ".bat", ".cmd", ".scr" };

        var badSignature = entry.SignatureStatus.Contains("поврежд", StringComparison.OrdinalIgnoreCase) ||
                           entry.SignatureStatus.Contains("Запрещ", StringComparison.OrdinalIgnoreCase);

        if (badSignature ||
            riskyLocations.Any(lower.Contains) ||
            riskyTools.Any(lower.Contains) ||
            scriptExtensions.Any(lower.Contains) ||
            lower.Contains("http://") ||
            lower.Contains("https://"))
        {
            return true;
        }

        var target = TryGetExistingTargetPath(entry);
        return target is null &&
               !entry.Location.StartsWith("HK", StringComparison.OrdinalIgnoreCase) &&
               !entry.Location.StartsWith("Offline", StringComparison.OrdinalIgnoreCase) &&
               !entry.Location.StartsWith("Task Scheduler:", StringComparison.OrdinalIgnoreCase);
    }

    private StartupEntry? GetSelectedEntry()
    {
        return _grid.CurrentRow?.DataBoundItem as StartupEntry;
    }

    private void GridCellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || e.RowIndex < 0)
        {
            return;
        }

        _grid.ClearSelection();
        _grid.Rows[e.RowIndex].Selected = true;
        _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[Math.Max(e.ColumnIndex, 0)];
        UpdateActions();
    }

    private void CopySelectedCommand()
    {
        var entry = GetSelectedEntry();

        if (entry is not null && !string.IsNullOrWhiteSpace(entry.Command))
        {
            Clipboard.SetText(entry.Command);
            _statusLabel.Text = "Команда скопирована в буфер обмена.";
        }
    }

    private void CopySelectedRow()
    {
        var entry = GetSelectedEntry();

        if (entry is null)
        {
            return;
        }

        Clipboard.SetText($"{entry.Category}\t{entry.Name}\t{entry.Type}\t{entry.Scope}\t{entry.SignatureStatus}\t{entry.SignaturePublisher}\t{entry.Source}\t{entry.Command}\t{entry.Location}");
        _statusLabel.Text = "Строка скопирована в буфер обмена.";
    }

    private void OpenSelectedLocation()
    {
        var entry = GetSelectedEntry();

        if (entry is null)
        {
            return;
        }

        try
        {
            if (File.Exists(entry.Location))
            {
                StartProcess("explorer.exe", $"/select,\"{entry.Location}\"");
            }
            else if (Directory.Exists(entry.Location))
            {
                StartProcess("explorer.exe", $"\"{entry.Location}\"");
            }
            else if (entry.Location.StartsWith("HK", StringComparison.OrdinalIgnoreCase))
            {
                Clipboard.SetText(entry.Location);
                StartProcess("regedit.exe", string.Empty);
                _statusLabel.Text = "Путь реестра скопирован. Вставьте его в адресную строку Regedit.";
            }
            else
            {
                Clipboard.SetText(entry.Location);
                _statusLabel.Text = "Расположение скопировано в буфер обмена.";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Не удалось открыть расположение",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void EditSelectedRegistryValue()
    {
        var entry = GetSelectedEntry();

        if (entry is null || !entry.CanEditRegistry)
        {
            return;
        }

        using var form = new RegistryValueEditForm(entry);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            SaveRegistryValue(entry, form.EditedText);
            _statusLabel.Text = "Значение реестра изменено.";
            RefreshEntries();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Не удалось изменить значение",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void DeleteSelectedStartupEntry()
    {
        var entry = GetSelectedEntry();
        if (entry is null)
        {
            return;
        }

        var deleteText = IsServiceOrDriverEntry(entry)
            ? $"Удалить запись \"{entry.Name}\" из Services?\r\n\r\n{entry.Location}\r\n\r\nФайл драйвера/службы удалён не будет."
            : $"Удалить запись автозагрузки \"{entry.Name}\"?\r\n\r\n{entry.Location}";

        var result = MessageBox.Show(
            this,
            deleteText,
            "Удалить автозагрузку",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
        {
            return;
        }

        try
        {
            DeleteStartupEntry(entry);
            _statusLabel.Text = "Запись автозагрузки удалена.";
            RefreshEntries();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Не удалось удалить запись",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void EditSelectedScheduledTask()
    {
        var entry = GetSelectedEntry();
        if (entry is null || !entry.CanEditScheduledTask)
        {
            return;
        }

        using var form = new TextEditForm("Изменить задачу", "Команда запуска", entry.Command == "(команда не указана)" ? string.Empty : entry.Command);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(form.EditedText))
        {
            MessageBox.Show(this, "Команда задачи не может быть пустой.", "Планировщик", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            EditScheduledTask(entry, form.EditedText);
            _statusLabel.Text = "Задача изменена.";
            RefreshEntries();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Не удалось изменить задачу",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static void EditScheduledTask(StartupEntry entry, string command)
    {
        if (!string.IsNullOrWhiteSpace(entry.ScheduledTaskPath))
        {
            RunSchtasks(["/Change", "/TN", entry.ScheduledTaskPath, "/TR", command]);
            return;
        }

        if (!string.IsNullOrWhiteSpace(entry.OfflineScheduledTaskFile) && File.Exists(entry.OfflineScheduledTaskFile))
        {
            EditOfflineTaskXml(entry.OfflineScheduledTaskFile, command);
            return;
        }

        throw new InvalidOperationException("Для этой задачи нет пути для изменения.");
    }

    private static void EditOfflineTaskXml(string taskFile, string commandLine)
    {
        var document = XDocument.Load(taskFile, LoadOptions.PreserveWhitespace);
        var exec = document.Descendants().FirstOrDefault(node => node.Name.LocalName == "Exec")
            ?? throw new InvalidOperationException("В XML задачи нет Exec-действия.");

        var (command, arguments) = SplitCommandLine(commandLine);
        var ns = exec.Name.Namespace;
        var commandElement = exec.Elements().FirstOrDefault(node => node.Name.LocalName == "Command");
        if (commandElement is null)
        {
            commandElement = new XElement(ns + "Command");
            exec.AddFirst(commandElement);
        }

        commandElement.Value = command;

        var argumentsElement = exec.Elements().FirstOrDefault(node => node.Name.LocalName == "Arguments");
        if (string.IsNullOrWhiteSpace(arguments))
        {
            argumentsElement?.Remove();
        }
        else if (argumentsElement is null)
        {
            exec.Add(new XElement(ns + "Arguments", arguments));
        }
        else
        {
            argumentsElement.Value = arguments;
        }

        document.Save(taskFile);
    }

    private static void DeleteStartupEntry(StartupEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.OfflineRegistryHiveFile) &&
            !string.IsNullOrWhiteSpace(entry.OfflineRegistryMountPrefix) &&
            !string.IsNullOrWhiteSpace(entry.RegistryKeyPath) &&
            entry.RegistryValueName is not null)
        {
            OfflineRegistryEditor.DeleteValue(
                entry.OfflineRegistryHiveFile,
                entry.OfflineRegistryMountPrefix,
                entry.RegistryKeyPath,
                entry.RegistryValueName);
            return;
        }

        if (entry.RegistryHive is not null &&
            !string.IsNullOrWhiteSpace(entry.RegistryKeyPath) &&
            entry.RegistryValueName is not null)
        {
            using var baseKey = RegistryKey.OpenBaseKey(entry.RegistryHive.Value, entry.RegistryView);
            using var key = baseKey.OpenSubKey(entry.RegistryKeyPath, writable: true)
                ?? throw new InvalidOperationException("Ключ реестра не найден или недоступен для записи.");
            key.DeleteValue(entry.RegistryValueName, throwOnMissingValue: true);
            return;
        }

        if (IsServiceOrDriverEntry(entry) &&
            !string.IsNullOrWhiteSpace(entry.OfflineRegistryHiveFile) &&
            !string.IsNullOrWhiteSpace(entry.OfflineRegistryMountPrefix) &&
            !string.IsNullOrWhiteSpace(entry.RegistryKeyPath))
        {
            OfflineRegistryEditor.DeleteKey(
                entry.OfflineRegistryHiveFile,
                entry.OfflineRegistryMountPrefix,
                entry.RegistryKeyPath);
            return;
        }

        if (IsServiceOrDriverEntry(entry) &&
            entry.RegistryHive is not null &&
            !string.IsNullOrWhiteSpace(entry.RegistryKeyPath))
        {
            DeleteLiveRegistryKey(entry.RegistryHive.Value, entry.RegistryView, entry.RegistryKeyPath);
            return;
        }

        if (entry.Category == "Startup Folder" && File.Exists(entry.Location))
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                entry.Location,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            return;
        }

        if (entry.Category == "Scheduled Task" &&
            entry.Source.Contains("offline", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(entry.Location))
        {
            File.Delete(entry.Location);
            return;
        }

        if (!string.IsNullOrWhiteSpace(entry.ScheduledTaskPath))
        {
            RunSchtasks(["/Delete", "/TN", entry.ScheduledTaskPath, "/F"]);
            return;
        }

        throw new InvalidOperationException("Для этой записи удаление пока не поддерживается.");
    }

    private static void DeleteLiveRegistryKey(RegistryHive hive, RegistryView view, string keyPath)
    {
        var normalizedPath = keyPath.Trim('\\');
        var separator = normalizedPath.LastIndexOf('\\');
        if (separator <= 0 || separator >= normalizedPath.Length - 1)
        {
            throw new InvalidOperationException($"Нельзя удалить корневой ключ: {keyPath}");
        }

        var parentPath = normalizedPath[..separator];
        var subKeyName = normalizedPath[(separator + 1)..];
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var parent = baseKey.OpenSubKey(parentPath, writable: true)
            ?? throw new InvalidOperationException("Ключ реестра не найден или недоступен для записи.");
        parent.DeleteSubKeyTree(subKeyName, throwOnMissingSubKey: true);
    }

    private void OpenSelectedInIUnlockerExplorer()
    {
        var entry = GetSelectedEntry();
        if (entry is null)
        {
            return;
        }

        var path = TryGetExistingTargetPath(entry);
        if (path is null)
        {
            MessageBox.Show(
                this,
                "Не удалось определить существующий файл или папку для этой записи.",
                "Путь не найден",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var initialPath = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(initialPath))
        {
            return;
        }

        var explorer = new FileExplorerForm(_session, initialPath, path);
        explorer.Show(this);
    }

    private void OpenSelectedInIUnlockerRegistry()
    {
        var entry = GetSelectedEntry();
        if (entry is null || !entry.CanEditRegistry || string.IsNullOrWhiteSpace(entry.RegistryKeyPath))
        {
            return;
        }

        string? rootName = null;
        if (entry.RegistryHive == RegistryHive.LocalMachine)
        {
            rootName = "HKLM";
        }
        else if (entry.RegistryHive == RegistryHive.CurrentUser)
        {
            rootName = "HKCU";
        }

        var editor = new RegistryEditorForm(
            _session,
            rootName,
            entry.RegistryKeyPath,
            entry.RegistryValueName,
            entry.OfflineRegistryHiveFile);
        editor.Show(this);
    }

    private static void SaveRegistryValue(StartupEntry entry, string text)
    {
        if (!string.IsNullOrWhiteSpace(entry.OfflineRegistryHiveFile) &&
            !string.IsNullOrWhiteSpace(entry.OfflineRegistryMountPrefix) &&
            !string.IsNullOrWhiteSpace(entry.RegistryKeyPath) &&
            entry.RegistryValueName is not null)
        {
            OfflineRegistryEditor.SetValue(
                entry.OfflineRegistryHiveFile,
                entry.OfflineRegistryMountPrefix,
                entry.RegistryKeyPath,
                entry.RegistryValueName,
                ConvertRegistryValue(text, entry.RegistryValueKind),
                entry.RegistryValueKind);
            return;
        }

        if (entry.RegistryHive is null || entry.RegistryKeyPath is null || entry.RegistryValueName is null)
        {
            throw new InvalidOperationException("Для этой строки нет точного пути к значению реестра.");
        }

        using var baseKey = RegistryKey.OpenBaseKey(entry.RegistryHive.Value, entry.RegistryView);
        using var key = baseKey.OpenSubKey(entry.RegistryKeyPath, writable: true)
            ?? throw new InvalidOperationException("Ключ реестра не найден или недоступен для записи.");

        key.SetValue(entry.RegistryValueName, ConvertRegistryValue(text, entry.RegistryValueKind), entry.RegistryValueKind);
    }

    private static object ConvertRegistryValue(string text, RegistryValueKind kind)
    {
        return kind switch
        {
            RegistryValueKind.DWord => ParseInt32(text),
            RegistryValueKind.QWord => ParseInt64(text),
            RegistryValueKind.MultiString => text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.None),
            RegistryValueKind.Binary => ParseBinary(text),
            RegistryValueKind.ExpandString => text,
            RegistryValueKind.String => text,
            RegistryValueKind.None => text,
            RegistryValueKind.Unknown => text,
            _ => text,
        };
    }

    private static int ParseInt32(string text)
    {
        var trimmed = text.Trim();
        return trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt32(trimmed[2..], 16)
            : int.Parse(trimmed);
    }

    private static long ParseInt64(string text)
    {
        var trimmed = text.Trim();
        return trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt64(trimmed[2..], 16)
            : long.Parse(trimmed);
    }

    private static byte[] ParseBinary(string text)
    {
        var cleaned = text
            .Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);

        if (cleaned.Length % 2 != 0)
        {
            throw new FormatException("Для REG_BINARY нужно чётное количество hex-символов.");
        }

        return Convert.FromHexString(cleaned);
    }

    private static (string Command, string Arguments) SplitCommandLine(string commandLine)
    {
        var trimmed = commandLine.Trim();
        if (trimmed.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        if (trimmed[0] == '"')
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote > 0)
            {
                return (trimmed[1..closingQuote], trimmed[(closingQuote + 1)..].Trim());
            }
        }

        var firstSpace = trimmed.IndexOf(' ');
        return firstSpace < 0
            ? (trimmed, string.Empty)
            : (trimmed[..firstSpace], trimmed[(firstSpace + 1)..].Trim());
    }

    private static void RunSchtasks(IReadOnlyList<string> arguments)
    {
        using var process = new Process();
        process.StartInfo.FileName = "schtasks.exe";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);
        }
    }

    private static string? TryGetExistingTargetPath(StartupEntry entry)
    {
        if (File.Exists(entry.Location) || Directory.Exists(entry.Location))
        {
            return entry.Location;
        }

        foreach (var candidate in GetCommandPathCandidates(entry.Command))
        {
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCommandPathCandidates(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            yield break;
        }

        var expanded = Environment.ExpandEnvironmentVariables(command.Trim());
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

        foreach (var extension in new[] { ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js" })
        {
            var index = expanded.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                yield return expanded[..(index + extension.Length)].Trim('"', ' ');
            }
        }
    }

    private static void StartProcess(string fileName, string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = true,
        });
    }

    private void UpdateActions()
    {
        var entry = GetSelectedEntry();
        var hasSelection = entry is not null;

        _copyCommandMenuItem.Enabled = hasSelection;
        _openLocationMenuItem.Enabled = hasSelection;
        _copyRowMenuItem.Enabled = hasSelection;
        _editRegistryMenuItem.Enabled = entry?.CanEditRegistry == true;
        _openInIUnlockerRegistryMenuItem.Enabled = entry?.CanEditRegistry == true;
        _editScheduledTaskMenuItem.Enabled = entry?.CanEditScheduledTask == true;
        _deleteStartupMenuItem.Enabled = hasSelection && CanDeleteStartupEntry(entry!);
        _openInIUnlockerExplorerMenuItem.Enabled = hasSelection && TryGetExistingTargetPath(entry!) is not null;
    }

    private static bool CanDeleteStartupEntry(StartupEntry entry)
    {
        return entry.CanEditRegistry ||
               (IsServiceOrDriverEntry(entry) && !string.IsNullOrWhiteSpace(entry.RegistryKeyPath) &&
                (entry.RegistryHive is not null || !string.IsNullOrWhiteSpace(entry.OfflineRegistryHiveFile))) ||
               (entry.Category == "Startup Folder" && File.Exists(entry.Location)) ||
               !string.IsNullOrWhiteSpace(entry.ScheduledTaskPath) ||
               (entry.Category == "Scheduled Task" &&
                entry.Source.Contains("offline", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(entry.Location));
    }

    private static bool IsServiceOrDriverEntry(StartupEntry entry)
    {
        return entry.Category.Equals("Services", StringComparison.OrdinalIgnoreCase) ||
               entry.Category.Equals("Drivers", StringComparison.OrdinalIgnoreCase);
    }
}
