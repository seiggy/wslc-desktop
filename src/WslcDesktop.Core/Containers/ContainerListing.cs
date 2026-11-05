
using WslcDesktop.Compose.Execution;
using WslcDesktop.Sessions;

namespace WslcDesktop.Containers;

public static class ContainerListing
{
    /// <summary>
    /// Orders containers by session (app session first, as the catalog returns them), standalone containers before
    /// stacks, and stacks by name; the first container of each stack carries the group heading.
    /// </summary>
    public static IReadOnlyList<ContainerEntry> Build(SessionSnapshot<ContainerRow> snapshot, string? sessionFilter)
    {
        var ordered = snapshot.Items
            .Where(e => sessionFilter is null || string.Equals(e.Session, sessionFilter, StringComparison.OrdinalIgnoreCase))
            .Select(e => (Entry: e, Stack: ComposeLabels.TryParse(e.Item.Labels, out var p, out _) ? p : string.Empty))
            .OrderBy(x => snapshot.Sessions.ToList().FindIndex(s => string.Equals(s, x.Entry.Session, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(x => x.Stack.Length > 0)
            .ThenBy(x => x.Stack, StringComparer.Ordinal)
            .ToList();

        var result = new List<ContainerEntry>(ordered.Count);
        string? previous = null;
        foreach (var (entry, stack) in ordered)
        {
            var group = stack.Length > 0 ? $"{entry.Session}|{stack}" : null;
            var header = group is not null && group != previous
                ? $"Stack: {stack} ({ordered.Count(x => x.Stack == stack && x.Entry.Session == entry.Session)})"
                : string.Empty;
            previous = group;
            result.Add(new ContainerEntry(entry.Session, entry.IsThisApp, entry.Item, stack, header));
        }

        return result;
    }
}
