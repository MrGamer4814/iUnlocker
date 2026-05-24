using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IUnlocker;

public sealed class FileExplorerForm : Form
{
    private const string LoadingNodeText = "Загрузка...";

    private readonly AppSession _session;
    private readonly TreeView _folderTree = new();
    private readonly ListView _fileList = new();
    private readonly ImageList _treeImages = new();
    private readonly ImageList _listImages = new();
    private readonly ContextMenuStrip _fileMenu = new();
    private readonly ToolStripMenuItem _openMenuItem = new("Открыть");
    private readonly ToolStripMenuItem _copyPathMenuItem = new("Копировать как путь");
    private readonly ToolStripMenuItem _cutMenuItem = new("Вырезать");
    private readonly ToolStripMenuItem _copyMenuItem = new("Копировать");
    private readonly ToolStripMenuItem _createShortcutMenuItem = new("Создать ярлык");
    private readonly ToolStripMenuItem _quarantineMenuItem = new("Добавить в карантин");
    private readonly ToolStripMenuItem _deleteMenuItem = new("Удалить");
    private readonly ToolStripMenuItem _renameMenuItem = new("Переименовать");
    private readonly ToolStripMenuItem _propertiesMenuItem = new("Свойства");
    private readonly Dictionary<string, string> _extensionImageKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _executableImageKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox _pathBox = new();
    private readonly Label _statusLabel = new();
    private readonly Button _upButton = new();
    private readonly Button _searchButton = new();
    private readonly Button _refreshButton = new();

    private string? _currentPath;
    private string? _initialPath;
    private string? _initialSelectPath;
    private FileSearchForm? _searchForm;
    private bool _syncingTreeSelection;

    public FileExplorerForm(AppSession session, string? initialPath = null, string? initialSelectPath = null)
    {
        _session = session;
        _initialPath = initialPath;
        _initialSelectPath = initialSelectPath;
        BuildInterface();
        Load += (_, _) => LoadDrives();
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - проводник";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 560);
        ClientSize = new Size(1120, 680);
        UiTheme.ApplyForm(this);
        SetupImages();

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
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _upButton.Text = "Вверх";
        _upButton.AutoSize = true;
        _upButton.Margin = new Padding(0, 0, 8, 10);
        _upButton.Click += (_, _) => NavigateUp();
        UiTheme.StyleButton(_upButton);

        _pathBox.Dock = DockStyle.Fill;
        _pathBox.Margin = new Padding(0, 0, 8, 10);
        _pathBox.KeyDown += PathBoxKeyDown;
        UiTheme.StyleTextBox(_pathBox);

        _searchButton.Text = "Поиск";
        _searchButton.AutoSize = true;
        _searchButton.Margin = new Padding(0, 0, 8, 10);
        _searchButton.Click += (_, _) => OpenSearchForm();
        UiTheme.StyleButton(_searchButton);

        _refreshButton.Text = "Обновить";
        _refreshButton.AutoSize = true;
        _refreshButton.Margin = new Padding(0, 0, 0, 10);
        _refreshButton.Click += (_, _) => RefreshCurrent();
        UiTheme.StyleButton(_refreshButton, primary: true);

        toolbar.Controls.Add(_upButton, 0, 0);
        toolbar.Controls.Add(_pathBox, 1, 0);
        toolbar.Controls.Add(_searchButton, 2, 0);
        toolbar.Controls.Add(_refreshButton, 3, 0);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 320,
            FixedPanel = FixedPanel.None,
            BorderStyle = BorderStyle.FixedSingle,
        };

        _folderTree.Dock = DockStyle.Fill;
        _folderTree.HideSelection = false;
        _folderTree.ImageList = _treeImages;
        _folderTree.BeforeExpand += FolderTreeBeforeExpand;
        _folderTree.AfterSelect += FolderTreeAfterSelect;
        UiTheme.StyleTree(_folderTree);

        _fileList.Dock = DockStyle.Fill;
        _fileList.SmallImageList = _listImages;
        _fileList.View = View.Details;
        _fileList.FullRowSelect = true;
        _fileList.GridLines = true;
        _fileList.HideSelection = false;
        _fileList.MultiSelect = false;
        _fileList.Columns.Add("Имя", 320);
        _fileList.Columns.Add("Тип", 140);
        _fileList.Columns.Add("Размер", 110, HorizontalAlignment.Right);
        _fileList.Columns.Add("Изменён", 170);
        _fileList.DoubleClick += FileListDoubleClick;
        _fileList.MouseDown += FileListMouseDown;
        SetupFileContextMenu();
        UiTheme.StyleListView(_fileList);

        split.Panel1.Controls.Add(_folderTree);
        split.Panel2.Controls.Add(_fileList);

        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(0, 10, 0, 0);
        _statusLabel.ForeColor = UiTheme.MutedText;

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(split, 0, 1);
        root.Controls.Add(_statusLabel, 0, 2);
        Controls.Add(root);
    }

    private void SetupFileContextMenu()
    {
        UiTheme.StyleContextMenu(_fileMenu, dark: true);
        _fileMenu.Renderer = new DarkContextMenuRenderer();
        _fileMenu.Opening += (_, e) =>
        {
            var hasSelection = GetSelectedListItem() is not null;
            foreach (ToolStripItem item in _fileMenu.Items)
            {
                item.Enabled = hasSelection || item is ToolStripSeparator;
            }

            if (GetSelectedListItem() is { IsDirectory: true })
            {
                _quarantineMenuItem.Enabled = false;
            }

            e.Cancel = !hasSelection;
        };

        _openMenuItem.Font = new Font(_fileMenu.Font, FontStyle.Bold);
        _openMenuItem.Click += (_, _) => OpenSelectedItem();
        _copyPathMenuItem.Click += (_, _) => CopySelectedPath();
        _cutMenuItem.Click += (_, _) => SetSelectedItemClipboard(cut: true);
        _copyMenuItem.Click += (_, _) => SetSelectedItemClipboard(cut: false);
        _createShortcutMenuItem.Click += (_, _) => CreateSelectedShortcut();
        _quarantineMenuItem.Click += (_, _) => QuarantineSelectedItem();
        _deleteMenuItem.Click += (_, _) => DeleteSelectedItem();
        _renameMenuItem.Click += (_, _) => RenameSelectedItem();
        _propertiesMenuItem.Click += (_, _) => ShowSelectedProperties();

        _fileMenu.Items.AddRange(new ToolStripItem[]
        {
            _openMenuItem,
            new ToolStripSeparator(),
            _copyPathMenuItem,
            new ToolStripSeparator(),
            _cutMenuItem,
            _copyMenuItem,
            new ToolStripSeparator(),
            _createShortcutMenuItem,
            _quarantineMenuItem,
            _deleteMenuItem,
            _renameMenuItem,
            new ToolStripSeparator(),
            _propertiesMenuItem,
        });

        _fileList.ContextMenuStrip = _fileMenu;
    }

    private void SetupImages()
    {
        _treeImages.ColorDepth = ColorDepth.Depth32Bit;
        _treeImages.ImageSize = new Size(16, 16);
        _treeImages.Images.Add("drive", ShellIconProvider.GetSmallIcon("C:\\", FileAttributes.Directory) ?? SystemIcons.WinLogo);
        _treeImages.Images.Add("folder", ShellIconProvider.GetSmallIcon("folder", FileAttributes.Directory) ?? SystemIcons.WinLogo);
        _treeImages.Images.Add("file", ShellIconProvider.GetSmallIcon(".file", FileAttributes.Normal) ?? SystemIcons.Application);
        _treeImages.Images.Add("warning", SystemIcons.Warning.ToBitmap());

        _listImages.ColorDepth = ColorDepth.Depth32Bit;
        _listImages.ImageSize = new Size(16, 16);
        _listImages.Images.Add("folder", ShellIconProvider.GetSmallIcon("folder", FileAttributes.Directory) ?? SystemIcons.WinLogo);
        _listImages.Images.Add("file", ShellIconProvider.GetSmallIcon(".file", FileAttributes.Normal) ?? SystemIcons.Application);
    }

    private void LoadDrives()
    {
        _folderTree.Nodes.Clear();

        foreach (var drive in DriveInfo.GetDrives().OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase))
        {
            var label = drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? $"{drive.Name} {drive.VolumeLabel}"
                : drive.Name;

            var node = new TreeNode(label)
            {
                Tag = drive.Name,
                ImageKey = "drive",
                SelectedImageKey = "drive",
            };

            if (drive.IsReady)
            {
                AddLoadingNode(node);
            }

            _folderTree.Nodes.Add(node);
        }

        var firstReadyDrive = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var startupPath = Directory.Exists(_initialPath)
            ? _initialPath
            : File.Exists(_initialPath)
                ? Path.GetDirectoryName(_initialPath)
                : null;

        startupPath ??= Directory.Exists(_session.DriveRoot)
            ? _session.DriveRoot
            : firstReadyDrive?.Name;

        if (startupPath is not null)
        {
            NavigateToPath(startupPath, syncTree: true);
        }
        else
        {
            _statusLabel.Text = "Нет готовых дисков.";
            _upButton.Enabled = false;
        }
    }

    private void FolderTreeBeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        if (e.Node?.Tag is not string path || !HasLoadingNode(e.Node))
        {
            return;
        }

        LoadChildDirectories(e.Node, path);
    }

    private void FolderTreeAfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (_syncingTreeSelection || e.Node?.Tag is not string path)
        {
            return;
        }

        NavigateToPath(path, syncTree: false);
    }

    private void FileListDoubleClick(object? sender, EventArgs e)
    {
        OpenSelectedItem();
    }

    private void FileListMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        var item = _fileList.GetItemAt(e.X, e.Y);
        if (item is null)
        {
            _fileList.SelectedItems.Clear();
            return;
        }

        item.Selected = true;
        item.Focused = true;
    }

    private void PathBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.SuppressKeyPress = true;
        NavigateToPath(_pathBox.Text.Trim(), syncTree: true);
    }

    private void NavigateUp()
    {
        if (string.IsNullOrWhiteSpace(_currentPath))
        {
            return;
        }

        var parent = Directory.GetParent(_currentPath);
        if (parent is not null)
        {
            NavigateToPath(parent.FullName, syncTree: true);
        }
    }

    private void RefreshCurrent()
    {
        if (string.IsNullOrWhiteSpace(_currentPath))
        {
            LoadDrives();
            return;
        }

        NavigateToPath(_currentPath, syncTree: true);
    }

    private void OpenSearchForm()
    {
        var startPath = !string.IsNullOrWhiteSpace(_currentPath) && Directory.Exists(_currentPath)
            ? _currentPath
            : Directory.Exists(_session.DriveRoot)
                ? _session.DriveRoot
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        if (_searchForm is { IsDisposed: false })
        {
            _searchForm.SetStartPath(startPath);
            _searchForm.Activate();
            _searchForm.WindowState = FormWindowState.Normal;
            return;
        }

        _searchForm = new FileSearchForm(startPath, OpenSearchResult);
        _searchForm.FormClosed += (_, _) => _searchForm = null;
        _searchForm.Show();
    }

    private void OpenSearchResult(string path)
    {
        if (Directory.Exists(path))
        {
            NavigateToPath(path, syncTree: true);
            return;
        }

        if (!File.Exists(path))
        {
            _statusLabel.Text = $"Файл не найден: {path}";
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        _initialSelectPath = path;
        NavigateToPath(directory, syncTree: true);
    }

    private void NavigateToPath(string path, bool syncTree)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            ShowPathError(path, ex);
            return;
        }

        if (!Directory.Exists(fullPath))
        {
            _statusLabel.Text = $"Папка не найдена: {fullPath}";
            return;
        }

        try
        {
            LoadDirectoryContent(fullPath);
            _currentPath = fullPath;
            _pathBox.Text = fullPath;
            _upButton.Enabled = Directory.GetParent(fullPath) is not null;

            if (syncTree)
            {
                SelectTreeNodeForPath(fullPath);
            }

            SelectInitialItemIfNeeded(fullPath);
        }
        catch (Exception ex) when (IsFileSystemAccessException(ex))
        {
            ShowPathError(fullPath, ex);
        }
    }

    private void LoadDirectoryContent(string path)
    {
        _fileList.BeginUpdate();
        _fileList.Items.Clear();

        try
        {
            var directories = Directory.EnumerateDirectories(path)
                .Select(directory => new DirectoryInfo(directory))
                .OrderBy(directory => directory.Name, StringComparer.CurrentCultureIgnoreCase);

            foreach (var directory in directories)
            {
                var item = new ListViewItem(directory.Name);
                item.ImageKey = "folder";
                item.SubItems.Add("Папка");
                item.SubItems.Add(string.Empty);
                item.SubItems.Add(FormatDate(directory.LastWriteTime));
                item.Tag = new FileSystemListItem(directory.FullName, true);
                _fileList.Items.Add(item);
            }

            var files = Directory.EnumerateFiles(path)
                .Select(file => new FileInfo(file))
                .OrderBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase);

            foreach (var file in files)
            {
                var item = new ListViewItem(file.Name);
                item.ImageKey = GetFileImageKey(file.FullName, file.Extension);
                item.SubItems.Add(string.IsNullOrWhiteSpace(file.Extension) ? "Файл" : file.Extension);
                item.SubItems.Add(FormatSize(file.Length));
                item.SubItems.Add(FormatDate(file.LastWriteTime));
                item.Tag = new FileSystemListItem(file.FullName, false);
                _fileList.Items.Add(item);
            }

            _statusLabel.Text = $"Папка: {path}. Элементов: {_fileList.Items.Count}.";
        }
        finally
        {
            _fileList.EndUpdate();
        }
    }

    private void SelectInitialItemIfNeeded(string currentDirectory)
    {
        if (string.IsNullOrWhiteSpace(_initialSelectPath) ||
            !Path.GetDirectoryName(_initialSelectPath)?.Equals(currentDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) == true)
        {
            return;
        }

        foreach (ListViewItem item in _fileList.Items)
        {
            if (item.Tag is not FileSystemListItem fileItem ||
                !PathsEqual(fileItem.Path, _initialSelectPath))
            {
                continue;
            }

            _fileList.SelectedItems.Clear();
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
            _fileList.Focus();
            _initialSelectPath = null;
            return;
        }
    }

    private void LoadChildDirectories(TreeNode node, string path)
    {
        node.Nodes.Clear();

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(path)
                         .Select(directory => new DirectoryInfo(directory))
                         .OrderBy(directory => directory.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var child = new TreeNode(directory.Name)
                {
                    Tag = directory.FullName,
                    ImageKey = "folder",
                    SelectedImageKey = "folder",
                };

                if (CanHaveChildDirectories(directory.FullName))
                {
                    AddLoadingNode(child);
                }

                node.Nodes.Add(child);
            }
        }
        catch (Exception ex) when (IsFileSystemAccessException(ex))
        {
            var denied = new TreeNode($"Нет доступа: {ex.Message}")
            {
                ForeColor = SystemColors.GrayText,
                ImageKey = "warning",
                SelectedImageKey = "warning",
            };
            node.Nodes.Add(denied);
        }
    }

    private void SelectTreeNodeForPath(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var node = FindNodeByPath(_folderTree.Nodes, root);
        if (node is null)
        {
            return;
        }

        var relative = Path.GetRelativePath(root, path);
        if (relative != ".")
        {
            foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                if (string.IsNullOrWhiteSpace(part))
                {
                    continue;
                }

                if (HasLoadingNode(node))
                {
                    LoadChildDirectories(node, Convert.ToString(node.Tag) ?? string.Empty);
                }

                node = FindNodeByName(node.Nodes, part);
                if (node is null)
                {
                    return;
                }
            }
        }

        _syncingTreeSelection = true;
        try
        {
            node.EnsureVisible();
            _folderTree.SelectedNode = node;
        }
        finally
        {
            _syncingTreeSelection = false;
        }
    }

    private static TreeNode? FindNodeByPath(TreeNodeCollection nodes, string path)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Tag is string nodePath && PathsEqual(nodePath, path))
            {
                return node;
            }
        }

        return null;
    }

    private static TreeNode? FindNodeByName(TreeNodeCollection nodes, string name)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Text.Equals(name, StringComparison.CurrentCultureIgnoreCase))
            {
                return node;
            }
        }

        return null;
    }

    private static void AddLoadingNode(TreeNode node)
    {
        node.Nodes.Add(new TreeNode(LoadingNodeText));
    }

    private static bool HasLoadingNode(TreeNode node)
    {
        return node.Nodes.Count == 1 && node.Nodes[0].Text == LoadingNodeText && node.Nodes[0].Tag is null;
    }

    private static bool CanHaveChildDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).Take(1).Any();
        }
        catch
        {
            return false;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private void ShowPathError(string path, Exception ex)
    {
        _statusLabel.Text = $"Не удалось открыть: {path}. {ex.Message}";
    }

    private FileSystemListItem? GetSelectedListItem()
    {
        return _fileList.SelectedItems.Count == 0
            ? null
            : _fileList.SelectedItems[0].Tag as FileSystemListItem;
    }

    private void OpenSelectedItem()
    {
        var item = GetSelectedListItem();
        if (item is null)
        {
            return;
        }

        try
        {
            if (item.IsDirectory)
            {
                NavigateToPath(item.Path, syncTree: true);
                return;
            }

            OpenFile(item.Path);
        }
        catch (Exception ex)
        {
            ShowPathError(item.Path, ex);
        }
    }

    private void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch
        {
            if (IsTextLikeFile(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = QuoteArgument(path),
                    UseShellExecute = true,
                });
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "rundll32.exe",
                Arguments = $"shell32.dll,OpenAs_RunDLL {QuoteArgument(path)}",
                UseShellExecute = true,
            });
        }
    }

    private void CopySelectedPath()
    {
        var item = GetSelectedListItem();
        if (item is null)
        {
            return;
        }

        Clipboard.SetText($"\"{item.Path}\"");
        _statusLabel.Text = "Путь скопирован.";
    }

    private void SetSelectedItemClipboard(bool cut)
    {
        var item = GetSelectedListItem();
        if (item is null)
        {
            return;
        }

        var paths = new StringCollection { item.Path };
        var data = new DataObject();
        data.SetFileDropList(paths);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
        Clipboard.SetDataObject(data, copy: true);
        _statusLabel.Text = cut ? "Элемент помещён в буфер для вырезания." : "Элемент скопирован в буфер.";
    }

    private void CreateSelectedShortcut()
    {
        var item = GetSelectedListItem();
        if (item is null || string.IsNullOrWhiteSpace(_currentPath))
        {
            return;
        }

        try
        {
            var shortcutPath = GetUniquePath(Path.Combine(_currentPath, $"{Path.GetFileName(item.Path)} - ярлык.lnk"));
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("WScript.Shell недоступен.");
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = item.Path;
            shortcut.WorkingDirectory = item.IsDirectory ? item.Path : Path.GetDirectoryName(item.Path) ?? _currentPath;
            shortcut.Save();
            RefreshCurrent();
            _statusLabel.Text = $"Ярлык создан: {shortcutPath}";
        }
        catch (Exception ex)
        {
            ShowPathError(item.Path, ex);
        }
    }

    private void DeleteSelectedItem()
    {
        var item = GetSelectedListItem();
        if (item is null)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Удалить \"{Path.GetFileName(item.Path)}\"?",
            "Удаление",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
        {
            return;
        }

        try
        {
            if (item.IsDirectory)
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    item.Path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            else
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    item.Path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }

            RefreshCurrent();
            _statusLabel.Text = "Элемент удалён в корзину.";
        }
        catch (Exception ex)
        {
            ShowPathError(item.Path, ex);
        }
    }

    private void QuarantineSelectedItem()
    {
        var item = GetSelectedListItem();
        if (item is null || item.IsDirectory)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Добавить файл в карантин?\r\n\r\n{item.Path}",
            "Карантин",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
        {
            return;
        }

        try
        {
            QuarantineManager.QuarantineFile(item.Path, "Добавлено вручную из проводника iUnlocker", "Проводник");
            RefreshCurrent();
            _statusLabel.Text = "Файл добавлен в карантин.";
        }
        catch (Exception ex)
        {
            ShowPathError(item.Path, ex);
        }
    }

    private void RenameSelectedItem()
    {
        var item = GetSelectedListItem();
        if (item is null)
        {
            return;
        }

        var oldName = Path.GetFileName(item.Path);
        var newName = PromptForName("Переименовать", oldName);
        if (string.IsNullOrWhiteSpace(newName) || newName == oldName)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(item.Path)
                ?? throw new InvalidOperationException("Не удалось определить папку элемента.");
            var newPath = Path.Combine(directory, newName);

            if (item.IsDirectory)
            {
                Directory.Move(item.Path, newPath);
            }
            else
            {
                File.Move(item.Path, newPath);
            }

            RefreshCurrent();
            _statusLabel.Text = "Элемент переименован.";
        }
        catch (Exception ex)
        {
            ShowPathError(item.Path, ex);
        }
    }

    private void ShowSelectedProperties()
    {
        var item = GetSelectedListItem();
        if (item is null)
        {
            return;
        }

        try
        {
            ShellPropertyDialog.Show(Handle, item.Path);
        }
        catch (Exception ex)
        {
            ShowPathError(item.Path, ex);
        }
    }

    private string? PromptForName(string title, string currentName)
    {
        using var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(420, 118),
        };

        var textBox = new TextBox
        {
            Text = currentName,
            Location = new Point(12, 12),
            Width = 396,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(252, 72),
            AutoSize = true,
        };

        var cancelButton = new Button
        {
            Text = "Отмена",
            DialogResult = DialogResult.Cancel,
            Location = new Point(332, 72),
            AutoSize = true,
        };

        form.Controls.Add(textBox);
        form.Controls.Add(okButton);
        form.Controls.Add(cancelButton);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;

        textBox.SelectAll();
        return form.ShowDialog(this) == DialogResult.OK ? textBox.Text.Trim() : null;
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool IsFileSystemAccessException(Exception ex)
    {
        return ex is UnauthorizedAccessException or IOException or System.Security.SecurityException;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        var size = (double)bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} {units[unit]}" : $"{size:0.##} {units[unit]}";
    }

    private static string FormatDate(DateTime date)
    {
        return date.ToString("dd.MM.yyyy HH:mm");
    }

    private static bool IsTextLikeFile(string path)
    {
        var extension = Path.GetExtension(path);
        var textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bat",
            ".cmd",
            ".cfg",
            ".conf",
            ".config",
            ".cs",
            ".css",
            ".csv",
            ".htm",
            ".html",
            ".ini",
            ".js",
            ".json",
            ".log",
            ".md",
            ".ps1",
            ".reg",
            ".txt",
            ".xml",
            ".yaml",
            ".yml",
        };

        return textExtensions.Contains(extension);
    }

    private static string QuoteArgument(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private string GetFileImageKey(string path, string extension)
    {
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return GetFileImageKey(extension);
        }

        if (_executableImageKeys.TryGetValue(path, out var existingKey))
        {
            return existingKey;
        }

        var imageKey = $"exe:{path}";
        var icon = ShellIconProvider.GetSmallIconFromFile(path) ?? ShellIconProvider.GetSmallIcon(extension, FileAttributes.Normal);
        _listImages.Images.Add(imageKey, icon ?? SystemIcons.Application);
        _executableImageKeys[path] = imageKey;

        return imageKey;
    }

    private string GetFileImageKey(string extension)
    {
        var normalizedExtension = string.IsNullOrWhiteSpace(extension) ? ".file" : extension;
        if (_extensionImageKeys.TryGetValue(normalizedExtension, out var existingKey))
        {
            return existingKey;
        }

        var imageKey = $"ext:{normalizedExtension}";
        var icon = ShellIconProvider.GetSmallIcon(normalizedExtension, FileAttributes.Normal);
        _listImages.Images.Add(imageKey, icon ?? SystemIcons.Application);
        _extensionImageKeys[normalizedExtension] = imageKey;

        return imageKey;
    }

    private sealed record FileSystemListItem(string Path, bool IsDirectory);

    private sealed class DarkContextMenuRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color Background = Color.FromArgb(37, 37, 37);
        private static readonly Color Selected = Color.FromArgb(62, 62, 62);
        private static readonly Color Separator = Color.FromArgb(64, 64, 64);

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Background);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var color = e.Item.Selected ? Selected : Background;
            using var brush = new SolidBrush(color);
            e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Separator);
            var y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 0, y, e.Item.Width, y);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Background);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }
    }

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
                Directory = System.IO.Directory.Exists(path)
                    ? path
                    : Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
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
            public string Verb;
            public string File;
            public string Parameters;
            public string Directory;
            public int Show;
            public IntPtr InstanceHandle;
            public IntPtr IdList;
            public string Class;
            public IntPtr ClassKey;
            public uint HotKey;
            public IntPtr IconHandle;
            public IntPtr ProcessHandle;
        }
    }

    private static class ShellIconProvider
    {
        private const uint ShgfiIcon = 0x000000100;
        private const uint ShgfiSmallIcon = 0x000000001;
        private const uint ShgfiUseFileAttributes = 0x000000010;

        public static Icon? GetSmallIcon(string pathOrExtension, FileAttributes attributes)
        {
            return GetSmallIcon(pathOrExtension, attributes, useFileAttributes: true);
        }

        public static Icon? GetSmallIconFromFile(string path)
        {
            return GetSmallIcon(path, FileAttributes.Normal, useFileAttributes: false);
        }

        private static Icon? GetSmallIcon(string pathOrExtension, FileAttributes attributes, bool useFileAttributes)
        {
            var info = new ShFileInfo();
            var flags = ShgfiIcon | ShgfiSmallIcon;
            if (useFileAttributes)
            {
                flags |= ShgfiUseFileAttributes;
            }

            var result = SHGetFileInfo(
                pathOrExtension,
                (uint)attributes,
                ref info,
                (uint)Marshal.SizeOf<ShFileInfo>(),
                flags);

            if (result == IntPtr.Zero || info.IconHandle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return (Icon)Icon.FromHandle(info.IconHandle).Clone();
            }
            finally
            {
                DestroyIcon(info.IconHandle);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            ref ShFileInfo psfi,
            uint cbFileInfo,
            uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShFileInfo
        {
            public IntPtr IconHandle;
            public int IconIndex;
            public uint Attributes;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string DisplayName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string TypeName;
        }
    }
}
