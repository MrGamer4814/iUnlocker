namespace IUnlocker;

public sealed class UpdateDownloadForm : Form
{
    private readonly GitHubUpdateInfo _update;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Label _statusLabel = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Button _cancelButton = new();

    public string? DownloadedFilePath { get; private set; }

    public UpdateDownloadForm(GitHubUpdateInfo update)
    {
        _update = update;
        BuildInterface();
        Shown += async (_, _) => await DownloadUpdateAsync();
        FormClosing += (_, _) => _cancellation.Cancel();
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - обновление";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(460, 150);
        UiTheme.ApplyForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _statusLabel.AutoSize = false;
        _statusLabel.Dock = DockStyle.Top;
        _statusLabel.Height = 44;
        _statusLabel.Text = $"Скачивание {_update.AssetName}...";

        _progressBar.Dock = DockStyle.Top;
        _progressBar.Height = 22;
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 100;
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Margin = new Padding(0, 0, 0, 16);

        _cancelButton.Text = "Отмена";
        _cancelButton.AutoSize = true;
        _cancelButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        _cancelButton.Click += (_, _) => _cancellation.Cancel();
        UiTheme.StyleButton(_cancelButton);

        root.Controls.Add(_statusLabel, 0, 0);
        root.Controls.Add(_progressBar, 0, 1);
        root.Controls.Add(_cancelButton, 0, 2);
        Controls.Add(root);
    }

    private async Task DownloadUpdateAsync()
    {
        try
        {
            var progress = new Progress<int>(value =>
            {
                _progressBar.Value = Math.Clamp(value, 0, 100);
                _statusLabel.Text = $"Скачивание {_update.AssetName}... {value}%";
            });

            DownloadedFilePath = await GitHubUpdater.DownloadAsync(_update, progress, _cancellation.Token);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Не удалось скачать обновление", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.Abort;
            Close();
        }
    }
}
