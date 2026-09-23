global using System.IO;
global using System.Windows;
global using System.Windows.Controls;
global using System.Windows.Media;
global using Microsoft.Win32;

namespace TANGERINE_ZIP;

internal static class WpfUi
{
    public static readonly Brush Foreground = Brushes.WhiteSmoke;
    public static readonly Brush DirectoryForeground = Brushes.LightGoldenrodYellow;

    public static void SizeWindow(Window window, double widthShare, double heightShare)
    {
        Rect area = SystemParameters.WorkArea;
        window.Width = area.Width * widthShare;
        window.Height = area.Height * heightShare;
    }

    public static bool Confirm(Window? owner, string message, string title) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

}
