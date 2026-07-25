namespace IUnlocker;

public sealed class BootRepairForm : Form
{
    private readonly AppSession _session;
    private readonly ComboBox _efiDriveBox = new();
    private readonly ComboBox _firmwareBox = new();
    private readonly TextBox _commandBox = new();
    private readonly Button _refreshButton = new();
    private readonly Button _runButton = new();
    private readonly Label _statusLabel = new();

    public BootRepairForm(AppSession session)
    {
        _session = session;
        BuildInterface();
        Shown += (_, _) => RefreshDrives();
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - восстановление загрузчика";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 360);
        ClientSize = new Size(860, 430);
        UiTheme.ApplyForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(18),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Восстановление загрузочных файлов",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        };
        UiTheme.StyleTitle(title, 16F);

        var warning = new Label
        {
            Text = "Функция запускает bcdboot для выбранной Windows. Используйте её только если понимаете, какой EFI-раздел выбран.",
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = Color.FromArgb(150, 70, 0),
            Margin = new Padding(0, 0, 0, 12),
        };

        var options = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            RowCount = 2,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var efiLabel = new Label { Text = "EFI-раздел:", AutoSize = true, Margin = new Padding(0, 7, 8, 8) };
        _efiDriveBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _efiDriveBox.Dock = DockStyle.Fill;
        _efiDriveBox.SelectedIndexChanged += (_, _) => UpdateCommandPreview();

        _firmwareBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _firmwareBox.Items.AddRange(["UEFI", "BIOS", "ALL"]);
        _firmwareBox.SelectedIndex = 0;
        _firmwareBox.Width = 100;
        _firmwareBox.Margin = new Padding(8, 0, 8, 8);
        _firmwareBox.SelectedIndexChanged += (_, _) => UpdateCommandPreview();

        _refreshButton.Text = "Обновить";
        _refreshButton.AutoSize = true;
        _refreshButton.Margin = new Padding(0, 0, 0, 8);
        _refreshButton.Click += (_, _) => RefreshDrives();
        UiTheme.StyleButton(_refreshButton);

        options.Controls.Add(efiLabel, 0, 0);
        options.Controls.Add(_efiDriveBox, 1, 0);
        options.Controls.Add(_firmwareBox, 2, 0);
        options.Controls.Add(_refreshButton, 3, 0);

        _commandBox.Dock = DockStyle.Fill;
        _commandBox.Multiline = true;
        _commandBox.ReadOnly = true;
        _commandBox.ScrollBars = ScrollBars.Vertical;
        _commandBox.Height = 120;
        UiTheme.StyleTextBox(_commandBox);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
        };
        _runButton.Text = "Запустить восстановление";
        _runButton.AutoSize = true;
        _runButton.Click += (_, _) => RunRepair();
        UiTheme.StyleButton(_runButton, primary: true);
        buttons.Controls.Add(_runButton);

        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = UiTheme.MutedText;
        _statusLabel.Padding = new Padding(0, 10, 0, 0);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(warning, 0, 1);
        root.Controls.Add(options, 0, 2);
        root.Controls.Add(_commandBox, 0, 3);
        root.Controls.Add(buttons, 0, 4);
        root.Controls.Add(_statusLabel, 0, 5);
        Controls.Add(root);
    }

    private void RefreshDrives()
    {
        _efiDriveBox.Items.Clear();
        foreach (var candidate in GetEfiCandidates())
        {
            _efiDriveBox.Items.Add(candidate);
        }

        if (_efiDriveBox.Items.Count > 0)
        {
            _efiDriveBox.SelectedIndex = 0;
        }

        _runButton.Enabled = _efiDriveBox.Items.Count > 0 &&
                             !string.IsNullOrWhiteSpace(_session.WindowsPath) &&
                             Directory.Exists(_session.WindowsPath);
        _statusLabel.Text = _efiDriveBox.Items.Count == 0
            ? "EFI-раздел не найден. Назначьте букву EFI-разделу и нажмите обновить."
            : $"Найдено разделов: {_efiDriveBox.Items.Count}.";
        UpdateCommandPreview();
    }

    private IEnumerable<EfiDriveCandidate> GetEfiCandidates()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.RootDirectory.FullName.StartsWith("X:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string format;
            try
            {
                format = drive.DriveFormat;
            }
            catch
            {
                format = string.Empty;
            }

            var root = drive.RootDirectory.FullName;
            var hasEfiFolder = Directory.Exists(Path.Combine(root, "EFI"));
            var likely = format.Equals("FAT32", StringComparison.OrdinalIgnoreCase) || hasEfiFolder;
            if (likely)
            {
                yield return new EfiDriveCandidate(root, $"{root}  {format}  {(hasEfiFolder ? "EFI" : "")}".Trim());
            }
        }
    }

    private void UpdateCommandPreview()
    {
        _commandBox.Text = GetSelectedCandidate() is not { } candidate || string.IsNullOrWhiteSpace(_session.WindowsPath)
            ? string.Empty
            : $"iUnlocker создаст backup BCD перед запуском.{Environment.NewLine}bcdboot.exe {BuildArguments(candidate.Root)}";
    }

    private void RunRepair()
    {
        if (GetSelectedCandidate() is not { } candidate || string.IsNullOrWhiteSpace(_session.WindowsPath))
        {
            return;
        }

        var confirmation =
            $"Запустить восстановление загрузчика?\r\n\r\nWindows: {_session.WindowsPath}\r\nEFI: {candidate.Root}\r\n\r\n" +
            "Перед запуском будет создан backup BCD, если файл найден.";
        if (MessageBox.Show(this, confirmation, "Восстановление загрузчика", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            BackupBcd(candidate.Root);
            SystemCommandRunner.Show(this, "iUnlocker: восстановление загрузчика", new SystemCommand("bcdboot.exe", BuildArguments(candidate.Root)));
            _statusLabel.Text = "Восстановление загрузчика запущено в окне iUnlocker.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Восстановление загрузчика", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private string BuildArguments(string efiRoot)
    {
        var firmware = Convert.ToString(_firmwareBox.SelectedItem) ?? "UEFI";
        return $@"""{_session.WindowsPath}"" /s {efiRoot.TrimEnd('\\')} /f {firmware}";
    }

    private void BackupBcd(string efiRoot)
    {
        foreach (var bcd in new[]
                 {
                     Path.Combine(efiRoot, "EFI", "Microsoft", "Boot", "BCD"),
                     Path.Combine(efiRoot, "Microsoft", "Boot", "BCD"),
                     Path.Combine(efiRoot, "Boot", "BCD"),
                 })
        {
            if (!File.Exists(bcd))
            {
                continue;
            }

            var backup = $"{bcd}.iUnlocker.{DateTime.Now:yyyyMMddHHmmss}.bak";
            File.Copy(bcd, backup, overwrite: false);
        }
    }

    private EfiDriveCandidate? GetSelectedCandidate()
    {
        return _efiDriveBox.SelectedItem as EfiDriveCandidate;
    }

    private sealed record EfiDriveCandidate(string Root, string Display)
    {
        public override string ToString() => Display;
    }
}
