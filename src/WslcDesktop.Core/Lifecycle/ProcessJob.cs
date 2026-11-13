using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WslcDesktop.Lifecycle;

/// <summary>
/// A Windows job object holding one child process and everything it starts. Terminating the job ends the whole tree
/// without the full process/thread snapshot <see cref="Process.Kill(bool)"/> takes, and closing the handle (including
/// when this app crashes) ends anything still in it.
/// </summary>
internal sealed class ProcessJob : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;

    private readonly JobHandle _handle;

    private ProcessJob(JobHandle handle) => _handle = handle;

    /// <summary>Puts <paramref name="process"/> in a new job, or returns null when the OS refuses (the caller falls back to a tree kill).</summary>
    public static ProcessJob? TryAssign(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var handle = CreateJobObjectW(nint.Zero, null);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        var limits = new ExtendedLimitInformation { BasicLimitInformation = { LimitFlags = KillOnJobClose } };
        if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, ref limits, Marshal.SizeOf<ExtendedLimitInformation>())
            || !AssignProcessToJobObject(handle, process.Handle))
        {
            handle.Dispose();
            return null;
        }

        return new ProcessJob(handle);
    }

    public bool Terminate() => !_handle.IsClosed && TerminateJobObject(_handle, 1);

    public void Dispose() => _handle.Dispose();

    private sealed class JobHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern JobHandle CreateJobObjectW(nint attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(JobHandle job, int infoClass, ref ExtendedLimitInformation info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(JobHandle job, nint process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(JobHandle job, uint exitCode);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
