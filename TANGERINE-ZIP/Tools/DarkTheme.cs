namespace TANGERINE_ZIP.Tools;

internal static class DarkTheme
{
    private static readonly Color Background = Color.Black;
    private static readonly Color Foreground = Color.WhiteSmoke;

    public static void Apply(Control root)
    {
        root.BackColor = Background;
        root.ForeColor = Foreground;
        foreach (Control child in root.Controls) Apply(child);
        if (root is Form form) ApplyToolStrips(form.Controls);
    }

    private static void ApplyToolStrips(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            if (control is ToolStrip strip)
            {
                strip.BackColor = Background; strip.ForeColor = Foreground;
                foreach (ToolStripItem item in strip.Items) ApplyToolStripItem(item);
            }
            ApplyToolStrips(control.Controls);
        }
    }

    private static void ApplyToolStripItem(ToolStripItem item)
    {
        item.BackColor = Background; item.ForeColor = Foreground;
        if (item is ToolStripDropDownItem dropDownItem)
            foreach (ToolStripItem child in dropDownItem.DropDownItems) ApplyToolStripItem(child);
    }
}
