using System.Diagnostics;
using System.Text;

namespace WslcDesktop.Services;

/// <summary>Registry login/logout through `wslc registry`. Passwords go over stdin and are never stored or logged.</summary>
public static class WslcRegistry
{
    public static async Task LoginAsync(string session, string server, string username, string password)
    {
        var args = new List<string> { "--session", session, "registry", "login", "--username", username, "--password-stdin" };
        if (server.Length > 0)
        {
            args.Add(server);
        }

        await RunAsync(args, password, password);
    }

    public static async Task LogoutAsync(string session, string server)
    {
        var args = new List<string> { "--session", session, "registry", "logout" };
        if (server.Length > 0)
        {
            args.Add(server);
        }

        await RunAsync(args, null, null);
    }

    private static async Task RunAsync(List<string> args, string? stdin, string? secret)
    {
        var info = new ProcessStartInfo("wslc.exe")
        {
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }

        try
        {
            using var process = ChildProcessTracker.Start(info);
            if (stdin is not null)
            {
                await process.StandardInput.WriteAsync(stdin);
                process.StandardInput.Close();
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await process.WaitForExitAsync(cts.Token);
            if (process.ExitCode != 0)
            {
                var text = (await stderr).Trim();
                if (text.Length == 0)
                {
                    text = (await stdout).Trim();
                }

                if (!string.IsNullOrEmpty(secret))
                {
                    text = text.Replace(secret, "••••");
                }

                var lines = text.Split('\n').Select(l => l.TrimEnd())
                    .Where(l => l.Length > 0 && !l.StartsWith("If this error", StringComparison.Ordinal)
                        && !l.StartsWith("https://github.com/microsoft/WSL", StringComparison.Ordinal));
                var message = string.Join('\n', lines);
                throw new InvalidOperationException(message.Length > 0 ? message : $"wslc exited with {process.ExitCode}.");
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("wslc.exe wasn't found on PATH.");
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("wslc didn't respond in time.");
        }
    }
}
