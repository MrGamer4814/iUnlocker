using Microsoft.Win32;

namespace IUnlocker;

public sealed class RegistryEditorForm : Form
{
    private const int RegistrySearchMaxResults = 10000;

    private readonly AppSession _session;
    private readonly TreeView _keyTree = new();
    private readonly ListView _valueList = new();
    private readonly TextBox _pathBox = new();
    private readonly Button _goButton = new();
    private readonly Button _searchButton = new();
    private readonly AutoCompleteStringCollection _pathSuggestions = new();
    private readonly System.Windows.Forms.Timer _pathSuggestionTimer = new();
    private readonly Label _statusLabel = new();
    private readonly List<RegistryRootContext> _roots = [];
    private readonly string? _initialHiveFile;
    private readonly string? _initialRootName;
    private readonly string? _initialKeyPath;
    private readonly string? _initialValueName;
    private RegistrySearchForm? _searchForm;
    private string _pendingPathSuggestionText = string.Empty;
    private bool _settingPathText;

    public RegistryEditorForm(
        AppSession session,
        string? initialRootName = null,
        string? initialKeyPath = null,
        string? initialValueName = null,
        string? initialHiveFile = null)
    {
        _session = session;
        _initialRootName = initialRootName;
        _initialKeyPath = initialKeyPath;
        _initialValueName = initialValueName;
        _initialHiveFile = initialHiveFile;
        BuildInterface();
        Load += (_, _) => LoadRoots();
        FormClosed += (_, _) =>
        {
            _searchForm?.Close();
            _pathSuggestionTimer.Stop();
            _pathSuggestionTimer.Dispose();
            DisposeRoots();
        };
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - редактор реестра";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(980, 600);
        ClientSize = new Size(1160, 700);
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

        var pathPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            RowCount = 1,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        };
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var pathLabel = new Label
        {
            Text = "Путь:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 8, 0),
        };

        _pathBox.Dock = DockStyle.Fill;
        _pathBox.Margin = new Padding(0, 0, 8, 0);
        _pathBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _pathBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
        _pathBox.AutoCompleteCustomSource = _pathSuggestions;
        _pathBox.KeyDown += (_, args) =>
        {
            if (args.KeyCode != Keys.Enter)
            {
                return;
            }

            args.SuppressKeyPress = true;
            NavigateToPath();
        };
        UiTheme.StyleTextBox(_pathBox);
        _pathBox.TextChanged += (_, _) =>
        {
            if (!_settingPathText)
            {
                SchedulePathSuggestions(_pathBox.Text);
            }
        };

        _pathSuggestionTimer.Interval = 250;
        _pathSuggestionTimer.Tick += (_, _) =>
        {
            _pathSuggestionTimer.Stop();
            UpdatePathSuggestions(_pendingPathSuggestionText);
        };

        _goButton.Text = "Перейти";
        _goButton.AutoSize = true;
        _goButton.Click += (_, _) => NavigateToPath();
        UiTheme.StyleButton(_goButton, primary: true);

        _searchButton.Text = "Поиск";
        _searchButton.AutoSize = true;
        _searchButton.Margin = new Padding(8, 0, 0, 0);
        _searchButton.Click += (_, _) => OpenSearchForm();
        UiTheme.StyleButton(_searchButton);

        pathPanel.Controls.Add(pathLabel, 0, 0);
        pathPanel.Controls.Add(_pathBox, 1, 0);
        pathPanel.Controls.Add(_goButton, 2, 0);
        pathPanel.Controls.Add(_searchButton, 3, 0);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 390,
            BorderStyle = BorderStyle.FixedSingle,
        };

        _keyTree.Dock = DockStyle.Fill;
        _keyTree.HideSelection = false;
        _keyTree.BeforeExpand += KeyTreeBeforeExpand;
        _keyTree.AfterSelect += (_, _) => LoadValues();
        UiTheme.StyleTree(_keyTree);

        var keyMenu = new ContextMenuStrip();
        UiTheme.StyleContextMenu(keyMenu);
        var createKeyItem = new ToolStripMenuItem("Создать раздел...");
        createKeyItem.Click += (_, _) => CreateSubKey();
        var createValueItem = new ToolStripMenuItem("Создать параметр");
        AddCreateValueMenuItem(createValueItem, "Строковый параметр", RegistryValueKind.String);
        AddCreateValueMenuItem(createValueItem, "Расширяемый строковый параметр", RegistryValueKind.ExpandString);
        AddCreateValueMenuItem(createValueItem, "DWORD (32 бита)", RegistryValueKind.DWord);
        AddCreateValueMenuItem(createValueItem, "QWORD (64 бита)", RegistryValueKind.QWord);
        AddCreateValueMenuItem(createValueItem, "Мультистроковый параметр", RegistryValueKind.MultiString);
        AddCreateValueMenuItem(createValueItem, "Двоичный параметр", RegistryValueKind.Binary);
        keyMenu.Items.Add(createKeyItem);
        keyMenu.Items.Add(createValueItem);
        keyMenu.Opening += (_, _) =>
        {
            var hasKey = GetSelectedKeyContext() is not null;
            createKeyItem.Enabled = hasKey;
            createValueItem.Enabled = hasKey;
            UiTheme.HideUnavailableContextMenuItems(keyMenu);
        };
        _keyTree.NodeMouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Right)
            {
                _keyTree.SelectedNode = args.Node;
            }
        };
        _keyTree.ContextMenuStrip = keyMenu;

        _valueList.Dock = DockStyle.Fill;
        _valueList.View = View.Details;
        _valueList.FullRowSelect = true;
        _valueList.GridLines = true;
        _valueList.Columns.Add("Имя", 220);
        _valueList.Columns.Add("Тип", 140);
        _valueList.Columns.Add("Значение", 520);
        _valueList.DoubleClick += (_, _) => EditSelectedValue();
        UiTheme.StyleListView(_valueList);

        var menu = new ContextMenuStrip();
        UiTheme.StyleContextMenu(menu);
        var editMenuItem = menu.Items.Add("Изменить", null, (_, _) => EditSelectedValue());
        var deleteMenuItem = menu.Items.Add("Удалить значение", null, (_, _) => DeleteSelectedValue());
        menu.Opening += (_, e) =>
        {
            var hasSelection = _valueList.SelectedItems.Count > 0;
            editMenuItem.Enabled = hasSelection;
            deleteMenuItem.Enabled = hasSelection;
            UiTheme.HideUnavailableContextMenuItems(menu);
        };
        _valueList.ContextMenuStrip = menu;

        split.Panel1.Controls.Add(_keyTree);
        split.Panel2.Controls.Add(_valueList);

        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(0, 10, 0, 0);
        _statusLabel.ForeColor = UiTheme.MutedText;

        root.Controls.Add(pathPanel, 0, 0);
        root.Controls.Add(split, 0, 1);
        root.Controls.Add(_statusLabel, 0, 2);
        Controls.Add(root);
    }

    private void LoadRoots()
    {
        _keyTree.Nodes.Clear();
        _roots.Clear();

        if (_session.IsWinPe && _session.WindowsPath is not null && !_session.DriveRoot.StartsWith("X:", StringComparison.OrdinalIgnoreCase))
        {
            AddOfflineWindowsRoots();
        }
        else
        {
            AddLiveRoot("HKEY_CLASSES_ROOT", Registry.ClassesRoot);
            AddLiveRoot("HKEY_CURRENT_USER", Registry.CurrentUser);
            AddLiveRoot("HKEY_LOCAL_MACHINE", Registry.LocalMachine);
            AddLiveRoot("HKEY_USERS", Registry.Users);
            AddLiveRoot("HKEY_CURRENT_CONFIG", Registry.CurrentConfig);
        }

        _statusLabel.Text = _roots.Count == 0 ? "Нет доступных hive." : "Двойной клик по значению открывает редактирование.";
        SchedulePathSuggestions(_pathBox.Text);
        SelectInitialTarget();
    }

    private void AddOfflineWindowsRoots()
    {
        var windowsPath = _session.WindowsPath;
        if (string.IsNullOrWhiteSpace(windowsPath))
        {
            return;
        }

        var localMachineNode = AddLogicalRoot("HKEY_LOCAL_MACHINE");
        var usersNode = AddLogicalRoot("HKEY_USERS");

        try
        {
            AddOfflineRoot(localMachineNode, "SOFTWARE", Path.Combine(windowsPath, "System32", "config", "SOFTWARE"));
            AddOfflineRoot(localMachineNode, "SYSTEM", Path.Combine(windowsPath, "System32", "config", "SYSTEM"));
            AddOfflineRoot(localMachineNode, "SAM", Path.Combine(windowsPath, "System32", "config", "SAM"));
            AddOfflineRoot(localMachineNode, "SECURITY", Path.Combine(windowsPath, "System32", "config", "SECURITY"));
            AddOfflineRoot(usersNode, "DEFAULT", Path.Combine(windowsPath, "System32", "config", "DEFAULT"));
            AddOfflineRoot(localMachineNode, "COMPONENTS", Path.Combine(windowsPath, "System32", "config", "COMPONENTS"));
            AddOfflineRoot(localMachineNode, "BCD", Path.Combine(_session.DriveRoot, "Boot", "BCD"));
            AddOfflineUserRoots(usersNode);
        }
        finally
        {
            localMachineNode.Expand();
            usersNode.Expand();
        }
    }

    private TreeNode AddLiveRoot(string name, RegistryKey rootKey)
    {
        var context = new RegistryRootContext(name, rootKey.Name, rootKey, null, GetLiveRegistryView());
        _roots.Add(context);
        var node = new TreeNode(name) { Tag = new RegistryNodeContext(context, string.Empty) };
        AddLoadingNode(node);
        _keyTree.Nodes.Add(node);
        return node;
    }

    private TreeNode AddLogicalRoot(string name)
    {
        var node = new TreeNode(name);
        _keyTree.Nodes.Add(node);
        return node;
    }

    private void AddOfflineRoot(TreeNode parentNode, string name, string hiveFile)
    {
        if (!File.Exists(hiveFile))
        {
            return;
        }

        try
        {
            var mount = OfflineRegistryHiveMount.Load(hiveFile, $"IUnlocker_EDIT_{name}");
            var parentPath = GetNodeDisplayPath(parentNode);
            var displayPath = $@"{parentPath}\{name}";
            var context = new RegistryRootContext(displayPath, displayPath, null, mount, RegistryView.Default);
            _roots.Add(context);
            var node = new TreeNode(name) { Tag = new RegistryNodeContext(context, string.Empty) };
            AddLoadingNode(node);
            parentNode.Nodes.Add(node);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Не удалось загрузить {name}: {ex.Message}";
        }
    }

    private void AddOfflineUserRoots(TreeNode usersNode)
    {
        var usersRoot = Path.Combine(_session.DriveRoot, "Users");
        if (!Directory.Exists(usersRoot))
        {
            return;
        }

        foreach (var profile in Directory.EnumerateDirectories(usersRoot).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
        {
            var hiveFile = Path.Combine(profile, "NTUSER.DAT");
            if (!File.Exists(hiveFile))
            {
                continue;
            }

            AddOfflineRoot(usersNode, Path.GetFileName(profile), hiveFile);
        }
    }

    private void SelectInitialTarget()
    {
        if (string.IsNullOrWhiteSpace(_initialKeyPath))
        {
            return;
        }

        TreeNode? rootNode = null;
        foreach (var node in EnumerateContextNodes())
        {
            var context = (RegistryNodeContext)node.Tag!;
            var rootMatches = !string.IsNullOrWhiteSpace(_initialRootName) &&
                              RootMatches(context.Root, _initialRootName);
            var hiveMatches = !string.IsNullOrWhiteSpace(_initialHiveFile) &&
                              context.Root.HiveFile?.Equals(_initialHiveFile, StringComparison.OrdinalIgnoreCase) == true;

            if (rootMatches || hiveMatches)
            {
                rootNode = node;
                break;
            }
        }

        if (rootNode is null)
        {
            return;
        }

        var nodeToSelect = EnsurePathNode(rootNode, _initialKeyPath);
        if (nodeToSelect is null)
        {
            return;
        }

        nodeToSelect.EnsureVisible();
        _keyTree.SelectedNode = nodeToSelect;
        LoadValues();
        SelectInitialValue();
    }

    private TreeNode? EnsurePathNode(TreeNode rootNode, string keyPath)
    {
        if (rootNode.Tag is not RegistryNodeContext context)
        {
            return null;
        }

        var currentNode = rootNode;
        foreach (var part in keyPath.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (HasLoadingNode(currentNode))
            {
                LoadChildKeys(currentNode, (RegistryNodeContext)currentNode.Tag);
            }

            TreeNode? nextNode = null;
            foreach (TreeNode child in currentNode.Nodes)
            {
                if (child.Text.Equals(part, StringComparison.OrdinalIgnoreCase))
                {
                    nextNode = child;
                    break;
                }
            }

            if (nextNode is null)
            {
                return null;
            }

            currentNode = nextNode;
        }

        return currentNode;
    }

    private void SelectInitialValue()
    {
        if (_initialValueName is null)
        {
            return;
        }

        foreach (ListViewItem item in _valueList.Items)
        {
            if (item.Tag is not RegistryValueContext valueContext ||
                !valueContext.ValueName.Equals(_initialValueName, StringComparison.Ordinal))
            {
                continue;
            }

            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
            _valueList.Focus();
            return;
        }
    }

    private void KeyTreeBeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        if (e.Node is null || !HasLoadingNode(e.Node) || e.Node.Tag is not RegistryNodeContext context)
        {
            return;
        }

        LoadChildKeys(e.Node, context);
    }

    private void LoadChildKeys(TreeNode node, RegistryNodeContext context)
    {
        RemoveLoadingNodes(node);

        try
        {
            using var key = OpenKey(context, writable: false);
            if (key is null)
            {
                return;
            }

            foreach (var subKeyName in key.GetSubKeyNames().OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
            {
                if (ShouldHideMountedIUnlockerHive(subKeyName))
                {
                    continue;
                }

                if (HasChildNode(node, subKeyName))
                {
                    continue;
                }

                var childPath = string.IsNullOrWhiteSpace(context.Path) ? subKeyName : $@"{context.Path}\{subKeyName}";
                var child = new TreeNode(subKeyName) { Tag = new RegistryNodeContext(context.Root, childPath) };
                AddLoadingNode(child);
                node.Nodes.Add(child);
            }
        }
        catch (Exception ex)
        {
            node.Nodes.Add(new TreeNode($"Ошибка: {ex.Message}"));
        }
    }

    private void LoadValues()
    {
        _valueList.Items.Clear();
        if (_keyTree.SelectedNode?.Tag is not RegistryNodeContext context)
        {
            return;
        }

        try
        {
            using var key = OpenKey(context, writable: false);
            if (key is null)
            {
                return;
            }

            var valueNames = key.GetValueNames();
            if (!valueNames.Contains(string.Empty, StringComparer.Ordinal))
            {
                AddValueListItem(context, key, string.Empty, defaultValueIsMissing: true);
            }

            foreach (var valueName in valueNames.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
            {
                AddValueListItem(context, key, valueName, defaultValueIsMissing: false);
            }

            _statusLabel.Text = string.IsNullOrWhiteSpace(context.Path)
                ? context.Root.DisplayPath
                : $@"{context.Root.DisplayPath}\{context.Path}";
            UpdatePathBox(context);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = ex.Message;
        }
    }

    private void AddValueListItem(RegistryNodeContext context, RegistryKey key, string valueName, bool defaultValueIsMissing)
    {
        var displayName = string.IsNullOrEmpty(valueName) ? "(по умолчанию)" : valueName;
        var kind = defaultValueIsMissing ? RegistryValueKind.String : SafeGetValueKind(key, valueName);
        var value = defaultValueIsMissing ? "(значение не присвоено)" : ValueToString(key.GetValue(valueName));
        var item = new ListViewItem(displayName);
        item.SubItems.Add(FormatRegistryValueKind(kind));
        item.SubItems.Add(value);
        item.Tag = new RegistryValueContext(context, valueName, kind);
        _valueList.Items.Add(item);
    }

    private void UpdatePathBox(RegistryNodeContext context)
    {
        _settingPathText = true;
        try
        {
            _pathBox.Text = string.IsNullOrWhiteSpace(context.Path)
                ? context.Root.DisplayPath
                : $@"{context.Root.DisplayPath}\{context.Path}";
        }
        finally
        {
            _settingPathText = false;
        }
    }

    private void SchedulePathSuggestions(string rawPath)
    {
        _pendingPathSuggestionText = rawPath;
        _pathSuggestionTimer.Stop();

        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        _pathSuggestionTimer.Start();
    }

    private void UpdatePathSuggestions(string rawPath)
    {
        try
        {
            var suggestions = BuildPathSuggestions(rawPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(60)
                .ToArray();

            var previousMode = _pathBox.AutoCompleteMode;
            _pathBox.AutoCompleteMode = AutoCompleteMode.None;
            _pathSuggestions.Clear();
            if (suggestions.Length > 0)
            {
                _pathSuggestions.AddRange(suggestions);
            }

            _pathBox.AutoCompleteMode = previousMode;
        }
        catch
        {
            _pathBox.AutoCompleteMode = AutoCompleteMode.None;
            _pathSuggestions.Clear();
            _pathBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        }
    }

    private IEnumerable<string> BuildPathSuggestions(string rawPath)
    {
        var path = NormalizeRegistryPath(rawPath, trimTrailingSeparators: false);
        if (string.IsNullOrWhiteSpace(path) || !path.Contains('\\'))
        {
            foreach (var root in _roots)
            {
                if (string.IsNullOrWhiteSpace(path) ||
                    root.DisplayPath.StartsWith(path, StringComparison.OrdinalIgnoreCase) ||
                    GetRootAliases(root).Any(alias => alias.StartsWith(path, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return root.DisplayPath;
                }
            }

            yield break;
        }

        var target = ResolvePathForCompletion(path);
        if (target is null)
        {
            yield break;
        }

        var parentPath = target.ParentPath;
        var prefix = target.Prefix;
        if (string.IsNullOrWhiteSpace(prefix) && parentPath.Length == 0 && target.Root.LiveHive == RegistryHive.ClassesRoot)
        {
            yield break;
        }

        var parentContext = new RegistryNodeContext(target.Root, parentPath);

        using var key = OpenKey(parentContext, writable: false);
        if (key is null)
        {
            yield break;
        }

        string[] subKeyNames;
        try
        {
            subKeyNames = key.GetSubKeyNames();
        }
        catch
        {
            yield break;
        }

        var matches = new List<string>();
        foreach (var subKeyName in subKeyNames)
        {
            if (!string.IsNullOrWhiteSpace(prefix) &&
                !subKeyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matches.Add(string.IsNullOrWhiteSpace(parentPath)
                ? $@"{target.Root.DisplayPath}\{subKeyName}"
                : $@"{target.Root.DisplayPath}\{parentPath}\{subKeyName}");

            if (matches.Count >= 60)
            {
                break;
            }
        }

        foreach (var match in matches.OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase))
        {
            yield return match;
        }
    }

    private void NavigateToPath()
    {
        var rawPath = NormalizeRegistryPath(_pathBox.Text);
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            MessageBox.Show(this, "Введите путь реестра.", "Переход по пути", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var target = ResolvePath(rawPath);
            if (target is null)
            {
                MessageBox.Show(this, "Корень реестра не найден.", "Переход по пути", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var node = EnsurePathNode(target.RootNode, target.KeyPath);
            if (node is null)
            {
                MessageBox.Show(this, "Ключ реестра не найден или недоступен.", "Переход по пути", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            node.EnsureVisible();
            _keyTree.SelectedNode = node;
            _keyTree.Focus();
            LoadValues();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Переход по пути", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenSearchForm()
    {
        if (_roots.Count == 0)
        {
            MessageBox.Show(this, "Сначала дождитесь загрузки корней реестра.", "Поиск по реестру", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_searchForm is { IsDisposed: false })
        {
            _searchForm.Activate();
            _searchForm.WindowState = FormWindowState.Normal;
            return;
        }

        _searchForm = new RegistrySearchForm(this);
        _searchForm.FormClosed += (_, _) => _searchForm = null;
        _searchForm.Show();
    }

    private void NavigateToSearchResult(RegistrySearchResult result)
    {
        var rootNode = EnumerateContextNodes()
            .FirstOrDefault(node =>
                node.Tag is RegistryNodeContext context &&
                ReferenceEquals(context.Root, result.Key.Root) &&
                string.IsNullOrWhiteSpace(context.Path));

        if (rootNode is null)
        {
            MessageBox.Show(this, "Корень результата уже недоступен.", "Поиск по реестру", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var node = EnsurePathNode(rootNode, result.Key.Path);
        if (node is null)
        {
            MessageBox.Show(this, "Ключ результата не найден или недоступен.", "Поиск по реестру", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        node.EnsureVisible();
        _keyTree.SelectedNode = node;
        _keyTree.Focus();
        LoadValues();

        if (result.ValueName is not null)
        {
            SelectValue(result.ValueName);
        }
    }

    private void SelectValue(string valueName)
    {
        foreach (ListViewItem item in _valueList.Items)
        {
            if (item.Tag is not RegistryValueContext valueContext ||
                !valueContext.ValueName.Equals(valueName, StringComparison.Ordinal))
            {
                continue;
            }

            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
            _valueList.Focus();
            return;
        }
    }

    private PathTarget? ResolvePath(string rawPath)
    {
        var path = NormalizeRegistryPath(rawPath);
        var bestMatchLength = -1;
        TreeNode? bestRootNode = null;
        RegistryRootContext? bestRoot = null;
        string matchedAlias = string.Empty;

        foreach (var rootNode in EnumerateContextNodes())
        {
            var context = (RegistryNodeContext)rootNode.Tag!;
            foreach (var alias in GetRootAliases(context.Root).OrderByDescending(alias => alias.Length))
            {
                if (!PathStartsWithRoot(path, alias) || alias.Length <= bestMatchLength)
                {
                    continue;
                }

                bestMatchLength = alias.Length;
                bestRootNode = rootNode;
                bestRoot = context.Root;
                matchedAlias = alias;
            }
        }

        if (bestRootNode is not null && bestRoot is not null)
        {
            var keyPath = path.Length == matchedAlias.Length
                ? string.Empty
                : path[(matchedAlias.Length + 1)..];
            return new PathTarget(bestRootNode, keyPath);
        }

        if (_keyTree.SelectedNode?.Tag is RegistryNodeContext selectedContext)
        {
            var selectedRootNode = GetRootNode(_keyTree.SelectedNode);
            if (selectedRootNode is not null)
            {
                return new PathTarget(selectedRootNode, path.Trim('\\'));
            }
        }

        return null;
    }

    private CompletionTarget? ResolvePathForCompletion(string rawPath)
    {
        var path = rawPath.Trim().Replace('/', '\\');
        var endsWithSlash = path.EndsWith('\\');
        path = path.TrimEnd('\\');
        var bestMatchLength = -1;
        RegistryRootContext? bestRoot = null;
        string matchedAlias = string.Empty;

        foreach (var rootNode in EnumerateContextNodes())
        {
            var context = (RegistryNodeContext)rootNode.Tag!;
            foreach (var alias in GetRootAliases(context.Root).OrderByDescending(alias => alias.Length))
            {
                if (!PathStartsWithRoot(path, alias) || alias.Length <= bestMatchLength)
                {
                    continue;
                }

                bestMatchLength = alias.Length;
                bestRoot = context.Root;
                matchedAlias = alias;
            }
        }

        if (bestRoot is null)
        {
            return null;
        }

        var keyPath = path.Length == matchedAlias.Length
            ? string.Empty
            : path[(matchedAlias.Length + 1)..];
        if (endsWithSlash)
        {
            return new CompletionTarget(bestRoot, keyPath, string.Empty);
        }

        var lastSlash = keyPath.LastIndexOf('\\');
        var parentPath = lastSlash < 0 ? string.Empty : keyPath[..lastSlash];
        var prefix = lastSlash < 0 ? keyPath : keyPath[(lastSlash + 1)..];
        return new CompletionTarget(bestRoot, parentPath, prefix);
    }

    private static bool RootMatches(RegistryRootContext root, string rootName)
    {
        return GetRootAliases(root).Any(alias => alias.Equals(rootName, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> GetRootAliases(RegistryRootContext root)
    {
        yield return root.Name;
        yield return root.DisplayPath;

        if (root.LiveRootKey is null)
        {
            yield break;
        }

        foreach (var alias in root.LiveHive switch
        {
            RegistryHive.ClassesRoot => new[] { "HKCR", "HKEY_CLASSES_ROOT" },
            RegistryHive.CurrentUser => new[] { "HKCU", "HKEY_CURRENT_USER" },
            RegistryHive.LocalMachine => new[] { "HKLM", "HKEY_LOCAL_MACHINE" },
            RegistryHive.Users => new[] { "HKU", "HKEY_USERS" },
            RegistryHive.CurrentConfig => new[] { "HKCC", "HKEY_CURRENT_CONFIG" },
            _ => Array.Empty<string>(),
        })
        {
            yield return alias;
        }
    }

    private static bool PathStartsWithRoot(string path, string rootAlias)
    {
        return path.Equals(rootAlias, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(rootAlias + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRegistryPath(string rawPath, bool trimTrailingSeparators = true)
    {
        var normalized = rawPath
            .Trim()
            .Replace('/', '\\')
            .TrimStart('\\');

        return trimTrailingSeparators
            ? normalized.TrimEnd('\\')
            : normalized;
    }

    private static TreeNode? GetRootNode(TreeNode node)
    {
        var current = node;
        while (current.Parent is not null)
        {
            current = current.Parent;
        }

        return current;
    }

    private void EditSelectedValue()
    {
        if (_valueList.SelectedItems.Count == 0 ||
            _valueList.SelectedItems[0].Tag is not RegistryValueContext valueContext)
        {
            return;
        }

        using var key = OpenKey(valueContext.Key, writable: false);
        if (key is null)
        {
            return;
        }

        var currentValue = key.GetValue(valueContext.ValueName);
        var entry = new StartupEntry(
            "Registry",
            string.IsNullOrEmpty(valueContext.ValueName) ? "(по умолчанию)" : valueContext.ValueName,
            "Реестр",
            valueContext.Key.Root.Name,
            "Registry editor",
            ValueToString(currentValue),
            $@"{valueContext.Key.Root.DisplayPath}\{valueContext.Key.Path}\{valueContext.ValueName}",
            RegistryKeyPath: valueContext.Key.Path,
            RegistryValueName: valueContext.ValueName,
            RegistryValueKind: valueContext.Kind,
            RegistryEditText: ValueToEditableString(currentValue));

        using var form = new RegistryValueEditForm(entry);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        using var writableKey = OpenKey(valueContext.Key, writable: true)
            ?? throw new InvalidOperationException("Ключ недоступен для записи.");
        writableKey.SetValue(valueContext.ValueName, ConvertRegistryValue(form.EditedText, valueContext.Kind), valueContext.Kind);
        LoadValues();
    }

    private void DeleteSelectedValue()
    {
        if (_valueList.SelectedItems.Count == 0 ||
            _valueList.SelectedItems[0].Tag is not RegistryValueContext valueContext)
        {
            return;
        }

        if (MessageBox.Show(this, "Удалить выбранное значение?", "Реестр", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        using var key = OpenKey(valueContext.Key, writable: true)
            ?? throw new InvalidOperationException("Ключ недоступен для записи.");
        key.DeleteValue(valueContext.ValueName, throwOnMissingValue: true);
        LoadValues();
    }

    private RegistryNodeContext? GetSelectedKeyContext()
    {
        return _keyTree.SelectedNode?.Tag as RegistryNodeContext;
    }

    private void AddCreateValueMenuItem(ToolStripMenuItem parent, string text, RegistryValueKind kind)
    {
        parent.DropDownItems.Add(text, null, (_, _) => CreateRegistryValue(kind));
    }

    private void CreateSubKey()
    {
        var context = GetSelectedKeyContext();
        if (context is null)
        {
            return;
        }

        using var dialog = new TextEditForm("Создать раздел", "Имя нового раздела", string.Empty);
        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.EditedText))
        {
            return;
        }

        if (dialog.EditedText.Contains('\\'))
        {
            MessageBox.Show(this, "Имя раздела не должно содержать символ \\.", "Реестр", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            using var key = OpenKey(context, writable: true)
                ?? throw new InvalidOperationException("Ключ недоступен для записи.");
            key.CreateSubKey(dialog.EditedText, writable: true)?.Dispose();
            RefreshSelectedKeyNode();
            _statusLabel.Text = $"Создан раздел: {dialog.EditedText}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Реестр", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CreateRegistryValue(RegistryValueKind kind)
    {
        var context = GetSelectedKeyContext();
        if (context is null)
        {
            return;
        }

        using var nameDialog = new TextEditForm("Создать параметр", "Имя параметра", string.Empty);
        if (nameDialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(nameDialog.EditedText))
        {
            return;
        }

        using var valueDialog = new TextEditForm("Создать параметр", $"Значение ({FormatRegistryValueKind(kind)})", string.Empty);
        if (valueDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            using var key = OpenKey(context, writable: true)
                ?? throw new InvalidOperationException("Ключ недоступен для записи.");
            key.SetValue(nameDialog.EditedText, ConvertRegistryValue(valueDialog.EditedText, kind), kind);
            LoadValues();
            _statusLabel.Text = $"Создан параметр: {nameDialog.EditedText}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Реестр", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RefreshSelectedKeyNode()
    {
        if (_keyTree.SelectedNode?.Tag is not RegistryNodeContext context)
        {
            return;
        }

        _keyTree.SelectedNode.Nodes.Clear();
        LoadChildKeys(_keyTree.SelectedNode, context);
        _keyTree.SelectedNode.Expand();
    }

    private static RegistryKey? OpenKey(RegistryNodeContext context, bool writable)
    {
        using var baseKey = OpenRootKey(context.Root);
        if (baseKey is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(context.Path))
        {
            return OpenRootKey(context.Root, writable);
        }

        return baseKey.OpenSubKey(context.Path, writable);
    }

    private static RegistryKey? OpenRootKey(RegistryRootContext root, bool writable = false)
    {
        if (root.LiveRootKey is not null)
        {
            return RegistryKey.OpenBaseKey(root.LiveHive, root.LiveView);
        }

        return Registry.LocalMachine.OpenSubKey(root.MountName!, writable);
    }

    private static RegistryView GetLiveRegistryView()
    {
        return Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default;
    }

    private static RegistryValueKind SafeGetValueKind(RegistryKey key, string valueName)
    {
        try
        {
            return key.GetValueKind(valueName);
        }
        catch
        {
            return RegistryValueKind.String;
        }
    }

    private static string FormatRegistryValueKind(RegistryValueKind kind)
    {
        return kind switch
        {
            RegistryValueKind.String => "REG_SZ",
            RegistryValueKind.ExpandString => "REG_EXPAND_SZ",
            RegistryValueKind.Binary => "REG_BINARY",
            RegistryValueKind.DWord => "REG_DWORD",
            RegistryValueKind.MultiString => "REG_MULTI_SZ",
            RegistryValueKind.QWord => "REG_QWORD",
            RegistryValueKind.None => "REG_NONE",
            _ => kind.ToString(),
        };
    }

    private static void AddLoadingNode(TreeNode node)
    {
        if (!HasLoadingNode(node))
        {
            node.Nodes.Add(new TreeNode("Загрузка..."));
        }
    }

    private static bool HasLoadingNode(TreeNode node)
    {
        return node.Nodes.Cast<TreeNode>().Any(child => child.Text == "Загрузка...");
    }

    private static void RemoveLoadingNodes(TreeNode node)
    {
        foreach (var child in node.Nodes.Cast<TreeNode>().Where(child => child.Text == "Загрузка...").ToList())
        {
            node.Nodes.Remove(child);
        }
    }

    private static bool HasChildNode(TreeNode node, string text)
    {
        return node.Nodes.Cast<TreeNode>().Any(child => child.Text.Equals(text, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldHideMountedIUnlockerHive(string subKeyName)
    {
        return subKeyName.StartsWith("IUnlocker_EDIT_", StringComparison.OrdinalIgnoreCase) ||
               subKeyName.StartsWith("IUnlocker_DETECT_", StringComparison.OrdinalIgnoreCase) ||
               subKeyName.StartsWith("IUnlocker_SOFTWARE_", StringComparison.OrdinalIgnoreCase) ||
               subKeyName.StartsWith("IUnlocker_SYSTEM_", StringComparison.OrdinalIgnoreCase) ||
               subKeyName.StartsWith("IUnlocker_SAM_", StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<TreeNode> EnumerateContextNodes()
    {
        foreach (TreeNode node in _keyTree.Nodes)
        {
            foreach (var child in EnumerateContextNodes(node))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<TreeNode> EnumerateContextNodes(TreeNode node)
    {
        if (node.Tag is RegistryNodeContext)
        {
            yield return node;
        }

        foreach (TreeNode child in node.Nodes)
        {
            foreach (var nested in EnumerateContextNodes(child))
            {
                yield return nested;
            }
        }
    }

    private static string GetNodeDisplayPath(TreeNode node)
    {
        if (node.Tag is RegistryNodeContext context)
        {
            return string.IsNullOrWhiteSpace(context.Path)
                ? context.Root.DisplayPath
                : $@"{context.Root.DisplayPath}\{context.Path}";
        }

        return node.FullPath;
    }

    private void DisposeRoots()
    {
        foreach (var root in _roots)
        {
            root.Dispose();
        }

        _roots.Clear();
    }

    private static string ValueToString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            string[] values => string.Join("; ", values),
            byte[] bytes => $"0x{Convert.ToHexString(bytes)}",
            _ => Convert.ToString(value) ?? string.Empty,
        };
    }

    private static string ValueToEditableString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            string[] values => string.Join(Environment.NewLine, values),
            byte[] bytes => Convert.ToHexString(bytes),
            _ => Convert.ToString(value) ?? string.Empty,
        };
    }

    private static object ConvertRegistryValue(string text, RegistryValueKind kind)
    {
        return kind switch
        {
            RegistryValueKind.DWord => int.Parse(text.Trim()),
            RegistryValueKind.QWord => long.Parse(text.Trim()),
            RegistryValueKind.MultiString => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.None),
            RegistryValueKind.Binary => Convert.FromHexString(text.Replace(" ", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal)),
            _ => text,
        };
    }

    private sealed class RegistryRootContext : IDisposable
    {
        private readonly OfflineRegistryHiveMount? _mount;

        public RegistryRootContext(string name, string displayPath, RegistryKey? liveRootKey, OfflineRegistryHiveMount? mount, RegistryView liveView)
        {
            Name = name;
            DisplayPath = displayPath;
            LiveRootKey = liveRootKey;
            LiveView = liveView;
            _mount = mount;
            HiveFile = mount?.HiveFile;
            MountName = mount?.MountName;
            LiveHive = name switch
            {
                "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
                "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
                "HKCR" or "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
                "HKU" or "HKEY_USERS" => RegistryHive.Users,
                "HKCC" or "HKEY_CURRENT_CONFIG" => RegistryHive.CurrentConfig,
                _ => RegistryHive.LocalMachine,
            };
        }

        public string Name { get; }

        public string DisplayPath { get; }

        public RegistryKey? LiveRootKey { get; }

        public RegistryView LiveView { get; }

        public RegistryHive LiveHive { get; }

        public string? HiveFile { get; }

        public string? MountName { get; }

        public void Dispose()
        {
            _mount?.Dispose();
        }
    }

    private sealed record RegistryNodeContext(RegistryRootContext Root, string Path);

    private sealed record RegistryValueContext(RegistryNodeContext Key, string ValueName, RegistryValueKind Kind);

    private sealed record PathTarget(TreeNode RootNode, string KeyPath);

    private sealed record CompletionTarget(RegistryRootContext Root, string ParentPath, string Prefix);

    private sealed record RegistrySearchResult(
        string Type,
        string DisplayPath,
        string Name,
        string Data,
        RegistryNodeContext Key,
        string? ValueName);

    private sealed class RegistrySearchForm : Form
    {
        private readonly RegistryEditorForm _owner;
        private readonly TextBox _queryBox = new();
        private readonly Button _settingsButton = new();
        private readonly Button _startButton = new();
        private readonly Button _cancelButton = new();
        private readonly ContextMenuStrip _settingsMenu = new();
        private readonly ToolStripMenuItem _keysMenuItem = new("Искать ключи") { Checked = true, CheckOnClick = true };
        private readonly ToolStripMenuItem _valuesMenuItem = new("Искать значения") { Checked = true, CheckOnClick = true };
        private readonly ToolStripMenuItem _dataMenuItem = new("Искать данные") { Checked = true, CheckOnClick = true };
        private readonly ToolStripMenuItem _exactMatchMenuItem = new("Точное совпадение") { Checked = false, CheckOnClick = true };
        private readonly ListView _results = new();
        private readonly Label _statusLabel = new();
        private CancellationTokenSource? _searchCancellation;
        private int _resultCount;

        public RegistrySearchForm(RegistryEditorForm owner)
        {
            _owner = owner;
            BuildInterface();
            FormClosing += (_, _) => _searchCancellation?.Cancel();
        }

        private void BuildInterface()
        {
            Text = "iUnlocker - поиск по реестру";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(900, 520);
            ClientSize = new Size(1050, 620);
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
                ColumnCount = 4,
                RowCount = 1,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10),
            };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _queryBox.Dock = DockStyle.Fill;
            _queryBox.PlaceholderText = "Что искать";
            _queryBox.Margin = new Padding(0, 0, 10, 0);
            _queryBox.KeyDown += (_, args) =>
            {
                if (args.KeyCode == Keys.Enter)
                {
                    args.SuppressKeyPress = true;
                    BeginSearch();
                }
            };
            UiTheme.StyleTextBox(_queryBox);

            UiTheme.StyleContextMenu(_settingsMenu);
            _settingsMenu.Items.AddRange(new ToolStripItem[]
            {
                _keysMenuItem,
                _valuesMenuItem,
                _dataMenuItem,
                new ToolStripSeparator(),
                _exactMatchMenuItem,
            });

            _settingsButton.Text = "Настройки";
            _settingsButton.AutoSize = true;
            _settingsButton.Margin = new Padding(0, 0, 0, 0);
            _settingsButton.Click += (_, _) => _settingsMenu.Show(_settingsButton, new Point(0, _settingsButton.Height));
            UiTheme.StyleButton(_settingsButton);

            _startButton.Text = "Сканировать";
            _startButton.AutoSize = true;
            _startButton.Margin = new Padding(8, 0, 0, 0);
            _startButton.Click += (_, _) => BeginSearch();
            UiTheme.StyleButton(_startButton, primary: true);

            _cancelButton.Text = "Стоп";
            _cancelButton.AutoSize = true;
            _cancelButton.Margin = new Padding(8, 0, 0, 0);
            _cancelButton.Enabled = false;
            _cancelButton.Click += (_, _) => _searchCancellation?.Cancel();
            UiTheme.StyleButton(_cancelButton);

            toolbar.Controls.Add(_queryBox, 0, 0);
            toolbar.Controls.Add(_settingsButton, 1, 0);
            toolbar.Controls.Add(_startButton, 2, 0);
            toolbar.Controls.Add(_cancelButton, 3, 0);

            _results.Dock = DockStyle.Fill;
            _results.View = View.Details;
            _results.FullRowSelect = true;
            _results.HideSelection = false;
            _results.MultiSelect = false;
            _results.Columns.Add("Тип", 100);
            _results.Columns.Add("Путь", 470);
            _results.Columns.Add("Имя", 180);
            _results.Columns.Add("Данные", 360);
            _results.DoubleClick += (_, _) => OpenSelectedResult();
            UiTheme.StyleListView(_results);

            _statusLabel.AutoSize = true;
            _statusLabel.ForeColor = UiTheme.MutedText;
            _statusLabel.Padding = new Padding(0, 10, 0, 0);
            _statusLabel.Text = "Введите текст и нажмите сканирование. Двойной клик по результату открывает его.";

            root.Controls.Add(toolbar, 0, 0);
            root.Controls.Add(_results, 0, 1);
            root.Controls.Add(_statusLabel, 0, 2);
            Controls.Add(root);
        }

        private async void BeginSearch()
        {
            var query = _queryBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                MessageBox.Show(this, "Введите текст для поиска.", "Поиск по реестру", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!_keysMenuItem.Checked && !_valuesMenuItem.Checked && !_dataMenuItem.Checked)
            {
                MessageBox.Show(this, "Выберите хотя бы один тип поиска.", "Поиск по реестру", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _searchCancellation?.Cancel();
            _searchCancellation?.Dispose();
            _searchCancellation = new CancellationTokenSource();
            var token = _searchCancellation.Token;
            var options = new RegistrySearchOptions(
                _keysMenuItem.Checked,
                _valuesMenuItem.Checked,
                _dataMenuItem.Checked,
                _exactMatchMenuItem.Checked);

            _results.Items.Clear();
            _resultCount = 0;
            SetSearchingState(true);
            _statusLabel.Text = "Идёт поиск...";

            var progress = new Progress<IReadOnlyList<RegistrySearchResult>>(AddResults);

            try
            {
                var summary = await Task.Run(() => _owner.SearchRegistry(query, options, token, progress), token);
                _statusLabel.Text = summary.LimitReached
                    ? $"Показано первых {summary.Results} результатов. Ошибок доступа: {summary.Errors}."
                    : $"Найдено: {summary.Results}. Ошибок доступа: {summary.Errors}.";
            }
            catch (OperationCanceledException)
            {
                _statusLabel.Text = $"Поиск остановлен. Показано: {_resultCount}.";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Ошибка поиска: {ex.Message}";
            }
            finally
            {
                SetSearchingState(false);
            }
        }

        private void AddResults(IReadOnlyList<RegistrySearchResult> results)
        {
            if (results.Count == 0)
            {
                return;
            }

            _results.BeginUpdate();
            try
            {
                foreach (var result in results)
                {
                    var item = new ListViewItem(result.Type);
                    item.SubItems.Add(result.DisplayPath);
                    item.SubItems.Add(result.Name);
                    item.SubItems.Add(result.Data);
                    item.Tag = result;
                    _results.Items.Add(item);
                }
            }
            finally
            {
                _results.EndUpdate();
            }

            _resultCount += results.Count;
            _statusLabel.Text = $"Идёт поиск... найдено: {_resultCount}";
        }

        private void SetSearchingState(bool searching)
        {
            _startButton.Enabled = !searching;
            _cancelButton.Enabled = searching;
            _queryBox.Enabled = !searching;
            _settingsButton.Enabled = !searching;
            _keysMenuItem.Enabled = !searching;
            _valuesMenuItem.Enabled = !searching;
            _dataMenuItem.Enabled = !searching;
            _exactMatchMenuItem.Enabled = !searching;
        }

        private void OpenSelectedResult()
        {
            if (_results.SelectedItems.Count == 0 ||
                _results.SelectedItems[0].Tag is not RegistrySearchResult result)
            {
                return;
            }

            _owner.NavigateToSearchResult(result);
        }
    }

    private RegistrySearchSummary SearchRegistry(
        string query,
        RegistrySearchOptions options,
        CancellationToken cancellationToken,
        IProgress<IReadOnlyList<RegistrySearchResult>> progress)
    {
        var batch = new List<RegistrySearchResult>();
        var resultCount = 0;
        var errorCount = 0;
        var limitReached = false;
        var roots = _roots.ToArray();

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rootContext = new RegistryNodeContext(root, string.Empty);
            SearchKey(rootContext, query, options, cancellationToken, progress, batch, ref resultCount, ref errorCount, ref limitReached);

            if (limitReached)
            {
                break;
            }
        }

        FlushSearchBatch(progress, batch);
        return new RegistrySearchSummary(resultCount, errorCount, limitReached);
    }

    private void SearchKey(
        RegistryNodeContext context,
        string query,
        RegistrySearchOptions options,
        CancellationToken cancellationToken,
        IProgress<IReadOnlyList<RegistrySearchResult>> progress,
        List<RegistrySearchResult> batch,
        ref int resultCount,
        ref int errorCount,
        ref bool limitReached)
    {
        if (limitReached)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var displayPath = string.IsNullOrWhiteSpace(context.Path)
            ? context.Root.DisplayPath
            : $@"{context.Root.DisplayPath}\{context.Path}";

        if (options.SearchKeys && MatchesQuery(GetLastPathPart(displayPath), query, options.ExactMatch))
        {
            AddSearchResult(
                new RegistrySearchResult("Ключ", displayPath, GetLastPathPart(displayPath), string.Empty, context, null),
                progress,
                batch,
                ref resultCount,
                ref limitReached);
        }

        string[] subKeyNames;
        try
        {
            using var key = OpenKey(context, writable: false);
            if (key is null)
            {
                return;
            }

            foreach (var valueName in key.GetValueNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var displayName = string.IsNullOrEmpty(valueName) ? "(по умолчанию)" : valueName;
                var valueText = SafeValueToString(key, valueName);
                if (options.SearchValues && MatchesQuery(displayName, query, options.ExactMatch))
                {
                    AddSearchResult(
                        new RegistrySearchResult("Значение", displayPath, displayName, valueText, context, valueName),
                        progress,
                        batch,
                        ref resultCount,
                        ref limitReached);
                }

                if (limitReached)
                {
                    break;
                }

                if (options.SearchData && MatchesQuery(valueText, query, options.ExactMatch))
                {
                    AddSearchResult(
                        new RegistrySearchResult("Данные", displayPath, displayName, valueText, context, valueName),
                        progress,
                        batch,
                        ref resultCount,
                        ref limitReached);
                }

                if (limitReached)
                {
                    break;
                }
            }

            if (limitReached)
            {
                return;
            }

            subKeyNames = key.GetSubKeyNames();
        }
        catch
        {
            errorCount++;
            return;
        }

        foreach (var subKeyName in subKeyNames.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (ShouldHideMountedIUnlockerHive(subKeyName))
            {
                continue;
            }

            var childPath = string.IsNullOrWhiteSpace(context.Path) ? subKeyName : $@"{context.Path}\{subKeyName}";
            SearchKey(new RegistryNodeContext(context.Root, childPath), query, options, cancellationToken, progress, batch, ref resultCount, ref errorCount, ref limitReached);

            if (limitReached)
            {
                return;
            }
        }
    }

    private static void AddSearchResult(
        RegistrySearchResult result,
        IProgress<IReadOnlyList<RegistrySearchResult>> progress,
        List<RegistrySearchResult> batch,
        ref int resultCount,
        ref bool limitReached)
    {
        if (resultCount >= RegistrySearchMaxResults)
        {
            limitReached = true;
            return;
        }

        batch.Add(result);
        resultCount++;
        FlushSearchBatch(progress, batch);
    }

    private static void FlushSearchBatch(IProgress<IReadOnlyList<RegistrySearchResult>> progress, List<RegistrySearchResult> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        progress.Report(batch.ToArray());
        batch.Clear();
    }

    private static bool MatchesQuery(string text, string query, bool exactMatch)
    {
        return exactMatch
            ? text.Equals(query, StringComparison.CurrentCultureIgnoreCase)
            : text.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private static string GetLastPathPart(string path)
    {
        var index = path.LastIndexOf('\\');
        return index < 0 ? path : path[(index + 1)..];
    }

    private static string SafeValueToString(RegistryKey key, string valueName)
    {
        try
        {
            return ValueToString(key.GetValue(valueName));
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed record RegistrySearchOptions(bool SearchKeys, bool SearchValues, bool SearchData, bool ExactMatch);

    private sealed record RegistrySearchSummary(int Results, int Errors, bool LimitReached);
}
