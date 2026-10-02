using System.Text;
using System.Text.Json;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

internal sealed record CompressionProfile(string Name, FileDetector.FileType Format, CompressionOptions Options)
{
    public override string ToString() => Name;
}

// Stage head: CPRFL. Profiles intentionally omit passwords. The file is a
// convenience setting, never a credential store, and each write is atomic.
internal static class CompressionProfileStore
{
    private const string FileName = "COMPRESSION_PROFILES.json";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string StorePath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static IReadOnlyList<CompressionProfile> Load(FileDetector.FileType format)
    {
        try
        {
            if (!File.Exists(StorePath)) return [];
            FileInfo file = new(StorePath);
            if (file.Length > 1024 * 1024)
                throw new StageException("CPRFL0001", LanguageManager.Get("ProfileInvalidFile")); //CPRFL0001
            CompressionProfile[] profiles = JsonSerializer.Deserialize<CompressionProfile[]>(
                File.ReadAllText(StorePath, new UTF8Encoding(false, true))) ?? [];
            if (profiles.Length > 100 || profiles.Any(profile => profile is null || !Valid(profile)) ||
                profiles.GroupBy(profile => (profile.Format, profile.Name.ToUpperInvariant()))
                    .Any(group => group.Count() > 1))
                throw new StageException("CPRFL0001", LanguageManager.Get("ProfileInvalidFile")); //CPRFL0001
            return profiles.Where(profile => profile.Format == format)
                .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch (StageException) { throw; }
        catch (Exception error)
        {
            throw new StageException("CPRFL0002", LanguageManager.Get("ProfileLoadFailed"), error); //CPRFL0002
        }
    }

    public static async Task SaveAsync(CompressionProfile profile)
    {
        if (!Valid(profile))
            throw new StageException("CPRFL0003", LanguageManager.Get("ProfileInvalidName")); //CPRFL0003
        await Gate.WaitAsync();
        try
        {
            // Keep profiles for all formats in one file while replacing only
            // the exact format/name pair chosen by this window.
            CompressionProfile[] all = Enum.GetValues<FileDetector.FileType>()
                .Where(type => type is FileDetector.FileType.Zip or FileDetector.FileType.SevenZip or FileDetector.FileType.Rar)
                .SelectMany(type => Load(type)).ToArray();
            List<CompressionProfile> next = all.Where(item => item.Format != profile.Format ||
                !item.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            next.Add(profile with { Options = profile.Options with { Password = null } });
            if (next.Count > 100)
                throw new StageException("CPRFL0004", LanguageManager.Get("ProfileLimitReached")); //CPRFL0004
            await WriteAsync(next);
        }
        finally { Gate.Release(); }
    }

    public static async Task DeleteAsync(FileDetector.FileType format, string name)
    {
        await Gate.WaitAsync();
        try
        {
            CompressionProfile[] all = Enum.GetValues<FileDetector.FileType>()
                .Where(type => type is FileDetector.FileType.Zip or FileDetector.FileType.SevenZip or FileDetector.FileType.Rar)
                .SelectMany(type => Load(type)).ToArray();
            await WriteAsync(all.Where(item => item.Format != format ||
                !item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray());
        }
        finally { Gate.Release(); }
    }

    private static async Task WriteAsync(IReadOnlyList<CompressionProfile> profiles)
    {
        string temporary = StorePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string json = JsonSerializer.Serialize(profiles);
            await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false));
            File.Move(temporary, StorePath, overwrite: true);
        }
        catch (StageException) { throw; }
        catch (Exception error)
        {
            throw new StageException("CPRFL0005", LanguageManager.Get("ProfileSaveFailed"), error); //CPRFL0005
        }
        finally
        {
            // A failed cleanup must be visible, but must not replace the
            // original save exception with an unrelated secondary failure.
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error)
            {
                System.Diagnostics.Trace.TraceError("CPRFL0006: {0}: {1}",
                    LanguageManager.Get("ProfileCleanupFailed"), error);
            }
        }
    }

    private static bool Valid(CompressionProfile profile)
    {
        CompressionOptions? value = profile.Options;
        return value is not null &&
            profile.Format is (FileDetector.FileType.Zip or FileDetector.FileType.SevenZip or FileDetector.FileType.Rar) &&
            !string.IsNullOrWhiteSpace(profile.Name) && profile.Name.Length <= 64 &&
            !profile.Name.Any(char.IsControl) && value.Password is null &&
            value.Level is >= 0 and <= 9 &&
            (profile.Format != FileDetector.FileType.Rar || value.Level <= 5) &&
            (profile.Format != FileDetector.FileType.Zip || value.Method != "LZMA2") &&
            (profile.Format != FileDetector.FileType.SevenZip || value.Method != "Deflate") &&
            (profile.Format != FileDetector.FileType.Rar || value.Method == "Default") &&
            (profile.Format is FileDetector.FileType.SevenZip or FileDetector.FileType.Rar ||
                value.SolidMode == "Default") &&
            (profile.Format == FileDetector.FileType.Rar || value.RecoveryPercent == 0) &&
            value.DictionaryMiB is >= 1 and <= 1024 &&
            value.Threads is >= 0 and <= 128 && value.MemoryLimitMiB is >= 0 and <= 1048576 &&
            value.VolumeMiB is >= 0 and <= 1048576 && value.RecoveryPercent is >= 0 and <= 10 &&
            value.Method is "Default" or "Deflate" or "LZMA2" &&
            value.SolidMode is "Default" or "On" or "Off" &&
            (!value.SelfExtracting || profile.Format == FileDetector.FileType.SevenZip && value.VolumeMiB == 0) &&
            value.ExcludePatterns?.All(pattern =>
                !string.IsNullOrWhiteSpace(pattern) && pattern.Length <= 260 &&
                !pattern.Contains('\r') && !pattern.Contains('\n')) != false;
    }
}
