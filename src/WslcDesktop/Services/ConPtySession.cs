using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WslcDesktop.Services;

/// <summary>
/// A child process attached to a Windows pseudoconsole (ConPTY), as in the Windows Terminal
/// <c>samples/ConPTY/GUIConsole</c> sample. Output is raw VT bytes for a terminal emulator; input is raw VT bytes.
/// </summary>
public sealed class ConPtySession : IDisposable
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const nint ProcThreadAttributePseudoConsole = 0x00020016;
    private const uint Infinite = 0xFFFFFFFF;
    private static readonly object StdHandleGate = new();

    private readonly object _gate = new();
    private readonly Action<byte[], int> _onOutput;
    private readonly Action<int> _onExit;
    private readonly SafeFileHandle _input;
    private readonly SafeFileHandle _output;
    private nint _console;
    private nint _process;
    private nint _attributeList;
    private bool _disposed;
    private int _processId;

    private ConPtySession(Action<byte[], int> onOutput, Action<int> onExit, SafeFileHandle input, SafeFileHandle output)
    {
        _onOutput = onOutput;
        _onExit = onExit;
        _input = input;
        _output = output;
    }

    public int ProcessId => _processId;

    /// <summary>Starts <paramref name="commandLine"/> attached to a new pseudoconsole of the given size.</summary>
    /// <exception cref="InvalidOperationException">ConPTY is unavailable or the process couldn't be created.</exception>
    public static ConPtySession Start(
        string commandLine,
        short columns,
        short rows,
        Action<byte[], int> onOutput,
        Action<int> onExit,
        string? workingDirectory = null)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            throw new InvalidOperationException("The Windows pseudoconsole (ConPTY) requires Windows 10 version 1809 or later.");
        }

        if (!CreatePipe(out var inputRead, out var inputWrite, nint.Zero, 0)
            || !CreatePipe(out var outputRead, out var outputWrite, nint.Zero, 0))
        {
            throw new InvalidOperationException("Couldn't create the terminal pipes: " + Marshal.GetLastPInvokeErrorMessage());
        }

        nint console = nint.Zero;
        var hr = CreatePseudoConsole(new Coord(columns, rows), inputRead, outputWrite, 0, out console);
        inputRead.Dispose();
        outputWrite.Dispose();
        if (hr != 0)
        {
            inputWrite.Dispose();
            outputRead.Dispose();
            throw new InvalidOperationException($"CreatePseudoConsole failed (0x{hr:X8}).");
        }

        var session = new ConPtySession(
            onOutput,
            onExit,
            inputWrite,
            outputRead)
        {
            _console = console,
        };

        try
        {
            session.Launch(commandLine, workingDirectory);
        }
        catch
        {
            session.Dispose();
            throw;
        }

        session.BeginPumping();
        return session;
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var offset = 0;
            while (offset < data.Length && WriteFile(_input, ref MemoryMarshal.GetReference(data[offset..]), data.Length - offset, out var written, nint.Zero) && written > 0)
            {
                offset += written;
            }
        }
    }

    public void Resize(short columns, short rows)
    {
        if (columns <= 0 || rows <= 0)
        {
            return;
        }

        lock (_gate)
        {
            if (!_disposed && _console != nint.Zero)
            {
                ResizePseudoConsole(_console, new Coord(columns, rows));
            }
        }
    }

    public void Dispose()
    {
        nint console;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            console = _console;
            _console = nint.Zero;
            if (_process != nint.Zero)
            {
                TerminateProcess(_process, 1);
            }
        }

        _input.Dispose();

        // ClosePseudoConsole can block until output is drained on older builds, so never do it on the caller.
        _ = Task.Run(() =>
        {
            if (console != nint.Zero)
            {
                ClosePseudoConsole(console);
            }

            _output.Dispose();
            if (_attributeList != nint.Zero)
            {
                DeleteProcThreadAttributeList(_attributeList);
                Marshal.FreeHGlobal(_attributeList);
                _attributeList = nint.Zero;
            }
        });
    }

    private void Launch(string commandLine, string? workingDirectory)
    {
        nint size = nint.Zero;
        InitializeProcThreadAttributeList(nint.Zero, 1, 0, ref size);
        _attributeList = Marshal.AllocHGlobal(size);
        if (!InitializeProcThreadAttributeList(_attributeList, 1, 0, ref size))
        {
            throw new InvalidOperationException("Couldn't initialize the process attribute list.");
        }

        if (!UpdateProcThreadAttribute(_attributeList, 0, ProcThreadAttributePseudoConsole, _console, (nint)nint.Size, nint.Zero, nint.Zero))
        {
            throw new InvalidOperationException("Couldn't attach the pseudoconsole to the process.");
        }

        var startup = new StartupInfoEx
        {
            StartupInfo = new StartupInfo { cb = Marshal.SizeOf<StartupInfoEx>() },
            lpAttributeList = _attributeList,
        };

        // A parent with redirected standard handles (a test host, a console app) would otherwise pass them on to the
        // child instead of the pseudoconsole. GUI apps have none, so this is a no-op there.
        var saved = new[] { GetStdHandle(-10), GetStdHandle(-11), GetStdHandle(-12) };
        ProcessInformation info;
        bool created;
        lock (StdHandleGate)
        {
            for (var i = 0; i < saved.Length; i++)
            {
                SetStdHandle(-10 - i, nint.Zero);
            }

            try
            {
                created = CreateProcessW(null, commandLine, nint.Zero, nint.Zero, false, ExtendedStartupInfoPresent | CreateUnicodeEnvironment, nint.Zero, workingDirectory, ref startup, out info);
            }
            finally
            {
                for (var i = 0; i < saved.Length; i++)
                {
                    SetStdHandle(-10 - i, saved[i]);
                }
            }
        }

        if (!created)
        {
            var error = Marshal.GetLastPInvokeError();
            throw error == 2 || error == 3
                ? new InvalidOperationException("The command wasn't found on PATH.")
                : new InvalidOperationException("Couldn't start the process: " + Marshal.GetPInvokeErrorMessage(error));
        }

        CloseHandle(info.hThread);
        _process = info.hProcess;
        _processId = info.dwProcessId;
    }

    private void BeginPumping()
    {
        var reader = new Thread(() =>
        {
            var buffer = new byte[16 * 1024];
            try
            {
                int read;
                while (ReadFile(_output, buffer, buffer.Length, out read, nint.Zero) && read > 0)
                {
                    _onOutput(buffer, read);
                }
            }
            catch (ObjectDisposedException)
            {
                // Pseudoconsole closed.
            }
        })
        { IsBackground = true, Name = "ConPTY output" };
        reader.Start();

        var process = _process;
        new Thread(() =>
        {
            WaitForSingleObject(process, Infinite);
            GetExitCodeProcess(process, out var code);

            // Closing the console ends the output pipe so the reader can drain and finish.
            nint console;
            lock (_gate)
            {
                console = _console;
                _console = nint.Zero;
                _process = nint.Zero;
                CloseHandle(process);
            }

            if (console != nint.Zero)
            {
                ClosePseudoConsole(console);
            }

            reader.Join(TimeSpan.FromSeconds(2));
            _onExit((int)code);
        })
        { IsBackground = true, Name = "ConPTY wait" }.Start();
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Coord(short x, short y)
    {
        public readonly short X = x;
        public readonly short Y = y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public nint lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public nint lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(SafeFileHandle file, byte[] buffer, int count, out int read, nint overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(SafeFileHandle file, ref byte buffer, int count, out int written, nint overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, nint attributes, int size);

    [DllImport("kernel32.dll")]
    private static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out nint console);

    [DllImport("kernel32.dll")]
    private static extern int ResizePseudoConsole(nint console, Coord size);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(nint console);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(nint list, int count, int flags, ref nint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(nint list, uint flags, nint attribute, nint value, nint size, nint previous, nint returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(nint list);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string? applicationName,
        string commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        nint environment,
        string? currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll")]
    private static extern nint GetStdHandle(int id);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int id, nint handle);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(nint process, out uint exitCode);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(nint process, uint exitCode);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
