namespace IUnlocker;

public sealed class TextEditForm : Form
{
    private readonly TextBox _textBox = new();

    public TextEditForm(string title, string label, string text)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowIcon = false;
        Width = 720;
        Height = 260;
        MinimumSize = new Size(520, 220);
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

        var labelControl = new Label
        {
            Text = label,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        };

        _textBox.Dock = DockStyle.Fill;
        _textBox.Multiline = true;
        _textBox.ScrollBars = ScrollBars.Both;
        _textBox.AcceptsReturn = true;
        _textBox.AcceptsTab = true;
        _textBox.WordWrap = false;
        _textBox.Text = text;
        UiTheme.StyleTextBox(_textBox);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0),
        };

        var okButton = new Button { Text = "Сохранить", DialogResult = DialogResult.OK, AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        var cancelButton = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true };
        UiTheme.StyleButton(okButton, primary: true);
        UiTheme.StyleButton(cancelButton);
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        root.Controls.Add(labelControl, 0, 0);
        root.Controls.Add(_textBox, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
    }

    public string EditedText => _textBox.Text.Trim();
}
