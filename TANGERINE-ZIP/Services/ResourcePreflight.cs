using System.Runtime.InteropServices;

namespace TANGERINE_ZIP.Services;

internal static class ResourcePreflight
{
    public static void ForCompression(IReadOnlyList<string> sources, string output, CompressionOptions options)
    {
        long inputBytes = 0;
        foreach (string source in sources)
        {
            if (File.Exists(source)) inputBytes = SaturatingAdd(inputBytes, new FileInfo(source).Length);
            else if (Directory.Exists(source))
                foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                    inputBytes = SaturatingAdd(inputBytes, new FileInfo(file).Length);
            else throw new FileNotFoundException(source);
        }
        Check(output, Math.Min(inputBytes, long.MaxValue - 64 * 1024 * 1024) + 64 * 1024 * 1024,
            options.MemoryLimitMiB > 0 ? (long)options.MemoryLimitMiB * 1024 * 1024 : 128L * 1024 * 1024);
    }

    public static void ForExtraction(string archive, string destination, IReadOnlyList<ArchiveEntryInfo> entries)
    {
        long bytes = 0;
        foreach (ArchiveEntryInfo entry in entries.Where(e => !e.IsDirectory)) bytes = SaturatingAdd(bytes, Math.Max(0, entry.Size));
        // Archive metadata is not always trustworthy. Leave headroom before the first write;
        // per-file checks are also performed during extraction.
        Check(destination, SaturatingAdd(bytes, 64L * 1024 * 1024), 128L * 1024 * 1024);
    }

    public static void Check(string path, long requiredDiskBytes, long requiredMemoryBytes)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full) ?? throw new IOException(LanguageManager.Get("PreflightDiskUnknown"));
        DriveInfo drive = new(root);
        if (!drive.IsReady || drive.AvailableFreeSpace < requiredDiskBytes)
            throw new StageException("RSRCE0001", string.Format(LanguageManager.Get("PreflightDiskInsufficient"),
                FormatBytes(requiredDiskBytes), FormatBytes(drive.IsReady ? drive.AvailableFreeSpace : 0)));
        MEMORYSTATUSEX status = new() { Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        long available = GlobalMemoryStatusEx(ref status)
            ? (long)Math.Min(status.AvailablePhysical, (ulong)long.MaxValue)
            : GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (available > 0 && requiredMemoryBytes > available)
            throw new StageException("RSRCE0002", string.Format(LanguageManager.Get("PreflightMemoryInsufficient"),
                FormatBytes(requiredMemoryBytes), FormatBytes(available)));
    }

    public static string FormatBytes(long size)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double value = Math.Max(0, size);
        int index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return $"{value:0.##} {units[index]}";
    }

    private static long SaturatingAdd(long left, long right) => left > long.MaxValue - right ? long.MaxValue : left + right;

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
        public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
}
