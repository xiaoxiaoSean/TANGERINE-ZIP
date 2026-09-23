global using System.Windows;
global using System.Windows.Controls;
global using System.Windows.Media;
global using System.IO;
global using System.Windows.Controls.Primitives;
global using Microsoft.Win32;

namespace TANGERINE_ZIP;

internal static class WpfUi
{
    public static readonly Brush Background = Brushes.Black;
    public static readonly Brush Foreground = Brushes.WhiteSmoke;
    public static readonly Brush Surface = new SolidColorBrush(Color.FromRgb(22, 22, 22));
    public static readonly Brush ButtonBackground = new SolidColorBrush(Color.FromRgb(38, 38, 38));
    public static readonly Brush DirectoryForeground = Brushes.LightGoldenrodYellow;
    public static readonly Brush ErrorForeground = Brushes.OrangeRed;

    public static void Style(Window window)
    {
        window.Background = Background;
        window.Foreground = Foreground;
        window.FontFamily = new FontFamily("Microsoft YaHei UI");
        window.FontSize = SystemParameters.WorkArea.Height * 0.018;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var menuStyle = new Style(typeof(MenuItem));
        menuStyle.Setters.Add(new Setter(Control.ForegroundProperty, Foreground));
        menuStyle.Setters.Add(new Setter(Control.BackgroundProperty, Background));
        window.Resources[typeof(MenuItem)] = menuStyle;
        var tabStyle = new Style(typeof(TabItem));
        tabStyle.Setters.Add(new Setter(Control.ForegroundProperty, Foreground));
        tabStyle.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
        window.Resources[typeof(TabItem)] = tabStyle;
    }

    public static void SizeWindow(Window window, double widthShare, double heightShare)
    {
        Rect area = SystemParameters.WorkArea;
        window.Width = area.Width * widthShare;
        window.Height = area.Height * heightShare;
    }

    public static Grid Grid(params double[] rows)
    {
        var grid = new Grid();
        foreach (double weight in rows)
            grid.RowDefinitions.Add(new RowDefinition { Height = weight == 0 ? GridLength.Auto : new GridLength(weight, GridUnitType.Star) });
        return grid;
    }

    public static void Add(Grid grid, UIElement element, int row, int column = 0)
    {
        System.Windows.Controls.Grid.SetRow(element, row);
        System.Windows.Controls.Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    public static Button Button(string text) => new()
    {
        Content = text, Background = ButtonBackground, Foreground = Foreground,
        BorderBrush = Brushes.DimGray, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(4)
    };

    public static ProgressBar Progress() => new()
    {
        Minimum = 0, Maximum = 100, Foreground = Brushes.Orange,
        Background = Surface, BorderBrush = Brushes.DimGray, Margin = new Thickness(4)
    };

    public static TextBlock Text(string text) => new()
    {
        Text = text, Foreground = Foreground, TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4)
    };

    public static ListBox List(bool multiple = false) => new()
    {
        Background = Surface, Foreground = Foreground, BorderBrush = Brushes.DimGray,
        SelectionMode = multiple ? SelectionMode.Extended : SelectionMode.Single,
        HorizontalContentAlignment = HorizontalAlignment.Stretch
    };

    public static bool Confirm(Window? owner, string message, string title) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    public static Image Logo(System.Drawing.Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;
        var source = new System.Windows.Media.Imaging.BitmapImage();
        source.BeginInit();
        source.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        source.StreamSource = stream;
        source.EndInit();
        source.Freeze();
        return new Image { Source = source, Stretch = Stretch.Uniform };
    }

    public static ImageSource? WindowIcon(Type resourceOwner)
    {
        var resources = new System.ComponentModel.ComponentResourceManager(resourceOwner);
        return resources.GetObject("$this.Icon") is System.Drawing.Icon icon
            ? System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions())
            : null;
    }
}
