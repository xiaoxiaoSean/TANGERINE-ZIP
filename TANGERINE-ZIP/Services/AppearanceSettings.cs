using System.Globalization;
using System.Text;
using System.Windows.Media;

namespace TANGERINE_ZIP.Services;

// Stage head: COLRS (appearance color settings)
internal sealed class InvalidColorConfigurationException : Exception
{
    internal InvalidColorConfigurationException(string fileName, string stageCode)
        : base(fileName) => (FileName, StageCode) = (fileName, stageCode);

    internal string FileName { get; }
    internal string StageCode { get; }
}

internal sealed class ColorContrastException : Exception
{
    internal ColorContrastException(int accentIndex, string stageCode)
        : base(LanguageManager.Get("SettingsColorsTooSimilar")) =>
        (AccentIndex, StageCode) = (accentIndex, stageCode);

    internal int AccentIndex { get; }
    internal string StageCode { get; }
}

/// <summary>
/// Holds the four shared WPF colors. Configuration is stored as an invariant
/// seven-character #RRGGBB string beside the executable, one color per file.
/// The same mutable application brushes are used by every open window, so a
/// successful settings change takes effect without rebuilding any windows.
/// </summary>
internal static class AppearanceSettings
{
    internal static readonly string[] FileNames = ["COLOR1", "COLOR2", "COLOR3", "COLOR4"];
    private static readonly Color[] Defaults =
        [Colors.White, Colors.Black, Colors.Orange, Colors.White];
    private static readonly Color[] CurrentColors = (Color[])Defaults.Clone();
    private static readonly SemaphoreSlim SaveGate = new(1, 1);
    private const double MinimumContrastRatio = 1.8;

    internal static event Action? Changed;
    internal static Color GetColor(int index) => CurrentColors[index];
    internal static string Format(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    internal static void Initialize()
    {
        // Read every file before validating pairs. If one pair is too close,
        // the startup recovery dialog can change its accent while retaining
        // the other three values already read from disk.
        for (int index = 0; index < CurrentColors.Length; index++)
            CurrentColors[index] = ReadOrCreate(index);
        ValidatePair(CurrentColors, 0, "COLRS0009"); //COLRS0009
        ValidatePair(CurrentColors, 2, "COLRS0010"); //COLRS0010
        ApplyBrushes();
    }

    internal static void DeleteInvalidConfiguration(InvalidColorConfigurationException invalid)
    {
        if (!FileNames.Contains(invalid.FileName, StringComparer.Ordinal))
            throw new StageException("COLRS0006", LanguageManager.Get("SettingsColorUnexpectedFile")); //COLRS0006
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, invalid.FileName);
            File.Delete(path);
            if (File.Exists(path)) throw new IOException(LanguageManager.Get("MouseConfigDeleteIncomplete"));
        }
        catch (Exception exception)
        {
            throw new StageException("COLRS0006",
                string.Format(LanguageManager.Get("SettingsColorDeleteFailed"), invalid.FileName, exception.Message), exception); //COLRS0006
        }
    }

    internal static async Task SetColorAsync(int index, Color color)
    {
        if (index < 0 || index >= FileNames.Length || color.A != byte.MaxValue)
            throw new StageException("COLRS0007", LanguageManager.Get("SettingsColorInvalidSelection")); //COLRS0007
        await SaveGate.WaitAsync();
        try
        {
            Color[] candidate = (Color[])CurrentColors.Clone();
            candidate[index] = color;
            ValidatePair(candidate, index < 2 ? 0 : 2, "COLRS0008"); //COLRS0008

            // Persist before broadcasting, so a failed write never makes an
            // unsaved color visible to other windows.
            await Task.Run(() => WriteAndVerify(index, color));
            CurrentColors[index] = color;
            ApplyBrushes();
            Changed?.Invoke();
        }
        finally { SaveGate.Release(); }
    }

    internal static void SetColorAtStartup(int index, Color color)
    {
        // Startup has no running dispatcher yet. Keep this short write on the
        // startup thread; the normal settings page uses the async method.
        Color[] candidate = (Color[])CurrentColors.Clone();
        candidate[index] = color;
        ValidatePair(candidate, index < 2 ? 0 : 2, "COLRS0014"); //COLRS0014
        WriteAndVerify(index, color);
        CurrentColors[index] = color;
    }

    private static Color ReadOrCreate(int index)
    {
        string fileName = FileNames[index];
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        string text;
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 32)
                throw new InvalidColorConfigurationException(fileName, $"COLRS000{index + 1}"); //COLRS0001-COLRS0004
            using StreamReader reader = new(stream, new UTF8Encoding(false, true), false);
            text = reader.ReadToEnd();
        }
        catch (FileNotFoundException)
        {
            try
            {
                using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                using StreamWriter writer = new(stream, new UTF8Encoding(false));
                writer.Write(Format(Defaults[index]));
                return Defaults[index];
            }
            catch (IOException) when (File.Exists(path))
            {
                return ReadOrCreate(index);
            }
            catch (Exception exception)
            {
                throw new StageException("COLRS0005",
                    string.Format(LanguageManager.Get("SettingsColorCreateFailed"), fileName, exception.Message), exception); //COLRS0005
            }
        }
        catch (InvalidColorConfigurationException) { throw; }
        catch (DecoderFallbackException)
        {
            // Invalid UTF-8 is malformed configuration data, not an I/O
            // failure, so startup must offer the same delete-and-recreate
            // choice as for a malformed #RRGGBB value.
            throw new InvalidColorConfigurationException(fileName, $"COLRS000{index + 1}"); //COLRS0001-COLRS0004
        }
        catch (Exception exception)
        {
            throw new StageException("COLRS0011",
                string.Format(LanguageManager.Get("SettingsColorReadFailed"), fileName, exception.Message), exception); //COLRS0011
        }

        if (!TryParse(text, out Color color))
            throw new InvalidColorConfigurationException(fileName, $"COLRS000{index + 1}"); //COLRS0001-COLRS0004
        return color;
    }

    private static bool TryParse(string text, out Color color)
    {
        color = default;
        if (text.Length != 7 || text[0] != '#') return false;
        if (!byte.TryParse(text.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red) ||
            !byte.TryParse(text.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green) ||
            !byte.TryParse(text.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue))
            return false;
        color = Color.FromRgb(red, green, blue);
        return true;
    }

    private static void ValidatePair(Color[] colors, int accentIndex, string stageCode)
    {
        Color first = colors[accentIndex];
        Color second = colors[accentIndex + 1];
        double firstLuminance = Luminance(first);
        double secondLuminance = Luminance(second);
        double contrast = (Math.Max(firstLuminance, secondLuminance) + 0.05) /
            (Math.Min(firstLuminance, secondLuminance) + 0.05);
        double distance = Math.Sqrt(Math.Pow(first.R - second.R, 2) +
            Math.Pow(first.G - second.G, 2) + Math.Pow(first.B - second.B, 2));
        // Both checks are needed: luminance alone treats distinct hues with
        // similar brightness as identical, while RGB distance alone can admit
        // nearly unreadable foreground/background combinations.
        if (contrast < MinimumContrastRatio || distance < 100)
            throw new ColorContrastException(accentIndex, stageCode); //COLRS0008-COLRS0010
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            double value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private static void WriteAndVerify(int index, Color color)
    {
        string fileName = FileNames[index];
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Exception? failure = null;
        try
        {
            File.WriteAllText(temporaryPath, Format(color), new UTF8Encoding(false));
            File.Move(temporaryPath, path, true);
            if (ReadOrCreate(index) != color)
                throw new IOException(LanguageManager.Get("MouseConfigSaveMismatch"));
        }
        catch (Exception exception) { failure = exception; }
        try
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        catch (Exception cleanupFailure)
        {
            throw new StageException("COLRS0012",
                string.Format(LanguageManager.Get("SettingsColorSaveFailed"), fileName, cleanupFailure.Message),
                failure is null ? cleanupFailure : new AggregateException(failure, cleanupFailure)); //COLRS0012
        }
        if (failure is not null)
            throw new StageException("COLRS0013",
                string.Format(LanguageManager.Get("SettingsColorSaveFailed"), fileName, failure.Message), failure); //COLRS0013
    }

    private static void ApplyBrushes()
    {
        ResourceDictionary resources = Application.Current.Resources;
        SetBrush(resources, "BackgroundBrush", CurrentColors[1]);
        SetBrush(resources, "ForegroundBrush", CurrentColors[0]);
        SetBrush(resources, "SurfaceBrush", Blend(CurrentColors[1], CurrentColors[0], 0.08));
        SetBrush(resources, "ButtonBrush", Blend(CurrentColors[1], CurrentColors[0], 0.13));
        SetBrush(resources, "BorderBrush", Blend(CurrentColors[1], CurrentColors[0], 0.38));
        SetBrush(resources, "ProgressAccentBrush", CurrentColors[2]);
        SetBrush(resources, "ProgressBackgroundBrush", CurrentColors[3]);
    }

    private static void SetBrush(ResourceDictionary resources, string key, Color color)
    {
        if (resources[key] is SolidColorBrush existing) existing.Color = color;
        else resources[key] = new SolidColorBrush(color);
    }

    private static Color Blend(Color background, Color foreground, double foregroundAmount) =>
        Color.FromRgb(
            (byte)Math.Round(background.R * (1 - foregroundAmount) + foreground.R * foregroundAmount),
            (byte)Math.Round(background.G * (1 - foregroundAmount) + foreground.G * foregroundAmount),
            (byte)Math.Round(background.B * (1 - foregroundAmount) + foreground.B * foregroundAmount));
}
