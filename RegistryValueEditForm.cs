using Microsoft.Win32;

namespace IUnlocker;

public sealed class RegistryValueEditForm : Form
{
    private readonly TextBox _valueBox = new();

    public string EditedText => _valueBox.Text;

    public RegistryValueEditForm(StartupEntry entry)
    {
        Text = "Изменить значение реестра";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowIcon = false;
        Width = 720;
        Height = 430;
        MinimumSize = new Size(560, 320);
        UiTheme.ApplyForm(this);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var pathLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(660, 0),
            Text = GetRegistryPathText(entry),
            Margin = new Padding(0, 0, 0, 6),
        };

        var nameLabel = new Label
        {
            AutoSize = true,
            Text = $"Значение: {DisplayValueName(entry.RegistryValueName)}",
            Margin = new Padding(0, 0, 0, 6),
        };

        var kindLabel = new Label
        {
            AutoSize = true,
            Text = $"Тип: {entry.RegistryValueKind}",
            Margin = new Padding(0, 0, 0, 8),
        };

        _valueBox.Dock = DockStyle.Fill;
        _valueBox.Multiline = true;
        _valueBox.ScrollBars = ScrollBars.Both;
        _valueBox.AcceptsReturn = true;
        _valueBox.AcceptsTab = true;
        _valueBox.WordWrap = false;
        _valueBox.Text = entry.RegistryEditText ?? entry.Command;
        UiTheme.StyleTextBox(_valueBox);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0),
        };

        var saveButton = new Button
        {
            Text = "Сохранить",
            AutoSize = true,
            DialogResult = DialogResult.OK,
            Margin = new Padding(8, 0, 0, 0),
        };
        UiTheme.StyleButton(saveButton, primary: true);

        var cancelButton = new Button
        {
            Text = "Отмена",
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
        };
        UiTheme.StyleButton(cancelButton);

        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        layout.Controls.Add(pathLabel, 0, 0);
        layout.Controls.Add(nameLabel, 0, 1);
        layout.Controls.Add(kindLabel, 0, 2);
        layout.Controls.Add(_valueBox, 0, 3);
        layout.Controls.Add(buttons, 0, 4);
        Controls.Add(layout);
    }

    private static string DisplayValueName(string? valueName)
    {
        return string.IsNullOrEmpty(valueName) ? "(по умолчанию)" : valueName;
    }

    private static string GetRegistryPathText(StartupEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.OfflineRegistryHiveFile))
        {
            return $"Offline hive: {entry.OfflineRegistryHiveFile}\\{entry.RegistryKeyPath}";
        }

        return $"{entry.RegistryHive}\\{entry.RegistryKeyPath}";
    }
}
