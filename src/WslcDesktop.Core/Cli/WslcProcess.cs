using WslcDesktop.Localization;
using System.Diagnostics;
using System.Text;
using WslcDesktop.Lifecycle;
using WslcDesktop.Secrets;

namespace WslcDesktop.Cli;

/// <summary>Runs the real wslc.exe.</summary>
/// <param name="executable">Executable path or command name; defaults to wslc on PATH.</param>
public sealed class WslcProcess(string executable = "wslc") : IWslcProcess
{
    private readonly string _executable = executable ?? throw new ArgumentNullException(nameof(executable));

    public static WslcProcess Default { get; } = new();

    public async Task StreamAsync(WslcInvocation invocation, Action<string> onLine, CancellationToken ct = default)
    {
        Process process;
        try
        {
            process = await Task.Run(() => ChildProcessTracker.Start(CreateStartInfo(invocation)), ct);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(UiStrings.Get("WslcExeWasnTFoundOnPATH"));
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

    private ProcessStartInfo CreateStartInfo(WslcInvocation invocation)
    {
        var info = new ProcessStartInfo(_executable)
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
        var info = new ProcessStartInfo(_executable)
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
            using var process = await Task.Run(() => ChildProcessTracker.Start(info), linked.Token).ConfigureAwait(false);
            using var registration = linked.Token.Register(() => ChildProcessTracker.Kill(process));
            var stdout = process.StandardOutput.ReadToEndAsync(linked.Token);
            var stderr = process.StandardError.ReadToEndAsync(linked.Token);
            var completion = Task.WhenAll(stdout, stderr, process.WaitForExitAsync());
            try
            {
                if (invocation.StandardInput is not null)
                {
                    await process.StandardInput.WriteAsync(invocation.StandardInput.AsMemory(), linked.Token).ConfigureAwait(false);
                    await process.StandardInput.FlushAsync(linked.Token).ConfigureAwait(false);
                    process.StandardInput.Close();
                }

                await completion.ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
            }
            catch (Exception error)
            {
                ChildProcessTracker.Kill(process);
                try
                {
                    await completion.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    // Cancelled drains are expected; the original failure is rethrown below.
                }
                catch (Exception cleanupError)
                {
                    if (!ReferenceEquals(error, cleanupError))
                    {
                        error.Data["ProcessCleanupException"] = cleanupError;
                    }
                }

                // Killing a process can break its stdin pipe before the write observes cancellation.
                if (linked.IsCancellationRequested && error is IOException)
                {
                    throw new OperationCanceledException(UiStrings.Get("WslcWasCancelled"), error, linked.Token);
                }

                throw;
            }

            if (process.ExitCode != 0)
            {
                var err = (await stderr.ConfigureAwait(false)).Trim();
                var outText = (await stdout.ConfigureAwait(false)).Trim();
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
                throw new InvalidOperationException(message.Length > 0 ? SecretService.Default.Redact(message) : UiStrings.Format("WslcExitedWith", process.ExitCode));
            }

            return await stdout.ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(UiStrings.Get("WslcExeWasnTFoundOnPATHItS"));
        }
        catch (OperationCanceledException) when (timeout?.IsCancellationRequested == true && !ct.IsCancellationRequested)
        {
            throw new InvalidOperationException(UiStrings.Get("WslcDidnTRespondInTime"));
        }
    }
}
