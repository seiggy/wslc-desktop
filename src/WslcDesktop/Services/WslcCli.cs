using System.Diagnostics;
using System.Text.Json;

namespace WslcDesktop.Services;

public sealed record SessionRow(string Id, string CreatorPid, string Name);

public sealed record SessionInfo(
    string Id, string Name, string Creator, string CreatorPid, string Containers, string Images, bool IsThisApp)
{
    public bool CanTerminate => !IsThisApp;

    public string CreatorText => $"{Creator} ({CreatorPid})";

    public Microsoft.UI.Xaml.Visibility ThisAppVisibility =>
        IsThisApp ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
}

public sealed record EnvironmentInfo(string Wsl, string Kernel, string SessionManager, IReadOnlyList<SessionInfo> Sessions);

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

    public static async Task<EnvironmentInfo> GetEnvironmentAsync(string appSession)
    {
        var info = await RunAsync("info");
        string Version(string key) => info.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            .Select(l => l[(l.IndexOf(':') + 1)..].Trim())
            .FirstOrDefault() ?? string.Empty;

        var sessions = await ListSessionsAsync();
        var items = await Task.WhenAll(sessions.Select(async s =>
        {
            string creator;
            try
            {
                creator = System.Diagnostics.Process.GetProcessById(int.Parse(s.CreatorPid, System.Globalization.CultureInfo.InvariantCulture)).ProcessName;
            }
            catch
            {
                creator = "Exited";
            }

            string containers = "–", images = "–";
            try
            {
                var c = await ListContainersAsync(s.Name);
                containers = $"{c.Count(x => x.State.Equals("running", StringComparison.OrdinalIgnoreCase))} / {c.Count}";
                images = (await ListImagesAsync(s.Name)).Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
            }

            return new SessionInfo(s.Id, s.Name, creator, s.CreatorPid, containers, images,
                s.Name.Equals(appSession, StringComparison.OrdinalIgnoreCase));
        }));

        return new EnvironmentInfo(Version("WSL version"), Version("Kernel version"), Version("Session manager version"), items);
    }

    public static Task TerminateSessionAsync(string session) =>
        RunAsync("--session", session, "system", "session", "terminate");

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

    public static async Task<IReadOnlyList<ImageSummary>> ListImagesAsync(string session)
    {
        var output = await RunAsync("--session", session, "images", "--no-trunc", "--format", "json");
        var rows = new List<ImageSummary>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith('{'))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            rows.Add(new ImageSummary(
                Str(root, "Repository"), Str(root, "Tag"), Str(root, "ID"), Str(root, "Digest"),
                Str(root, "Size"), Str(root, "CreatedSince"),
                int.TryParse(Str(root, "Containers"), out var n) ? n : 0));
        }

        return rows;
    }

    public static async Task<ImageDetail> InspectImageAsync(string session, string target)
    {
        var output = await RunAsync("--session", session, "image", "inspect", target);
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
            ? doc.RootElement[0]
            : throw new InvalidOperationException($"Image '{target}' was not found.");

        var config = root.TryGetProperty("Config", out var c) ? c : default;
        var layers = root.TryGetProperty("RootFS", out var fs) ? Strings(fs, "Layers") : [];
        var ports = config.ValueKind == JsonValueKind.Object && config.TryGetProperty("ExposedPorts", out var p)
            && p.ValueKind == JsonValueKind.Object
            ? p.EnumerateObject().Select(x => x.Name).ToList()
            : [];
        var labels = config.ValueKind == JsonValueKind.Object && config.TryGetProperty("Labels", out var l)
            && l.ValueKind == JsonValueKind.Object
            ? l.EnumerateObject().Select(x => $"{x.Name}={x.Value}").ToList()
            : [];
        var size = root.TryGetProperty("Size", out var s) && s.TryGetUInt64(out var bytes) ? WslcHost.FormatSize(bytes) : string.Empty;

        return new ImageDetail(
            Str(root, "Id"), Strings(root, "RepoTags"), Strings(root, "RepoDigests"),
            Str(root, "Created"), size, Str(root, "Os"), Str(root, "Architecture"),
            Str(root, "Author"), Str(root, "Comment"),
            Str(config, "User"), Str(config, "WorkingDir"),
            Strings(config, "Entrypoint"), Strings(config, "Cmd"), Strings(config, "Env"),
            ports, labels, layers, output);
    }

    private static List<string> Strings(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
            : [];

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    internal static async Task<string> RunAsync(params string[] args)
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
                var err = (await stderr).Trim();
                var outText = (await stdout).Trim();
                var message = string.Join(
                    '\n',
                    (err.Length > 0 ? err : outText).Split('\n')
                        .Select(l => l.TrimEnd())
                        .Where(l => l.Length > 0
                            && !l.StartsWith("If this error was unexpected", StringComparison.Ordinal)
                            && !l.StartsWith("https://github.com/microsoft/WSL/issues", StringComparison.Ordinal)));
                throw new InvalidOperationException(message.Length > 0 ? message : $"wslc exited with {process.ExitCode}.");
            }

            return await stdout;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("wslc.exe wasn't found on PATH. It's needed to list containers and sessions.");
        }
    }
}
