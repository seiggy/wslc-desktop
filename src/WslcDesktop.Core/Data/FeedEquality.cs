using WslcDesktop.Resources;

namespace WslcDesktop.Data;

/// <summary>Value equality for feed contents, so a poll that returns the same data doesn't count as a change.</summary>
public static class FeedEquality
{
    public static bool Sequence<TItem>(IReadOnlyList<TItem> a, IReadOnlyList<TItem> b) =>
        a.Count == b.Count && a.SequenceEqual(b);

    // ResourceRow is a record, but its UsedBy list compares by reference.
    public static bool Resources(IReadOnlyList<ResourceRow> a, IReadOnlyList<ResourceRow> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First with { UsedBy = [] } == p.Second with { UsedBy = [] } && p.First.UsedBy.SequenceEqual(p.Second.UsedBy));
}
