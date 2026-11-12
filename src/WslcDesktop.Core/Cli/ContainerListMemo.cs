using System.Collections.Concurrent;
using WslcDesktop.Containers;

namespace WslcDesktop.Cli;

/// <summary>
/// Reuses the parsed rows when a session's <c>wslc list</c> output is unchanged, which it is for most polls, so the
/// split and per-line JSON parse only run when something actually changed.
/// </summary>
public sealed class ContainerListMemo
{
    private readonly ConcurrentDictionary<string, (string Output, IReadOnlyList<ContainerRow> Rows)> _last = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ContainerRow> Parse(string session, string output)
    {
        if (_last.TryGetValue(session, out var last) && string.Equals(last.Output, output, StringComparison.Ordinal))
        {
            return last.Rows;
        }

        var rows = WslcCli.ParseContainers(output);
        _last[session] = (output, rows);
        return rows;
    }
}
