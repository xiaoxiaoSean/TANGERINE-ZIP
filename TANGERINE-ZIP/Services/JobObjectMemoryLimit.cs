using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TANGERINE_ZIP.Services;

// 7-Zip 23.01 lacks a memory-use switch. Keep its process in a Windows job so a
// user-supplied limit is enforced by the OS for the entire compression task.
internal sealed class JobObjectMemoryLimit : IDisposable
{
    private IntPtr _handle;
    private JobObjectMemoryLimit(IntPtr handle) => _handle = handle;

    public static JobObjectMemoryLimit Attach(Process process, long bytes)
    {
        IntPtr handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            JOBOBJECT_EXTENDED_LIMIT_INFORMATION information = new();
            information.BasicLimitInformation.LimitFlags = 0x100; // JOB_OBJECT_LIMIT_PROCESS_MEMORY
            information.ProcessMemoryLimit = (UIntPtr)(ulong)bytes;
            if (!SetInformationJobObject(handle, 9, ref information,
                    (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()) ||
                !AssignProcessToJobObject(handle, process.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return new JobObjectMemoryLimit(handle);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            CloseHandle(handle);
            throw;
        }
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero) { CloseHandle(_handle); _handle = IntPtr.Zero; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int informationClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
