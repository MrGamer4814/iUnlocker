namespace IUnlocker;

public sealed class MainMenuForm : Form
{
    private readonly AppSession _session;

    private Form1? _startupForm;
    private FileExplorerForm? _fileExplorerForm;
    private RegistryEditorForm? _registryEditorForm;
    private SamSystemToolsForm? _samSystemToolsForm;
    private TaskManagerForm? _taskManagerForm;
    private RestrictionsUnlockForm? _restrictionsUnlockForm;
    private SuspiciousScanForm? _suspiciousScanForm;
    private QuarantineForm? _quarantineForm;
    private AdditionalFunctionsForm? _additionalFunctionsForm;
    private SettingsForm? _settingsForm;
    private bool _checkingUpdates;
    private bool _autoUpdateChecked;

    public MainMenuForm(AppSession session)
    {
        _session = session;
        BuildInterface();
        Shown += async (_, _) => await CheckUpdatesOnceAsync();
    }

    private void BuildInterface()
    {
        Text = "iUnlocker";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(700, 580);
        ClientSize = new Size(780, 660);
        UiTheme.ApplyForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(32),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "iUnlocker",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        };
        UiTheme.StyleTitle(title, 30F);

        var subtitle = new Label
        {
            Text = "Главное меню",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 20),
        };
        UiTheme.StyleSubtitle(subtitle);

        var systemInfo = new Label
        {
            Text = GetSystemInfoText(),
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 74,
            Margin = new Padding(0, 0, 0, 24),
        };
        UiTheme.StyleInfo(systemInfo);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 408,
            ColumnCount = 2,
            RowCount = 6,
            Margin = new Padding(0),
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 16.666F));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 16.666F));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 16.666F));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 16.666F));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 16.666F));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 16.666F));

        var startupButton = CreateMenuButton("Автозагрузка");
        startupButton.Click += (_, _) => OpenStartupForm();

        var fileExplorerButton = CreateMenuButton("Проводник");
        fileExplorerButton.Click += (_, _) => OpenFileExplorerForm();

        var registryEditorButton = CreateMenuButton("Редактор реестра");
        registryEditorButton.Click += (_, _) => OpenRegistryEditorForm();

        var samSystemButton = CreateMenuButton("SAM/SYSTEM");
        samSystemButton.Click += (_, _) => OpenSamSystemToolsForm();

        var taskManagerButton = CreateMenuButton("Диспетчер задач");
        taskManagerButton.Click += (_, _) => OpenTaskManagerForm();

        var restrictionsButton = CreateMenuButton("Разблокировка ограничений");
        restrictionsButton.Click += (_, _) => OpenRestrictionsUnlockForm();

        var suspiciousScanButton = CreateMenuButton("Скан подозрительного");
        suspiciousScanButton.Click += (_, _) => OpenSuspiciousScanForm();

        var quarantineButton = CreateMenuButton("Карантин");
        quarantineButton.Click += (_, _) => OpenQuarantineForm();

        var additionalFunctionsButton = CreateMenuButton("Дополнительные функции");
        additionalFunctionsButton.Click += (_, _) => OpenAdditionalFunctionsForm();

        var settingsButton = CreateMenuButton("Настройки");
        settingsButton.Click += (_, _) => OpenSettingsForm();

        buttons.Controls.Add(startupButton, 0, 0);
        buttons.Controls.Add(fileExplorerButton, 1, 0);
        buttons.Controls.Add(registryEditorButton, 0, 1);
        buttons.Controls.Add(samSystemButton, 1, 1);
        buttons.Controls.Add(taskManagerButton, 0, 2);
        buttons.Controls.Add(restrictionsButton, 1, 2);
        buttons.Controls.Add(suspiciousScanButton, 0, 3);
        buttons.Controls.Add(quarantineButton, 1, 3);
        buttons.Controls.Add(additionalFunctionsButton, 0, 4);
        buttons.SetColumnSpan(additionalFunctionsButton, 2);
        buttons.Controls.Add(settingsButton, 0, 5);
        buttons.SetColumnSpan(settingsButton, 2);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(subtitle, 0, 1);
        root.Controls.Add(systemInfo, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);
    }

    private Button CreateMenuButton(string text)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 14, 14),
            Font = AppFonts.Create(11F),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        UiTheme.StyleButton(button);
        return button;
    }

    private string GetSystemInfoText()
    {
        var windowsText = _session.WindowsPath is null
            ? "Windows: не найдена на выбранном диске"
            : $"Windows: {_session.WindowsPath}";

        return $"Среда: {_session.EnvironmentName}\r\nДиск: {_session.DriveRoot}\r\n{windowsText}";
    }

    private void OpenStartupForm()
    {
        if (_startupForm is { IsDisposed: false })
        {
            _startupForm.Activate();
            _startupForm.WindowState = FormWindowState.Normal;
            return;
        }

        _startupForm = new Form1(_session);
        _startupForm.FormClosed += (_, _) =>
        {
            _startupForm = null;
            ShowMainMenuAgain();
        };
        Hide();
        _startupForm.Show(this);
    }

    private void OpenFileExplorerForm()
    {
        if (_fileExplorerForm is { IsDisposed: false })
        {
            _fileExplorerForm.Activate();
            _fileExplorerForm.WindowState = FormWindowState.Normal;
            return;
        }

        _fileExplorerForm = new FileExplorerForm(_session);
        _fileExplorerForm.FormClosed += (_, _) =>
        {
            _fileExplorerForm = null;
            ShowMainMenuAgain();
        };
        Hide();
        _fileExplorerForm.Show(this);
    }

    private void OpenRegistryEditorForm()
    {
        if (_registryEditorForm is { IsDisposed: false })
        {
            _registryEditorForm.Activate();
            _registryEditorForm.WindowState = FormWindowState.Normal;
            return;
        }

        _registryEditorForm = new RegistryEditorForm(_session);
        _registryEditorForm.FormClosed += (_, _) =>
        {
            _registryEditorForm = null;
            ShowMainMenuAgain();
        };
        Hide();
        _registryEditorForm.Show(this);
    }

    private void OpenSamSystemToolsForm()
    {
        if (_samSystemToolsForm is { IsDisposed: false })
        {
            _samSystemToolsForm.Activate();
            _samSystemToolsForm.WindowState = FormWindowState.Normal;
            return;
        }

        _samSystemToolsForm = new SamSystemToolsForm(_session);
        _samSystemToolsForm.FormClosed += (_, _) =>
        {
            _samSystemToolsForm = null;
            ShowMainMenuAgain();
        };
        Hide();
        _samSystemToolsForm.Show(this);
    }

    private void OpenTaskManagerForm()
    {
        if (_taskManagerForm is { IsDisposed: false })
        {
            _taskManagerForm.Activate();
            _taskManagerForm.WindowState = FormWindowState.Normal;
            return;
        }

        try
        {
            _taskManagerForm = new TaskManagerForm(_session);
            _taskManagerForm.FormClosed += (_, _) =>
            {
                _taskManagerForm = null;
                ShowMainMenuAgain();
            };
            _taskManagerForm.Show();
            Hide();
        }
        catch (Exception ex)
        {
            _taskManagerForm = null;
            ShowMainMenuAgain();
            MessageBox.Show(
                this,
                ex.Message,
                "Не удалось открыть диспетчер задач",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void OpenRestrictionsUnlockForm()
    {
        if (_restrictionsUnlockForm is { IsDisposed: false })
        {
            _restrictionsUnlockForm.Activate();
            _restrictionsUnlockForm.WindowState = FormWindowState.Normal;
            return;
        }

        _restrictionsUnlockForm = new RestrictionsUnlockForm(_session);
        _restrictionsUnlockForm.FormClosed += (_, _) =>
        {
            _restrictionsUnlockForm = null;
            ShowMainMenuAgain();
        };
        Hide();
        _restrictionsUnlockForm.Show(this);
    }

    private void OpenAdditionalFunctionsForm()
    {
        if (_additionalFunctionsForm is { IsDisposed: false })
        {
            _additionalFunctionsForm.Activate();
            _additionalFunctionsForm.WindowState = FormWindowState.Normal;
            return;
        }

        _additionalFunctionsForm = new AdditionalFunctionsForm(_session);
        _additionalFunctionsForm.FormClosed += (_, _) =>
        {
            _additionalFunctionsForm = null;
            ShowMainMenuAgain();
        };
        Hide();
        _additionalFunctionsForm.Show(this);
    }

    private void OpenSuspiciousScanForm()
    {
        if (_suspiciousScanForm is { IsDisposed: false })
        {
            _suspiciousScanForm.Activate();
            _suspiciousScanForm.WindowState = FormWindowState.Normal;
            return;
        }

        _suspiciousScanForm = new SuspiciousScanForm(_session);
        _suspiciousScanForm.FormClosed += (_, _) =>
        {
            _suspiciousScanForm = null;
            ShowMainMenuAgain();
        };
        Hide();
        _suspiciousScanForm.Show(this);
    }

    private void OpenQuarantineForm()
    {
        if (_quarantineForm is { IsDisposed: false })
        {
            _quarantineForm.Activate();
            _quarantineForm.WindowState = FormWindowState.Normal;
            return;
        }

        _quarantineForm = new QuarantineForm(_session);
        _quarantineForm.FormClosed += (_, _) =>
        {
            _quarantineForm = null;
            ShowMainMenuAgain();
        };
        Hide();
        _quarantineForm.Show(this);
    }

    private void OpenSettingsForm()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            _settingsForm.WindowState = FormWindowState.Normal;
            return;
        }

        _settingsForm = new SettingsForm(owner => CheckForUpdatesAsync(showNoUpdate: true, owner));
        _settingsForm.FormClosed += (_, _) =>
        {
            _settingsForm = null;
            ShowMainMenuAgain();
        };
        Hide();
        _settingsForm.Show(this);
    }

    private void ShowMainMenuAgain()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private async Task CheckUpdatesOnceAsync()
    {
        if (_autoUpdateChecked)
        {
            return;
        }

        _autoUpdateChecked = true;
        await CheckForUpdatesAsync(showNoUpdate: false, owner: this);
    }

    private async Task CheckForUpdatesAsync(bool showNoUpdate, IWin32Window? owner = null)
    {
        if (_checkingUpdates)
        {
            return;
        }

        _checkingUpdates = true;
        var dialogOwner = owner ?? this;
        try
        {
            var update = await GitHubUpdater.CheckAsync(CancellationToken.None);
            if (update is null)
            {
                if (showNoUpdate)
                {
                    MessageBox.Show(
                        dialogOwner,
                        $"Установлена актуальная версия: {GitHubUpdater.CurrentVersion}.",
                        "Обновления iUnlocker",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            var message =
                $"Доступна новая версия iUnlocker: {update.TagName}\r\n" +
                $"Текущая версия: {GitHubUpdater.CurrentVersion}\r\n\r\n" +
                "Скачать и установить обновление?";

            if (MessageBox.Show(dialogOwner, message, "Обновления iUnlocker", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
            {
                return;
            }

            using var downloadForm = new UpdateDownloadForm(update);
            if (downloadForm.ShowDialog(dialogOwner) != DialogResult.OK || string.IsNullOrWhiteSpace(downloadForm.DownloadedFilePath))
            {
                return;
            }

            GitHubUpdater.StartSelfReplace(downloadForm.DownloadedFilePath);
            Application.Exit();
        }
        catch (Exception ex)
        {
            if (showNoUpdate)
            {
                MessageBox.Show(
                    dialogOwner,
                    ex.Message,
                    "Не удалось проверить обновления",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _checkingUpdates = false;
        }
    }
}
