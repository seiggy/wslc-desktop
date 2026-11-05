using System.Diagnostics;
using System.Text;
using WslcDesktop.Lifecycle;
using WslcDesktop.Secrets;

namespace WslcDesktop.Cli;

/// <summary>Runs the real wslc.exe.</summary>
public sealed class WslcProcess : IWslcProcess
{
    public static WslcProcess Default { get; } = new();

    public async Task StreamAsync(WslcInvocation invocation, Action<string> onLine, CancellationToken ct = default)
    {
        Process process;
        try
        {
            process = ChildProcessTracker.Start(CreateStartInfo(invocation));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("wslc.exe wasn't found on PATH.");
        }

        using (process)
        {
            using var registration = ct.Register(() =>
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Already exited.
                }
            });

            async Task Pump(StreamReader reader)
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    onLine(line);
                }
            }

            await Task.WhenAll(Pump(process.StandardOutput), Pump(process.StandardError));
            await process.WaitForExitAsync();
        }
    }

    public IShellProcess StartInteractive(WslcInvocation invocation, Action<string> onOutput, Action<int> onExit)
    {
        var info = CreateStartInfo(invocation);
        info.RedirectStandardInput = true;
        return new ShellProcess(info, onOutput, onExit);
    }

    private static ProcessStartInfo CreateStartInfo(WslcInvocation invocation)
    {
        var info = new ProcessStartInfo("wslc")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in invocation.Args)
        {
            info.ArgumentList.Add(a);
        }

        foreach (var (key, value) in invocation.Environment ?? new Dictionary<string, string>())
        {
            info.Environment[key] = value;
        }

        return info;
    }

    public async Task<string> RunAsync(WslcInvocation invocation, CancellationToken ct = default)
    {
        var info = new ProcessStartInfo("wslc")
        {
            RedirectStandardInput = invocation.StandardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in invocation.Args)
        {
            info.ArgumentList.Add(a);
        }

        foreach (var (key, value) in invocation.Environment ?? new Dictionary<string, string>())
        {
            info.Environment[key] = value;
        }

        using var timeout = invocation.Timeout is { } limit ? new CancellationTokenSource(limit) : null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout?.Token ?? CancellationToken.None);
        try
        {
            using var process = ChildProcessTracker.Start(info);
            if (invocation.StandardInput is not null)
            {
                await process.StandardInput.WriteAsync(invocation.StandardInput);
                process.StandardInput.Close();
            }

            var stdout = process.StandardOutput.ReadToEndAsync(linked.Token);
            var stderr = process.StandardError.ReadToEndAsync(linked.Token);
            using var registration = linked.Token.Register(() => ChildProcessTracker.Kill(process));
            await process.WaitForExitAsync(linked.Token);
            if (process.ExitCode != 0)
            {
                var err = (await stderr).Trim();
                var outText = (await stdout).Trim();
                var text = err.Length > 0 ? err : outText;
                if (!string.IsNullOrEmpty(invocation.StandardInput))
                {
                    text = text.Replace(invocation.StandardInput, "••••");
                }

                var message = string.Join(
                    '\n',
                    text.Split('\n')
                        .Select(l => l.TrimEnd())
                        .Where(l => l.Length > 0
                            && !l.StartsWith("If this error", StringComparison.Ordinal)
                            && !l.StartsWith("https://github.com/microsoft/WSL", StringComparison.Ordinal)));
                throw new InvalidOperationException(message.Length > 0 ? SecretService.Default.Redact(message) : $"wslc exited with {process.ExitCode}.");
            }

            return await stdout;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("wslc.exe wasn't found on PATH. It's needed to list containers and sessions.");
        }
        catch (OperationCanceledException) when (timeout?.IsCancellationRequested == true && !ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("wslc didn't respond in time.");
        }
    }
}
