using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TANGERINE_ZIP;
using TANGERINE_ZIP.Services;

internal static class UiProbe
{
    public static void Run(string outputDirectory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = new Application();
                Directory.CreateDirectory(outputDirectory);
                foreach (Type windowType in new[]
                {
                    typeof(MainWindow), typeof(ArchivePasswordWindow), typeof(ContextMenuSetupWindow),
                    typeof(ContextOperationWindow), typeof(OverwriteConflictWindow), typeof(FilePickerWindow),
                    typeof(OverwriteDecisionWindow), typeof(AboutWindow)
                })
                {
                    var constructor = windowType.GetConstructor(Type.EmptyTypes)
                        ?? throw new InvalidOperationException($"{windowType.Name}: no parameterless design constructor");
                    if (constructor.Invoke(null) is not Window preview || preview.Content is null)
                        throw new InvalidOperationException($"{windowType.Name}: XAML content unavailable");
                    Console.WriteLine($"PASS XAML designer constructor: {windowType.Name}");
                }
                var main = new MainWindow();
                typeof(MainWindow).GetMethod("MainWindow_Load", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(main, [main, EventArgs.Empty]);
                var windows = new (string Name, Window Window)[]
                {
                    ("main", main),
                    ("password", new ArchivePasswordWindow("sample.zip", true)),
                    ("conflict", new OverwriteConflictWindow(new ArchiveConflict("entry.txt", "C:\\Target\\entry.txt"))),
                    ("wizard", new ContextMenuSetupWindow(ContextMenuSetupMode.Create, "sample.exe")),
                    ("operation", new ContextOperationWindow("Extract", (_, _) => Task.CompletedTask)),
                    ("picker", new FilePickerWindow()),
                    ("overwrite", new OverwriteDecisionWindow()),
                    ("about", new AboutWindow())
                };
                foreach ((string name, Window window) in windows)
                {
                    if (window.Content is not FrameworkElement root) throw new InvalidOperationException($"{name}: missing content");
                    foreach ((string suffix, double share) in new[] { ("", 0.65), ("-compact", 0.4) })
                    {
                        int width = (int)(SystemParameters.WorkArea.Width * share);
                        int height = (int)(SystemParameters.WorkArea.Height * share);
                        root.Measure(new Size(width, height));
                        root.Arrange(new Rect(0, 0, width, height));
                        root.UpdateLayout();
                        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(root);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(outputDirectory, $"{name}{suffix}.png"));
                        encoder.Save(stream);
                    }
                    Console.WriteLine($"PASS WPF layout at two viewport sizes: {name}");
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}
