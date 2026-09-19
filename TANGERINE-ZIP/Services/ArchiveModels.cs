using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

internal sealed record ArchiveEntryInfo(string Key, bool IsDirectory, long Size);

internal sealed record ArchiveProgress(int Percentage, string EntryKey);

internal enum OverwritePolicy
{
    Ask,
    OverwriteAll,
    SkipAll,
    Cancel
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

    public static bool CanCreate(FileDetector.FileType type) =>
        CanOpen(type) && type != FileDetector.FileType.Rar;

    public static bool IsSingleFileStream(FileDetector.FileType type) => type is
        FileDetector.FileType.GZip or FileDetector.FileType.BZip2 or
        FileDetector.FileType.Xz or FileDetector.FileType.Lz4 or
        FileDetector.FileType.Zstd;
}
