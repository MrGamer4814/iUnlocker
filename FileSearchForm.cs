using System.Collections;
using System.Globalization;

namespace IUnlocker;

public sealed class FileSearchForm : Form
{
    private const int MaxResults = 50000;
    private static readonly string[] ResultColumnNames = ["Имя", "Тип", "Размер", "Изменён", "Путь"];

    private readonly Action<string> _openResult;
    private readonly TextBox _startPathBox = new();
    private readonly TextBox _queryBox = new();
    private readonly TextBox _minSizeBox = new();
    private readonly TextBox _maxSizeBox = new();
    private readonly DateTimePicker _dateFromPicker = new();
    private readonly DateTimePicker _dateToPicker = new();
    private readonly Button _settingsButton = new();
    private readonly Button _startButton = new();
    private readonly Button _cancelButton = new();
    private readonly ContextMenuStrip _settingsMenu = new();
    private readonly ToolStripMenuItem _subfoldersMenuItem = new("Искать во вложенных папках") { Checked = true, CheckOnClick = true };
    private readonly ToolStripMenuItem _filesMenuItem = new("Искать файлы") { Checked = true, CheckOnClick = true };
    private readonly ToolStripMenuItem _foldersMenuItem = new("Искать папки") { Checked = false, CheckOnClick = true };
    private readonly ToolStripMenuItem _exactMatchMenuItem = new("Точное совпадение") { Checked = false, CheckOnClick = true };
    private readonly ListView _results = new();
    private readonly Label _statusLabel = new();
    private readonly FileSearchResultComparer _resultComparer = new();

    private CancellationTokenSource? _searchCancellation;
    private int _resultCount;
    private int _sortColumn;
    private SortOrder _sortOrder = SortOrder.Ascending;

    public FileSearchForm(string startPath, Action<string> openResult)
    {
        _openResult = openResult;
        BuildInterface();
        SetStartPath(startPath);
        FormClosing += (_, _) => _searchCancellation?.Cancel();
    }

    public void SetStartPath(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            _startPathBox.Text = path;
        }
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - поиск файлов";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(920, 540);
        ClientSize = new Size(1120, 680);
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
            ColumnCount = 6,
            RowCount = 2,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var folderLabel = new Label
        {
            Text = "Папка:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 8, 0),
        };

        _startPathBox.Dock = DockStyle.Fill;
        _startPathBox.Margin = new Padding(0, 0, 10, 8);
        UiTheme.StyleTextBox(_startPathBox);

        var queryLabel = new Label
        {
            Text = "Искать:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 8, 0),
        };

        _queryBox.Dock = DockStyle.Fill;
        _queryBox.Margin = new Padding(0, 0, 10, 8);
        _queryBox.PlaceholderText = "Имя файла или папки, можно оставить пустым";
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
            _subfoldersMenuItem,
            new ToolStripSeparator(),
            _filesMenuItem,
            _foldersMenuItem,
            new ToolStripSeparator(),
            _exactMatchMenuItem,
        });

        _settingsButton.Text = "Настройки";
        _settingsButton.AutoSize = true;
        _settingsButton.Margin = new Padding(0, 0, 8, 8);
        _settingsButton.Click += (_, _) => _settingsMenu.Show(_settingsButton, new Point(0, _settingsButton.Height));
        UiTheme.StyleButton(_settingsButton);

        _startButton.Text = "Сканировать";
        _startButton.AutoSize = true;
        _startButton.Margin = new Padding(0, 0, 8, 8);
        _startButton.Click += (_, _) => BeginSearch();
        UiTheme.StyleButton(_startButton, primary: true);

        _cancelButton.Text = "Стоп";
        _cancelButton.AutoSize = true;
        _cancelButton.Margin = new Padding(0, 0, 0, 8);
        _cancelButton.Enabled = false;
        _cancelButton.Click += (_, _) => _searchCancellation?.Cancel();
        UiTheme.StyleButton(_cancelButton);

        var filtersPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 0, 10, 0),
        };

        _minSizeBox.Width = 95;
        _minSizeBox.PlaceholderText = "от";
        _minSizeBox.Margin = new Padding(0, 0, 8, 8);
        UiTheme.StyleTextBox(_minSizeBox);

        _maxSizeBox.Width = 95;
        _maxSizeBox.PlaceholderText = "до";
        _maxSizeBox.Margin = new Padding(0, 0, 16, 8);
        UiTheme.StyleTextBox(_maxSizeBox);

        _dateFromPicker.Width = 160;
        _dateFromPicker.Format = DateTimePickerFormat.Custom;
        _dateFromPicker.CustomFormat = "dd.MM.yyyy HH:mm";
        _dateFromPicker.ShowCheckBox = true;
        _dateFromPicker.Checked = false;
        _dateFromPicker.Value = DateTime.Today;
        _dateFromPicker.Margin = new Padding(0, 0, 8, 8);

        _dateToPicker.Width = 160;
        _dateToPicker.Format = DateTimePickerFormat.Custom;
        _dateToPicker.CustomFormat = "dd.MM.yyyy HH:mm";
        _dateToPicker.ShowCheckBox = true;
        _dateToPicker.Checked = false;
        _dateToPicker.Value = DateTime.Today.AddDays(1).AddMinutes(-1);
        _dateToPicker.Margin = new Padding(0, 0, 0, 8);

        filtersPanel.Controls.Add(CreateFilterLabel("Размер:"));
        filtersPanel.Controls.Add(_minSizeBox);
        filtersPanel.Controls.Add(CreateFilterLabel("-"));
        filtersPanel.Controls.Add(_maxSizeBox);
        filtersPanel.Controls.Add(CreateFilterLabel("Дата изменения:"));
        filtersPanel.Controls.Add(_dateFromPicker);
        filtersPanel.Controls.Add(CreateFilterLabel("-"));
        filtersPanel.Controls.Add(_dateToPicker);

        toolbar.Controls.Add(folderLabel, 0, 0);
        toolbar.Controls.Add(_startPathBox, 1, 0);
        toolbar.Controls.Add(queryLabel, 2, 0);
        toolbar.Controls.Add(_queryBox, 3, 0);
        toolbar.Controls.Add(_settingsButton, 4, 0);
        toolbar.Controls.Add(_startButton, 5, 0);
        toolbar.Controls.Add(filtersPanel, 0, 1);
        toolbar.SetColumnSpan(filtersPanel, 5);
        toolbar.Controls.Add(_cancelButton, 5, 1);

        _results.Dock = DockStyle.Fill;
        _results.View = View.Details;
        _results.FullRowSelect = true;
        _results.HideSelection = false;
        _results.MultiSelect = false;
        _results.Columns.Add(ResultColumnNames[0], 260);
        _results.Columns.Add(ResultColumnNames[1], 100);
        _results.Columns.Add(ResultColumnNames[2], 110, HorizontalAlignment.Right);
        _results.Columns.Add(ResultColumnNames[3], 150);
        _results.Columns.Add(ResultColumnNames[4], 560);
        _results.ColumnClick += ResultsColumnClick;
        _results.DoubleClick += (_, _) => OpenSelectedResult();
        UiTheme.StyleListView(_results);

        var menu = new ContextMenuStrip();
        UiTheme.StyleContextMenu(menu);
        menu.Items.Add("Открыть в проводнике iUnlocker", null, (_, _) => OpenSelectedResult());
        menu.Items.Add("Копировать путь", null, (_, _) => CopySelectedPath());
        _results.ContextMenuStrip = menu;

        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = UiTheme.MutedText;
        _statusLabel.Padding = new Padding(0, 10, 0, 0);
        _statusLabel.Text = "Путь берётся из текущей папки проводника, но его можно изменить.";

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_results, 0, 1);
        root.Controls.Add(_statusLabel, 0, 2);
        Controls.Add(root);
        ApplyResultSort();
    }

    private static Label CreateFilterLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 8, 8),
        };
    }

    private async void BeginSearch()
    {
        var startPath = _startPathBox.Text.Trim();
        var query = _queryBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(startPath) || !Directory.Exists(startPath))
        {
            MessageBox.Show(this, "Начальная папка не найдена.", "Поиск файлов", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_filesMenuItem.Checked && !_foldersMenuItem.Checked)
        {
            MessageBox.Show(this, "Выберите поиск файлов или папок.", "Поиск файлов", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!TryGetSizeFilters(out var minSizeBytes, out var maxSizeBytes, out var sizeError))
        {
            MessageBox.Show(this, sizeError, "Поиск файлов", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var modifiedFrom = _dateFromPicker.Checked ? TrimSeconds(_dateFromPicker.Value) : (DateTime?)null;
        var modifiedTo = _dateToPicker.Checked ? TrimSeconds(_dateToPicker.Value).AddSeconds(59) : (DateTime?)null;
        if (modifiedFrom.HasValue && modifiedTo.HasValue && modifiedFrom.Value > modifiedTo.Value)
        {
            MessageBox.Show(this, "Дата \"от\" не может быть позже даты \"до\".", "Поиск файлов", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(query) &&
            !minSizeBytes.HasValue &&
            !maxSizeBytes.HasValue &&
            !modifiedFrom.HasValue &&
            !modifiedTo.HasValue)
        {
            MessageBox.Show(
                this,
                "Укажите имя файла/папки или включите хотя бы один фильтр: размер или дату изменения.",
                "Поиск файлов",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;
        var options = new FileSearchOptions(
            _subfoldersMenuItem.Checked,
            _filesMenuItem.Checked,
            _foldersMenuItem.Checked,
            _exactMatchMenuItem.Checked,
            minSizeBytes,
            maxSizeBytes,
            modifiedFrom,
            modifiedTo);

        _results.Items.Clear();
        _resultCount = 0;
        SetSearchingState(true);
        _statusLabel.Text = "Идёт поиск...";

        var progress = new Progress<IReadOnlyList<FileSearchResult>>(AddResults);
        try
        {
            var summary = await Task.Run(() => Search(startPath, query, options, token, progress), token);
            ApplyResultSort();
            _statusLabel.Text = summary.LimitReached
                ? $"Показано первых {summary.Results} результатов. Ошибок доступа: {summary.Errors}."
                : $"Найдено: {summary.Results}. Ошибок доступа: {summary.Errors}.";
        }
        catch (OperationCanceledException)
        {
            ApplyResultSort();
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

    private static FileSearchSummary Search(
        string startPath,
        string query,
        FileSearchOptions options,
        CancellationToken cancellationToken,
        IProgress<IReadOnlyList<FileSearchResult>> progress)
    {
        var pending = new List<FileSearchResult>();
        var queue = new Queue<string>();
        queue.Enqueue(startPath);
        var resultCount = 0;
        var errorCount = 0;
        var limitReached = false;

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = queue.Dequeue();

            IEnumerable<string> childDirectories = [];
            try
            {
                childDirectories = Directory.EnumerateDirectories(directory).ToList();
            }
            catch
            {
                errorCount++;
            }

            if (options.SearchFolders)
            {
                foreach (var childDirectory in childDirectories)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Matches(Path.GetFileName(childDirectory), query, options.ExactMatch) &&
                        PassesDirectoryFilters(childDirectory, options))
                    {
                        AddResult(CreateDirectoryResult(childDirectory), progress, pending, ref resultCount, ref limitReached);
                        if (limitReached)
                        {
                            break;
                        }
                    }
                }
            }

            if (limitReached)
            {
                break;
            }

            if (options.IncludeSubfolders)
            {
                foreach (var childDirectory in childDirectories)
                {
                    queue.Enqueue(childDirectory);
                }
            }

            if (!options.SearchFiles)
            {
                continue;
            }

            IEnumerable<string> files = [];
            try
            {
                files = Directory.EnumerateFiles(directory).ToList();
            }
            catch
            {
                errorCount++;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Matches(Path.GetFileName(file), query, options.ExactMatch))
                {
                    continue;
                }

                if (!PassesFileFilters(file, options))
                {
                    continue;
                }

                AddResult(CreateFileResult(file), progress, pending, ref resultCount, ref limitReached);
                if (limitReached)
                {
                    break;
                }
            }

            if (limitReached)
            {
                break;
            }
        }

        FlushResults(progress, pending);
        return new FileSearchSummary(resultCount, errorCount, limitReached);
    }

    private static FileSearchResult CreateDirectoryResult(string path)
    {
        try
        {
            var directory = new DirectoryInfo(path);
            return new FileSearchResult(directory.Name, "Папка", string.Empty, null, FormatDate(directory.LastWriteTime), directory.LastWriteTime, directory.FullName);
        }
        catch
        {
            return new FileSearchResult(Path.GetFileName(path), "Папка", string.Empty, null, string.Empty, null, path);
        }
    }

    private static FileSearchResult CreateFileResult(string path)
    {
        try
        {
            var file = new FileInfo(path);
            var type = string.IsNullOrWhiteSpace(file.Extension) ? "Файл" : file.Extension;
            return new FileSearchResult(file.Name, type, FormatSize(file.Length), file.Length, FormatDate(file.LastWriteTime), file.LastWriteTime, file.FullName);
        }
        catch
        {
            return new FileSearchResult(Path.GetFileName(path), "Файл", string.Empty, null, string.Empty, null, path);
        }
    }

    private static void AddResult(
        FileSearchResult result,
        IProgress<IReadOnlyList<FileSearchResult>> progress,
        List<FileSearchResult> pending,
        ref int resultCount,
        ref bool limitReached)
    {
        if (resultCount >= MaxResults)
        {
            limitReached = true;
            return;
        }

        pending.Add(result);
        resultCount++;
        FlushResults(progress, pending);
    }

    private static void FlushResults(IProgress<IReadOnlyList<FileSearchResult>> progress, List<FileSearchResult> pending)
    {
        if (pending.Count == 0)
        {
            return;
        }

        progress.Report(pending.ToArray());
        pending.Clear();
    }

    private void AddResults(IReadOnlyList<FileSearchResult> results)
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
                var item = new ListViewItem(result.Name);
                item.SubItems.Add(result.Type);
                item.SubItems.Add(result.Size);
                item.SubItems.Add(result.Modified);
                item.SubItems.Add(result.Path);
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

    private void ResultsColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (_sortColumn == e.Column)
        {
            _sortOrder = _sortOrder == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
        }
        else
        {
            _sortColumn = e.Column;
            _sortOrder = SortOrder.Ascending;
        }

        ApplyResultSort();
    }

    private void ApplyResultSort()
    {
        _resultComparer.SetSort(_sortColumn, _sortOrder);
        _results.ListViewItemSorter = _resultComparer;
        _results.Sort();
        UpdateColumnHeaders();
    }

    private void UpdateColumnHeaders()
    {
        for (var index = 0; index < _results.Columns.Count && index < ResultColumnNames.Length; index++)
        {
            var suffix = index == _sortColumn
                ? _sortOrder == SortOrder.Ascending ? " ^" : " v"
                : string.Empty;
            _results.Columns[index].Text = ResultColumnNames[index] + suffix;
        }
    }

    private static bool Matches(string text, string query, bool exactMatch)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return exactMatch
            ? text.Equals(query, StringComparison.CurrentCultureIgnoreCase)
            : text.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool PassesDirectoryFilters(string path, FileSearchOptions options)
    {
        if (options.MinSizeBytes.HasValue || options.MaxSizeBytes.HasValue)
        {
            return false;
        }

        if (!options.ModifiedFrom.HasValue && !options.ModifiedTo.HasValue)
        {
            return true;
        }

        try
        {
            var modified = Directory.GetLastWriteTime(path);
            return PassesDateFilter(modified, options);
        }
        catch
        {
            return false;
        }
    }

    private static bool PassesFileFilters(string path, FileSearchOptions options)
    {
        try
        {
            var file = new FileInfo(path);
            if (options.MinSizeBytes.HasValue && file.Length < options.MinSizeBytes.Value)
            {
                return false;
            }

            if (options.MaxSizeBytes.HasValue && file.Length > options.MaxSizeBytes.Value)
            {
                return false;
            }

            return PassesDateFilter(file.LastWriteTime, options);
        }
        catch
        {
            return false;
        }
    }

    private static bool PassesDateFilter(DateTime modified, FileSearchOptions options)
    {
        if (options.ModifiedFrom.HasValue && modified < options.ModifiedFrom.Value)
        {
            return false;
        }

        if (options.ModifiedTo.HasValue && modified > options.ModifiedTo.Value)
        {
            return false;
        }

        return true;
    }

    private void SetSearchingState(bool searching)
    {
        _startButton.Enabled = !searching;
        _cancelButton.Enabled = searching;
        _settingsButton.Enabled = !searching;
        _startPathBox.Enabled = !searching;
        _queryBox.Enabled = !searching;
        _minSizeBox.Enabled = !searching;
        _maxSizeBox.Enabled = !searching;
        _dateFromPicker.Enabled = !searching;
        _dateToPicker.Enabled = !searching;
        _subfoldersMenuItem.Enabled = !searching;
        _filesMenuItem.Enabled = !searching;
        _foldersMenuItem.Enabled = !searching;
        _exactMatchMenuItem.Enabled = !searching;
    }

    private FileSearchResult? GetSelectedResult()
    {
        return _results.SelectedItems.Count == 0
            ? null
            : _results.SelectedItems[0].Tag as FileSearchResult;
    }

    private void OpenSelectedResult()
    {
        var result = GetSelectedResult();
        if (result is null)
        {
            return;
        }

        _openResult(result.Path);
    }

    private void CopySelectedPath()
    {
        var result = GetSelectedResult();
        if (result is null)
        {
            return;
        }

        Clipboard.SetText(result.Path);
        _statusLabel.Text = "Путь скопирован.";
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

    private static DateTime TrimSeconds(DateTime value)
    {
        return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Kind);
    }

    private bool TryGetSizeFilters(out long? minSizeBytes, out long? maxSizeBytes, out string message)
    {
        minSizeBytes = null;
        maxSizeBytes = null;
        message = string.Empty;

        if (!TryParseSize(_minSizeBox.Text, out minSizeBytes, out message))
        {
            message = $"Размер \"от\": {message}";
            return false;
        }

        if (!TryParseSize(_maxSizeBox.Text, out maxSizeBytes, out message))
        {
            message = $"Размер \"до\": {message}";
            return false;
        }

        if (minSizeBytes.HasValue && maxSizeBytes.HasValue && minSizeBytes.Value > maxSizeBytes.Value)
        {
            message = "Размер \"от\" не может быть больше размера \"до\".";
            return false;
        }

        return true;
    }

    private static bool TryParseSize(string text, out long? bytes, out string message)
    {
        bytes = null;
        message = string.Empty;
        text = text.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var numberPart = new string(text.TakeWhile(ch => char.IsDigit(ch) || ch == '.' || ch == ',').ToArray());
        var unitPart = text[numberPart.Length..].Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(numberPart) ||
            !double.TryParse(numberPart.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ||
            value < 0)
        {
            message = "укажите число, например 10 MB.";
            return false;
        }

        var multiplier = unitPart switch
        {
            "" => 1024d * 1024d,
            "b" or "byte" or "bytes" or "б" => 1d,
            "kb" or "k" or "кб" => 1024d,
            "mb" or "m" or "мб" => 1024d * 1024d,
            "gb" or "g" or "гб" => 1024d * 1024d * 1024d,
            "tb" or "t" or "тб" => 1024d * 1024d * 1024d * 1024d,
            _ => -1d,
        };

        if (multiplier < 0)
        {
            message = "поддерживаются B, KB, MB, GB, TB.";
            return false;
        }

        var result = value * multiplier;
        if (result > long.MaxValue)
        {
            message = "значение слишком большое.";
            return false;
        }

        bytes = (long)Math.Round(result);
        return true;
    }

    private sealed record FileSearchResult(
        string Name,
        string Type,
        string Size,
        long? SizeBytes,
        string Modified,
        DateTime? ModifiedAt,
        string Path);

    private sealed record FileSearchOptions(
        bool IncludeSubfolders,
        bool SearchFiles,
        bool SearchFolders,
        bool ExactMatch,
        long? MinSizeBytes,
        long? MaxSizeBytes,
        DateTime? ModifiedFrom,
        DateTime? ModifiedTo);

    private sealed record FileSearchSummary(int Results, int Errors, bool LimitReached);

    private sealed class FileSearchResultComparer : IComparer
    {
        private int _column;
        private SortOrder _order = SortOrder.Ascending;

        public void SetSort(int column, SortOrder order)
        {
            _column = column;
            _order = order;
        }

        public int Compare(object? x, object? y)
        {
            var left = (x as ListViewItem)?.Tag as FileSearchResult;
            var right = (y as ListViewItem)?.Tag as FileSearchResult;
            if (left is null || right is null)
            {
                return 0;
            }

            var result = _column switch
            {
                0 => string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase),
                1 => string.Compare(left.Type, right.Type, StringComparison.CurrentCultureIgnoreCase),
                2 => CompareNullable(left.SizeBytes, right.SizeBytes),
                3 => CompareNullable(left.ModifiedAt, right.ModifiedAt),
                4 => string.Compare(left.Path, right.Path, StringComparison.CurrentCultureIgnoreCase),
                _ => 0,
            };

            if (result == 0 && _column != 0)
            {
                result = string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
            }

            return _order == SortOrder.Descending ? -result : result;
        }

        private static int CompareNullable<T>(T? left, T? right)
            where T : struct, IComparable<T>
        {
            if (left.HasValue && right.HasValue)
            {
                return left.Value.CompareTo(right.Value);
            }

            if (left.HasValue)
            {
                return 1;
            }

            return right.HasValue ? -1 : 0;
        }
    }
}
