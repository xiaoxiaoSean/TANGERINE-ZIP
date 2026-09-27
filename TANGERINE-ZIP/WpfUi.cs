global using System.IO;
global using System.Windows;
global using System.Windows.Controls;
global using System.Windows.Media;
global using Microsoft.Win32;

namespace TANGERINE_ZIP;

internal static class WpfUi
{
    public static void SizeWindow(Window window, double widthShare, double heightShare)
    {
        Rect area = SystemParameters.WorkArea;
        window.Width = area.Width * widthShare;
        window.Height = area.Height * heightShare;
    }

    public static bool Confirm(Window? owner, string message, string title) =>
        ThemedPromptWindow.Ask(owner, title, message,
            (LanguageManager.Get("PromptYes"), MessageBoxResult.Yes),
            (LanguageManager.Get("PromptNo"), MessageBoxResult.No)) == MessageBoxResult.Yes;

}
