using System.Diagnostics;
using System.Text.Json;

namespace WslcDesktop.Services;

public sealed record SessionRow(string Id, string CreatorPid, string Name);

/// <summary>
/// The Microsoft.WSL.Containers API cannot enumerate containers or sessions, but wslc.exe can.
/// Discovery goes through the CLI; every mutation still uses the API.
/// </summary>
public static class WslcCli
{
    public static async Task<IReadOnlyList<ContainerRow>> ListContainersAsync(string session)
    {
        var output = await RunAsync("--session", session, "list", "--all", "--no-trunc", "--format", "json");
        var rows = new List<ContainerRow>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith('{'))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            rows.Add(new ContainerRow(
                Str(root, "Names"),
                Str(root, "Image"),
                Str(root, "ID"),
                Str(root, "State"),
                Str(root, "Command").Trim('"'),
                Str(root, "Ports")));
        }

        return rows;
    }

    public static async Task<IReadOnlyList<SessionRow>> ListSessionsAsync()
    {
        var output = await RunAsync("system", "session", "list");
        var rows = new List<SessionRow>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1))
        {
            var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 3)
            {
                rows.Add(new SessionRow(parts[0], parts[1], parts[2]));
            }
        }

        return rows;
    }

    /// <summary>Snapshot of resource usage for every running container in the session.</summary>
    public static async Task<IReadOnlyDictionary<string, StatsSample>> GetStatsAsync(string session)
    {
        var output = await RunAsync("--session", session, "stats", "--format", "json", "--no-trunc");
        var result = new Dictionary<string, StatsSample>();
        var now = DateTimeOffset.Now;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith('{'))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var (memUsed, memLimit) = ParsePair(Str(root, "MemUsage"));
            var (netRx, netTx) = ParsePair(Str(root, "NetIO"));
            var (blockRead, blockWrite) = ParsePair(Str(root, "BlockIO"));
            var pids = root.TryGetProperty("PIDs", out var p) && p.TryGetInt32(out var n) ? n : 0;
            result[Str(root, "Name")] = new StatsSample(
                now, ParsePercent(Str(root, "CPUPerc")), ParsePercent(Str(root, "MemPerc")),
                memUsed, memLimit, netRx, netTx, blockRead, blockWrite, pids);
        }

        return result;
    }

    /// <summary>Streams container logs line by line until the process ends or <paramref name="ct"/> is cancelled.</summary>
    public static Task StreamLogsAsync(string session, string container, int tail, bool follow, bool timestamps,
        Action<string> onLine, CancellationToken ct)
    {
        var args = new List<string> { "--session", session, "logs", "--tail", tail.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if (follow)
        {
            args.Add("--follow");
        }

        if (timestamps)
        {
            args.Add("--timestamps");
        }

        args.Add(container);
        return StreamAsync(args, onLine, ct);
    }

    /// <summary>Starts an interactive (piped, non-TTY) shell inside a running container.</summary>
    public static ShellProcess StartShell(string session, string container, string shell, Action<string> onOutput, Action<int> onExit)
    {
        var info = CreateStartInfo("--session", session, "exec", "--interactive", container, shell);
        info.RedirectStandardInput = true;
        return new ShellProcess(info, onOutput, onExit);
    }

    internal static ProcessStartInfo CreateStartInfo(params string[] args)
    {
        var info = new ProcessStartInfo("wslc")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }

        return info;
    }

    private static async Task StreamAsync(IEnumerable<string> args, Action<string> onLine, CancellationToken ct)
    {
        var info = CreateStartInfo(args.ToArray());
        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException("Couldn't start wslc.exe.");
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

    private static double ParsePercent(string s) =>
        double.TryParse(s.Trim().TrimEnd('%'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

    // "1.047MiB / 15.62GiB" -> (bytes, bytes)
    private static (ulong, ulong) ParsePair(string s)
    {
        var parts = s.Split('/', 2, StringSplitOptions.TrimEntries);
        return (ParseBytes(parts[0]), parts.Length > 1 ? ParseBytes(parts[1]) : 0);
    }

    private static ulong ParseBytes(string s)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s.Trim(), @"^([\d.]+)\s*([A-Za-z]*)$");
        if (!m.Success || !double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return 0;
        }

        var factor = m.Groups[2].Value.ToLowerInvariant() switch
        {
            "kb" => 1e3, "mb" => 1e6, "gb" => 1e9, "tb" => 1e12,
            "kib" => 1024.0, "mib" => 1024.0 * 1024, "gib" => Math.Pow(1024, 3), "tib" => Math.Pow(1024, 4),
            _ => 1.0,
        };
        return (ulong)(value * factor);
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static async Task<string> RunAsync(params string[] args)
    {
        var info = new ProcessStartInfo("wslc")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }

        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Couldn't start wslc.exe.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException((await stderr).Trim() is { Length: > 0 } m ? m : $"wslc exited with {process.ExitCode}.");
            }

            return await stdout;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("wslc.exe wasn't found on PATH. It's needed to list containers and sessions.");
        }
    }
}
