namespace IUnlocker;

public sealed class ProcessOutputForm : Form
{
    private readonly TextBox _outputBox = new();
    private readonly Button _closeButton = new();

    public ProcessOutputForm(string title, string commandLine)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 460);
        ClientSize = new Size(920, 560);
        UiTheme.ApplyForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _outputBox.Dock = DockStyle.Fill;
        _outputBox.Multiline = true;
        _outputBox.ReadOnly = true;
        _outputBox.ScrollBars = ScrollBars.Both;
        _outputBox.WordWrap = false;
        _outputBox.BackColor = Color.FromArgb(16, 18, 24);
        _outputBox.ForeColor = Color.FromArgb(229, 231, 235);
        _outputBox.BorderStyle = BorderStyle.FixedSingle;
        _outputBox.Font = new Font("Consolas", 10F, FontStyle.Regular);

        _closeButton.Text = "Закрыть";
        _closeButton.AutoSize = true;
        _closeButton.Anchor = AnchorStyles.Right;
        _closeButton.Margin = new Padding(0, 10, 0, 0);
        _closeButton.Click += (_, _) => Close();
        UiTheme.StyleButton(_closeButton);

        root.Controls.Add(_outputBox, 0, 0);
        root.Controls.Add(_closeButton, 0, 1);
        Controls.Add(root);

        AppendLine(commandLine);
        AppendLine(new string('-', 80));
    }

    public void AppendLine(string text)
    {
        AppendText(text + Environment.NewLine);
    }

    public void AppendText(string text)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action<string>(AppendText), text);
            }
            catch
            {
                // The window may be closing while process output is still arriving.
            }

            return;
        }

        _outputBox.AppendText(text);
        _outputBox.SelectionStart = _outputBox.TextLength;
        _outputBox.ScrollToCaret();
    }

    public void MarkFinished(int exitCode)
    {
        AppendLine(new string('-', 80));
        AppendLine($"Процесс завершён. Код выхода: {exitCode}");
    }
}
