namespace TANGERINE_ZIP.Services;

/// <summary>
/// Persists the mouse effect switch beside the executable. The absence of the
/// NO_MOUSE_EFFECT marker means ON; its presence means OFF. Keeping this
/// mapping here prevents startup and the settings page from disagreeing.
/// </summary>
internal static class MouseEffectSettings
{
    private const string MarkerName = "NO_MOUSE_EFFECT";
    private static bool _isEnabled;

    internal static event Action<bool>? Changed;
    internal static bool IsEnabled => _isEnabled;
    internal static string MarkerPath => Path.Combine(AppContext.BaseDirectory, MarkerName);

    internal static void Initialize() => _isEnabled = !ReadMarkerState();

    /// <summary>
    /// Only startup failure uses this fallback, after the error has been shown.
    /// It avoids enabling a visual effect against an unreadable configuration.
    /// </summary>
    internal static void UseDisabledFallback() => _isEnabled = false;

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
