using System.Windows.Input;
using System.Windows.Media.Effects;

namespace TANGERINE_ZIP;

/// <summary>
/// Applies a small GPU-rendered dilation to near-white pixels around the pointer.
/// The effect acts on the window's content visual, so text, icons and image details
/// share the same radius without changing their layout or hit testing.
/// </summary>
internal static class MouseWhiteThickening
{
    public static void Attach(Window window, double radius)
    {
        if (window.Content is not FrameworkElement content)
            throw new InvalidOperationException("A WPF window must have a content element before the mouse effect is attached.");
        if (!double.IsFinite(radius) || radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(radius));

        MouseWhiteThickenEffect effect = new() { Radius = radius };
        content.Effect = effect;

        void UpdateViewport(object? sender, SizeChangedEventArgs args)
        {
            // Shader coordinates are relative to the content visual, not the outer title bar.
            effect.Viewport = new Point(Math.Max(content.ActualWidth, 1), Math.Max(content.ActualHeight, 1));
        }

        void UpdatePointer(object? sender, MouseEventArgs args)
        {
            Point position = args.GetPosition(content);
            double width = content.ActualWidth;
            double height = content.ActualHeight;
            effect.Cursor = width > 0 && height > 0 &&
                position.X >= 0 && position.Y >= 0 && position.X <= width && position.Y <= height
                ? new Point(position.X / width, position.Y / height)
                : new Point(-100, -100);
        }

        void ClearPointer(object? sender, EventArgs args) => effect.Cursor = new Point(-100, -100);

        content.SizeChanged += UpdateViewport;
        window.AddHandler(UIElement.PreviewMouseMoveEvent, new MouseEventHandler(UpdatePointer), true);
        window.MouseLeave += ClearPointer;
        window.Deactivated += ClearPointer;
        window.Closed += (_, _) =>
        {
            content.SizeChanged -= UpdateViewport;
            window.MouseLeave -= ClearPointer;
            window.Deactivated -= ClearPointer;
            content.Effect = null;
        };
        effect.Viewport = new Point(Math.Max(content.ActualWidth, 1), Math.Max(content.ActualHeight, 1));
    }
}

/// <summary>
/// The shader expands only white and light-gray source pixels. The radius and mouse
/// position are updated as constants; moving the pointer does not render the window
/// into a bitmap or allocate a new effect instance.
/// </summary>
internal sealed class MouseWhiteThickenEffect : ShaderEffect
{
    private static readonly PixelShader SharedShader = new()
    {
        UriSource = new Uri("/TANGERINE-ZIP;component/Shaders/MouseWhiteThicken.ps", UriKind.Relative)
    };

    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty(nameof(Input), typeof(MouseWhiteThickenEffect), 0);
    public static readonly DependencyProperty CursorProperty =
        DependencyProperty.Register(nameof(Cursor), typeof(Point), typeof(MouseWhiteThickenEffect),
            new UIPropertyMetadata(new Point(-100, -100), PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty ViewportProperty =
        DependencyProperty.Register(nameof(Viewport), typeof(Point), typeof(MouseWhiteThickenEffect),
            new UIPropertyMetadata(new Point(1, 1), PixelShaderConstantCallback(1)));
    public static readonly DependencyProperty RadiusProperty =
        DependencyProperty.Register(nameof(Radius), typeof(double), typeof(MouseWhiteThickenEffect),
            new UIPropertyMetadata(120.0, PixelShaderConstantCallback(2)));

    public MouseWhiteThickenEffect()
    {
        PixelShader = SharedShader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(CursorProperty);
        UpdateShaderValue(ViewportProperty);
        UpdateShaderValue(RadiusProperty);
    }

    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public Point Cursor { get => (Point)GetValue(CursorProperty); set => SetValue(CursorProperty, value); }
    public Point Viewport { get => (Point)GetValue(ViewportProperty); set => SetValue(ViewportProperty, value); }
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
}
