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
