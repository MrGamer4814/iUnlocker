namespace IUnlocker;

public sealed class SettingsForm : Form
{
    private readonly Func<IWin32Window, Task> _checkUpdates;
    private readonly Button _checkUpdatesButton = new();
    private readonly Label _statusLabel = new();

    public SettingsForm(Func<IWin32Window, Task> checkUpdates)
    {
        _checkUpdates = checkUpdates;
        BuildInterface();
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - настройки";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(520, 260);
        ClientSize = new Size(600, 320);
        UiTheme.ApplyForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(24),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "Настройки",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6),
        };
        UiTheme.StyleTitle(title, 22F);

        var versionLabel = new Label
        {
            Text = $"Версия: {GitHubUpdater.CurrentVersion}",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 20),
        };
        UiTheme.StyleSubtitle(versionLabel);

        _checkUpdatesButton.Text = "Проверить обновление";
        _checkUpdatesButton.Width = 230;
        _checkUpdatesButton.Height = 42;
        _checkUpdatesButton.Margin = new Padding(0, 0, 0, 14);
        _checkUpdatesButton.Click += async (_, _) => await CheckUpdatesAsync();
        UiTheme.StyleButton(_checkUpdatesButton, primary: true);

        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = UiTheme.MutedText;
        _statusLabel.Text = "Автопроверка обновлений включена при запуске.";

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(versionLabel, 0, 1);
        root.Controls.Add(_checkUpdatesButton, 0, 2);
        root.Controls.Add(_statusLabel, 0, 3);
        Controls.Add(root);
    }

    private async Task CheckUpdatesAsync()
    {
        _checkUpdatesButton.Enabled = false;
        _statusLabel.Text = "Проверка обновлений...";
        try
        {
            await _checkUpdates(this);
            _statusLabel.Text = "Проверка завершена.";
        }
        finally
        {
            if (!IsDisposed)
            {
                _checkUpdatesButton.Enabled = true;
            }
        }
    }
}
