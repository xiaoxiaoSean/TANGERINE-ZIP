using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TANGERINE_ZIP.Services;

// Stage head: PRMEM
internal static class PreviewMemoryBudget
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    public static long GetLimit()
    {
        MemoryStatus status = new() { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status))
            throw new StageException("PRMEM0001", new Win32Exception(Marshal.GetLastWin32Error()).Message); //PRMEM0001
        ulong halfAvailable = status.AvailablePhysical / 2;
        // The transfer buffer, decoder and WPF dispatcher need working space
        // in addition to the extracted bytes. Do not start in a critically
        // low-memory state merely because the nominal half is nonzero.
        if (halfAvailable < 256 * 1024)
            throw new StageException("PRMEM0002", LanguageManager.Get("PreviewNoMemory")); //PRMEM0002
        return (long)Math.Min(halfAvailable, long.MaxValue);
    }
}
