using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

internal sealed record ArchiveEntryInfo(string Key, bool IsDirectory, long Size);

internal sealed record ArchiveProgress(int Percentage, string EntryKey);

// Buffer may have spare capacity; Length is the exact decompressed byte count.
internal sealed record PreviewPayload(string EntryKey, byte[] Buffer, int Length);

internal sealed record NestedTarInfo(IReadOnlyList<string> TarEntryKeys, bool FlattenAutomatically)
{
    public bool HasNestedTar => TarEntryKeys.Count > 0;

    public static NestedTarInfo None { get; } = new([], false);
}

internal enum OverwritePolicy
{
    Ask,
    OverwriteAll,
    SkipAll,
    Cancel
}

internal enum ConflictChoice
{
    ThisYes,
    ThisNo,
    AllYes,
    AllNo,
    Cancel
}

internal sealed record ArchiveConflict(string TargetPath, string EntryKey);

internal sealed class ConflictResolutionState
{
    private readonly Func<ArchiveConflict, ConflictChoice> _resolver;
    private bool? _overwriteAll;

    public ConflictResolutionState(Func<ArchiveConflict, ConflictChoice> resolver) => _resolver = resolver;

    public bool ShouldOverwrite(string targetPath, string entryKey, OverwritePolicy fallbackPolicy)
    {
        if (!File.Exists(targetPath)) return true;
        if (_overwriteAll.HasValue) return _overwriteAll.Value;
        if (fallbackPolicy == OverwritePolicy.OverwriteAll) return true;
        if (fallbackPolicy == OverwritePolicy.SkipAll) return false;

        ConflictChoice choice = _resolver(new ArchiveConflict(targetPath, entryKey));
        return choice switch
        {
            ConflictChoice.ThisYes => true,
            ConflictChoice.ThisNo => false,
            ConflictChoice.AllYes => SetAll(true),
            ConflictChoice.AllNo => SetAll(false),
            ConflictChoice.Cancel => throw new OperationCanceledException(),
            _ => throw new StageException("CNFLT0001", LanguageManager.Get("InvalidConflictChoice")) //CNFLT0001
        };
    }

    private bool SetAll(bool overwrite)
    {
        _overwriteAll = overwrite;
        return overwrite;
    }
}

internal sealed class StageException : Exception
{
    public StageException(string stageCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StageCode = stageCode;
    }

    public string StageCode { get; }
}

internal static class ArchiveCapabilities
{
    public static bool CanOpen(FileDetector.FileType type) => type is
        FileDetector.FileType.Zip or FileDetector.FileType.Rar or
        FileDetector.FileType.SevenZip or FileDetector.FileType.Tar or
        FileDetector.FileType.GZip or FileDetector.FileType.BZip2 or
        FileDetector.FileType.Xz or FileDetector.FileType.Lz4 or
        FileDetector.FileType.Zstd or FileDetector.FileType.Iso or
        FileDetector.FileType.Wim;

    public static bool CanCreate(FileDetector.FileType type) => CanOpen(type);

    // Only these archive standards define interoperable password protection.
    public static bool CanCreateWithPassword(FileDetector.FileType type) => type is
        FileDetector.FileType.Zip or FileDetector.FileType.Rar or FileDetector.FileType.SevenZip;

    public static bool IsSingleFileStream(FileDetector.FileType type) => type is
        FileDetector.FileType.GZip or FileDetector.FileType.BZip2 or
        FileDetector.FileType.Xz or FileDetector.FileType.Lz4 or
        FileDetector.FileType.Zstd;
}
