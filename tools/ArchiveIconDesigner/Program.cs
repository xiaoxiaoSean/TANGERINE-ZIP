using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

// Reproducible, code-native extension of the existing logo. The PNG logo itself
// stays unchanged. Outlined vector glyphs are rendered separately at each Shell
// size, rather than resizing a flattened preview or relying on installed fonts
// when the end user's computer reads the icon.
internal static class Program
{
    private static readonly int[] Sizes = [16, 24, 32, 48, 64, 128, 256];
    private static readonly string[] Labels = ["ZIP", "RAR", "7Z", "TAR", "GZIP", "BZIP2", "XZ", "LZ4", "ZSTD"];
    private static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0xF3, 0x7A, 0x07));

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Pass the repository root directory.");
            string root = Path.GetFullPath(args[0]);
            string output = Path.Combine(root, "TANGERINE-ZIP/Resources/FileTypeIcons");
            Directory.CreateDirectory(output);
            var original = BitmapFrame.Create(new Uri(Path.Combine(root, "TANGERINE-ZIP/Resources/TZIP.png")),
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            // Remove only the surrounding white margin from the composition;
            // every pixel of the logo artwork remains inside this source rectangle.
            var logo = new CroppedBitmap(original, new Int32Rect(320, 0, 790, 860));
            var drawings = Labels.ToDictionary(label => label, label => Design(logo, label));
            drawings.Add("TZIP", Design(logo, null));
            foreach (var (label, drawing) in drawings)
            {
                string stem = label.ToLowerInvariant();
                SaveIco(Path.Combine(output, stem + ".ico"), drawing);
                // WPF DrawingGroup XAML is the editable vector master, including
                // the unmodified source image reference and explicit glyph paths.
                SaveMaster(Path.Combine(output, stem + ".xaml"), drawing);
            }
            string preview = Path.Combine(root, "dev_doc1/assets/archive-icons-preview.png");
            Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
            SavePng(preview, Preview(drawings));
            Console.WriteLine($"Designed {drawings.Count} icons, each with {Sizes.Length} sizes. Preview: {preview}");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static DrawingGroup Design(ImageSource logo, string? label)
    {
        var group = new DrawingGroup();
        using var dc = group.Open();
        dc.DrawImage(logo, new Rect(35, 16, 442, 480));
        if (label is not null)
        {
            var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal),
                160, Orange, 1);
            Geometry glyphs = text.BuildGeometry(new Point());
            Rect bounds = glyphs.Bounds;
            // Fill the available width with the actual glyph outlines (no font
            // side bearings). Center their visible bounds vertically at 256.
            var transform = new TransformGroup();
            transform.Children.Add(new TranslateTransform(-bounds.X, -bounds.Y));
            transform.Children.Add(new ScaleTransform(474 / bounds.Width, 146 / bounds.Height));
            transform.Children.Add(new TranslateTransform(19, 183));
            glyphs.Transform = transform;
            var outline = new Pen(Brushes.White, 20) { LineJoin = PenLineJoin.Round };
            // Draw the outline first, then the fill, to preserve the orange body
            // of narrow strokes and counters in the 16/24-pixel icon frames.
            dc.DrawGeometry(Brushes.White, outline, glyphs);
            dc.DrawGeometry(Orange, null, glyphs);
        }
        return group;
    }

    private static byte[] Png(Drawing drawing, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 512d, size / 512d));
            dc.DrawDrawing(drawing);
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void SaveIco(string path, Drawing drawing)
    {
        // PNG-backed ICO frames are supported throughout Windows 10/11. In ICO
        // directory entries a zero width/height byte means 256, not an empty image.
        byte[][] frames = Sizes.Select(size => Png(drawing, size)).ToArray();
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)Sizes.Length);
        uint offset = (uint)(6 + 16 * Sizes.Length);
        for (int i = 0; i < Sizes.Length; i++)
        {
            writer.Write((byte)(Sizes[i] == 256 ? 0 : Sizes[i]));
            writer.Write((byte)(Sizes[i] == 256 ? 0 : Sizes[i]));
            writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write((uint)frames[i].Length); writer.Write(offset);
            offset += (uint)frames[i].Length;
        }
        foreach (byte[] frame in frames) writer.Write(frame);
    }

    private static void SaveMaster(string path, DrawingGroup drawing)
    {
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var group = new XElement(ns + "DrawingGroup");
        group.Add(new XElement(ns + "ImageDrawing", new XAttribute("Rect", "35,16,442,480"),
            new XElement(ns + "ImageDrawing.ImageSource", new XElement(ns + "CroppedBitmap",
                new XAttribute("Source", "../TZIP.png"), new XAttribute("SourceRect", "320,0,790,860")))));
        foreach (GeometryDrawing shape in drawing.Children.OfType<GeometryDrawing>())
        {
            // Bake transforms into paths, so the source has no font dependency.
            var geometry = shape.Geometry.GetFlattenedPathGeometry().Clone();
            var matrix = geometry.Transform?.Value ?? Matrix.Identity;
            foreach (PathFigure figure in geometry.Figures)
            {
                figure.StartPoint = matrix.Transform(figure.StartPoint);
                foreach (PolyLineSegment segment in figure.Segments.OfType<PolyLineSegment>())
                    for (int i = 0; i < segment.Points.Count; i++) segment.Points[i] = matrix.Transform(segment.Points[i]);
            }
            geometry.Transform = Transform.Identity;
            var element = new XElement(ns + "GeometryDrawing", new XAttribute("Brush", shape.Brush.ToString()),
                new XAttribute("Geometry", geometry.ToString(CultureInfo.InvariantCulture)));
            if (shape.Pen is { } pen)
                element.Add(new XElement(ns + "GeometryDrawing.Pen", new XElement(ns + "Pen",
                    new XAttribute("Brush", "White"), new XAttribute("Thickness", pen.Thickness), new XAttribute("LineJoin", "Round"))));
            group.Add(element);
        }
        new XDocument(group).Save(path);
    }

    private static Drawing Preview(Dictionary<string, DrawingGroup> drawings)
    {
        var group = new DrawingGroup();
        using var dc = group.Open();
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)), null, new Rect(0, 0, 960, 960));
        for (int i = 0; i < Labels.Length; i++)
        {
            double x = 22 + i % 3 * 314, y = 18 + i / 3 * 314;
            dc.DrawRoundedRectangle(Brushes.White, null, new Rect(x, y, 292, 294), 12, 12);
            dc.PushTransform(new TranslateTransform(x + 46, y + 10));
            dc.PushTransform(new ScaleTransform(200 / 512d, 200 / 512d));
            dc.DrawDrawing(drawings[Labels[i]]); dc.Pop(); dc.Pop();
            var caption = new FormattedText(Labels[i], CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 18, Brushes.Black, 1);
            dc.DrawText(caption, new Point(x + (292 - caption.Width) / 2, y + 207));
            double cursor = x + 28;
            foreach (int size in new[] { 16, 24, 32, 48, 64 })
            {
                using var stream = new MemoryStream(Png(drawings[Labels[i]], size));
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                dc.DrawImage(frame, new Rect(cursor, y + 234 + (64 - size) / 2d, size, size));
                cursor += size + 10;
            }
        }
        return group;
    }

    private static void SavePng(string path, Drawing drawing)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawDrawing(drawing);
        var bitmap = new RenderTargetBitmap(960, 960, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
