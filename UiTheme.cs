using System.Drawing;
using System.Windows.Forms;

namespace IUnlocker;

internal static class UiTheme
{
    public static readonly Color Background = Color.FromArgb(246, 247, 250);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceSoft = Color.FromArgb(250, 251, 253);
    public static readonly Color Border = Color.FromArgb(216, 222, 232);
    public static readonly Color BorderSoft = Color.FromArgb(232, 236, 243);
    public static readonly Color Text = Color.FromArgb(27, 33, 44);
    public static readonly Color MutedText = Color.FromArgb(91, 99, 114);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);
    public static readonly Color AccentSoft = Color.FromArgb(235, 242, 255);
    public static readonly Color AccentPressed = Color.FromArgb(219, 232, 255);

    public static void ApplyForm(Form form)
    {
        form.BackColor = Background;
        form.Font = AppFonts.Create(9F);
    }

    public static void StyleTitle(Label label, float size = 20F)
    {
        label.ForeColor = Text;
        label.Font = AppFonts.Create(size, FontStyle.Bold);
    }

    public static void StyleSubtitle(Label label)
    {
        label.ForeColor = MutedText;
        label.Font = AppFonts.Create(10F);
    }

    public static void StyleInfo(Label label)
    {
        label.BackColor = Surface;
        label.ForeColor = Text;
        label.BorderStyle = BorderStyle.FixedSingle;
        label.Padding = new Padding(14, 10, 14, 10);
    }

    public static void StyleButton(Button button, bool primary = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = primary ? Accent : Surface;
        button.ForeColor = primary ? Color.White : Text;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(29, 78, 216) : AccentSoft;
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(30, 64, 175) : AccentPressed;
        button.TextAlign = ContentAlignment.MiddleCenter;

        if (button.Font.Size < 9.5F)
        {
            button.Font = AppFonts.Create(9.5F);
        }
    }

    public static void StyleTextBox(TextBox textBox)
    {
        textBox.BackColor = Surface;
        textBox.ForeColor = Text;
        textBox.BorderStyle = BorderStyle.FixedSingle;
    }

    public static void StyleCheckBox(CheckBox checkBox)
    {
        checkBox.ForeColor = Text;
        checkBox.BackColor = Background;
        checkBox.FlatStyle = FlatStyle.System;
    }

    public static void StyleTree(TreeView tree)
    {
        tree.BackColor = Surface;
        tree.ForeColor = Text;
        tree.BorderStyle = BorderStyle.FixedSingle;
        tree.LineColor = Border;
    }

    public static void StyleListView(ListView listView)
    {
        listView.BackColor = Surface;
        listView.ForeColor = Text;
        listView.BorderStyle = BorderStyle.FixedSingle;
        listView.GridLines = true;
        listView.HideSelection = false;
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Surface;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.GridColor = BorderSoft;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.RowHeadersVisible = false;
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Accent;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = SurfaceSoft;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 244, 249);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(241, 244, 249);
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
        grid.RowTemplate.Height = Math.Max(grid.RowTemplate.Height, 24);
    }

    public static void StyleContextMenu(ContextMenuStrip menu, bool dark = false)
    {
        if (dark)
        {
            menu.BackColor = Color.FromArgb(37, 37, 37);
            menu.ForeColor = Color.White;
            return;
        }

        menu.BackColor = Surface;
        menu.ForeColor = Text;
        menu.RenderMode = ToolStripRenderMode.System;
    }

    public static bool HideUnavailableContextMenuItems(ContextMenuStrip menu)
    {
        foreach (ToolStripItem item in menu.Items)
        {
            item.Visible = item is ToolStripSeparator || item.Enabled;
        }

        return menu.Items.Cast<ToolStripItem>().Any(item => item.Visible && item is not ToolStripSeparator);
    }

    public static void ApplyControlTree(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            switch (control)
            {
                case Button button:
                    StyleButton(button);
                    break;
                case TextBox textBox:
                    StyleTextBox(textBox);
                    break;
                case CheckBox checkBox:
                    StyleCheckBox(checkBox);
                    break;
                case DataGridView grid:
                    StyleGrid(grid);
                    break;
                case ListView listView:
                    StyleListView(listView);
                    break;
                case TreeView tree:
                    StyleTree(tree);
                    break;
                case Label label:
                    label.ForeColor = label.ForeColor == SystemColors.ControlText ? Text : label.ForeColor;
                    break;
                case Panel or TableLayoutPanel or FlowLayoutPanel:
                    control.BackColor = parent is Form ? Background : control.BackColor;
                    break;
            }

            if (control.HasChildren)
            {
                ApplyControlTree(control);
            }
        }
    }
}
