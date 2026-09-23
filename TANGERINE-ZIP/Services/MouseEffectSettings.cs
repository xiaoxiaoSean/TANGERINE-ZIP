using System.Globalization;
using System.Text;

namespace TANGERINE_ZIP.Services;

/// <summary>
/// Carries the exact configuration file that the startup dialog may offer to
/// delete. An invalid value is distinct from an I/O error: only invalid values
/// may be deleted after explicit user confirmation.
/// </summary>
internal sealed class InvalidMouseEffectConfigurationException : Exception
{
    internal InvalidMouseEffectConfigurationException(string fileName, string stageCode, double minimum, double maximum)
        : base(fileName)
    {
        FileName = fileName;
        StageCode = stageCode;
        Minimum = minimum;
        Maximum = maximum;
    }

    internal string FileName { get; }
    internal string StageCode { get; }
    internal double Minimum { get; }
    internal double Maximum { get; }
}

/// <summary>
/// Persists the mouse effect switch beside the executable. The absence of the
/// NO_MOUSE_EFFECT marker means ON; its presence means OFF. Keeping this
/// mapping here prevents startup and the settings page from disagreeing.
/// </summary>
internal static class MouseEffectSettings
{
    private const string MarkerName = "NO_MOUSE_EFFECT";
    private const string RadiusFileName = "MOUSE_EFFECT_CONFIG1";
    private const string ThicknessFileName = "MOUSE_EFFECT_CONFIG2";
    internal const double DefaultRadius = 140.0;
    internal const double MinimumRadius = 40.0;
    internal const double MaximumRadius = 360.0;
    internal const double DefaultThickness = 0.95;
    internal const double MinimumThickness = 0.20;
    internal const double MaximumThickness = 1.25;
    private static bool _isEnabled;
    private static double _radius = DefaultRadius;
    private static double _thickness = DefaultThickness;
    private static bool _parametersInitialized;

    internal static event Action<bool>? Changed;
    internal static event Action<double, double>? ParametersChanged;
    internal static bool IsEnabled => _isEnabled;
    internal static double Radius => _radius;
    internal static double Thickness => _thickness;
    internal static bool ParametersInitialized => _parametersInitialized;
    internal static string MarkerPath => Path.Combine(AppContext.BaseDirectory, MarkerName);

    internal static void Initialize() => _isEnabled = !ReadMarkerState();

    /// <summary>
    /// Only startup failure uses this fallback, after the error has been shown.
    /// It avoids enabling a visual effect against an unreadable configuration.
    /// </summary>
    internal static void UseDisabledFallback() => _isEnabled = false;

    /// <summary>
    /// Runs before the first window is constructed. A missing file is created
    /// with an invariant-culture default; an invalid existing value is returned
    /// to Program for a separate, irreversible-delete confirmation.
    /// </summary>
    internal static void InitializeParameters()
    {
        double radius = ReadOrCreate(RadiusFileName, DefaultRadius, MinimumRadius, MaximumRadius, "MECFG0001"); //MECFG0001
        double thickness = ReadOrCreate(ThicknessFileName, DefaultThickness, MinimumThickness, MaximumThickness, "MECFG0002"); //MECFG0002
        _radius = radius;
        _thickness = thickness;
        _parametersInitialized = true;
    }

    internal static void DeleteInvalidConfiguration(InvalidMouseEffectConfigurationException invalid)
    {
        if (invalid.FileName is not (RadiusFileName or ThicknessFileName))
            throw new StageException("MECFG0005", LanguageManager.Get("MouseConfigUnexpectedFile")); //MECFG0005
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, invalid.FileName);
            File.Delete(path);
            if (File.Exists(path))
                throw new IOException(LanguageManager.Get("MouseConfigDeleteIncomplete"));
        }
        catch (Exception exception)
        {
            throw new StageException("MECFG0005",
                string.Format(LanguageManager.Get("MouseConfigDeleteFailed"), invalid.FileName, exception.Message), exception); //MECFG0005
        }
    }

    internal static async Task SetRadiusAsync(double radius)
    {
        ValidateRequestedValue(radius, MinimumRadius, MaximumRadius, "MECFG0006"); //MECFG0006
        await Task.Run(() => WriteAndVerify(RadiusFileName, radius, MinimumRadius, MaximumRadius, "MECFG0007")); //MECFG0007
        _radius = radius;
        ParametersChanged?.Invoke(_radius, _thickness);
    }

    internal static async Task SetThicknessAsync(double thickness)
    {
        ValidateRequestedValue(thickness, MinimumThickness, MaximumThickness, "MECFG0008"); //MECFG0008
        await Task.Run(() => WriteAndVerify(ThicknessFileName, thickness, MinimumThickness, MaximumThickness, "MECFG0009")); //MECFG0009
        _thickness = thickness;
        ParametersChanged?.Invoke(_radius, _thickness);
    }

    private static double ReadOrCreate(string fileName, double defaultValue, double minimum, double maximum, string invalidStageCode)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        string text;
        try
        {
            // Reject unexpectedly large input before allocating a string. A
            // setting contains one short number, not an arbitrary document.
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 64)
                throw new InvalidMouseEffectConfigurationException(fileName, invalidStageCode, minimum, maximum);
            using StreamReader reader = new(stream, Encoding.UTF8, true);
            text = reader.ReadToEnd();
        }
        catch (FileNotFoundException)
        {
            try
            {
                // CreateNew avoids overwriting a file created by another process
                // between the missing-file check and creation.
                using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                using StreamWriter writer = new(stream, new UTF8Encoding(false));
                writer.Write(defaultValue.ToString("R", CultureInfo.InvariantCulture));
                return defaultValue;
            }
            catch (IOException) when (File.Exists(path))
            {
                return ReadOrCreate(fileName, defaultValue, minimum, maximum, invalidStageCode);
            }
            catch (Exception exception)
            {
                throw new StageException("MECFG0003",
                    string.Format(LanguageManager.Get("MouseConfigCreateFailed"), fileName, exception.Message), exception); //MECFG0003
            }
        }
        catch (InvalidMouseEffectConfigurationException) { throw; }
        catch (Exception exception)
        {
            throw new StageException("MECFG0004",
                string.Format(LanguageManager.Get("MouseConfigReadFailed"), fileName, exception.Message), exception); //MECFG0004
        }

        if (text.Length > 64 || !double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || !double.IsFinite(value) || value < minimum || value > maximum)
            throw new InvalidMouseEffectConfigurationException(fileName, invalidStageCode, minimum, maximum);
        return value;
    }

    private static void ValidateRequestedValue(double value, double minimum, double maximum, string stageCode)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new StageException(stageCode,
                string.Format(LanguageManager.Get("MouseConfigValueOutOfRange"), minimum, maximum)); //MECFG0006/MECFG0008
    }

    private static void WriteAndVerify(string fileName, double value, double minimum, double maximum, string stageCode)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        string temporaryPath = Path.Combine(AppContext.BaseDirectory, fileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        Exception? writeFailure = null;
        try
        {
            // The temporary file is in the destination directory. Renaming it
            // over the old file avoids leaving a partially written value if the
            // process stops during the write.
            File.WriteAllText(temporaryPath, value.ToString("R", CultureInfo.InvariantCulture), new UTF8Encoding(false));
            File.Move(temporaryPath, path, true);
            double savedValue = ReadOrCreate(fileName, value, minimum, maximum, stageCode);
            if (Math.Abs(savedValue - value) > 0.001)
                throw new IOException(LanguageManager.Get("MouseConfigSaveMismatch"));
        }
        catch (Exception exception)
        {
            writeFailure = exception;
        }
        try
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
        catch (Exception cleanupFailure)
        {
            Exception cause = writeFailure is null ? cleanupFailure : new AggregateException(writeFailure, cleanupFailure);
            throw new StageException("MECFG0010",
                string.Format(LanguageManager.Get("MouseConfigTemporaryCleanupFailed"), temporaryPath, cause.Message), cause); //MECFG0010
        }
        if (writeFailure is not null)
            throw new StageException(stageCode,
                string.Format(LanguageManager.Get("MouseConfigSaveFailed"), fileName, writeFailure.Message), writeFailure); //MECFG0007/MECFG0009
    }

    internal static async Task SetEnabledAsync(bool enabled)
    {
        // File operations run away from the UI thread. The awaited continuation
        // returns to WPF's dispatcher before the change is broadcast to windows.
        await Task.Run(() => PersistMarker(enabled));
        _isEnabled = enabled;
        Changed?.Invoke(enabled);
    }

    private static void PersistMarker(bool enabled)
    {
        try
        {
            bool markerExists = ReadMarkerState();
            if (!enabled && !markerExists)
            {
                // OpenOrCreate does not truncate an existing marker if another
                // process creates it between the read and this operation.
                using FileStream marker = new(MarkerPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            }
            else if (enabled && markerExists)
            {
                File.Delete(MarkerPath);
            }

            if (ReadMarkerState() == enabled)
                throw new StageException("MESVC0004", LanguageManager.Get("MouseEffectSaveMismatch")); //MESVC0004
        }
        catch (StageException) { throw; }
        catch (Exception exception)
        {
            string code = enabled ? "MESVC0002" : "MESVC0003";
            throw new StageException(code,
                string.Format(LanguageManager.Get("MouseEffectSaveFailed"), exception.Message), exception); //MESVC0002/MESVC0003
        }
    }

    private static bool ReadMarkerState()
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(MarkerPath);
            if ((attributes & FileAttributes.Directory) != 0)
                throw new StageException("MESVC0001", LanguageManager.Get("MouseEffectMarkerIsDirectory")); //MESVC0001
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (StageException) { throw; }
        catch (Exception exception)
        {
            throw new StageException("MESVC0001",
                string.Format(LanguageManager.Get("MouseEffectReadFailed"), exception.Message), exception); //MESVC0001
        }
    }
}
