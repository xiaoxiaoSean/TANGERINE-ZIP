using TANGERINE_ZIP.Resources;

namespace TANGERINE_ZIP;

public sealed class TZIPForm : Window
{
    public TZIPForm()
    {
        WpfUi.Style(this);
        Icon = WpfUi.WindowIcon(typeof(TZIPForm));
        Title = LanguageManager.Get("ApplicationTitle");
        WindowState = WindowState.Maximized;
        var root = WpfUi.Grid(2, 1);
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        WpfUi.Add(root, WpfUi.Logo(TZIPResource.Kiro), 0);
        WpfUi.Add(root, WpfUi.Logo(TZIPResource.TZIP), 0, 1);
        var information = new TextBox
        {
            Text = new System.ComponentModel.ComponentResourceManager(typeof(TZIPForm)).GetString("textBox1.Text") ?? string.Empty,
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = WpfUi.Surface, Foreground = WpfUi.Foreground, Margin = new Thickness(8)
        };
        WpfUi.Add(root, information, 1);
        var close = WpfUi.Button(LanguageManager.Get("Close"));
        close.Click += (_, _) => Close();
        WpfUi.Add(root, close, 1, 1);
        Content = root;
    }
}
