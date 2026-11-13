namespace WslcDesktop.Images;

/// <summary>
/// The SDK reports progress per layer, so a raw bar jumps back to zero for every layer. This folds the layer reports
/// into one overall figure: bytes summed across every layer seen so far.
/// </summary>
public sealed class PullProgressTracker
{
    private const string CompleteStatus = "Complete";

    private readonly Dictionary<string, (long Current, long Total)> _layers = new(StringComparer.Ordinal);

    public PullProgress Update(PullProgress layer)
    {
        var (current, total) = (Math.Max(layer.CurrentBytes, 0), Math.Max(layer.TotalBytes, 0));
        if (layer.Id.Length > 0)
        {
            // A finished layer counts fully even if its last report carried no byte counts.
            _layers.TryGetValue(layer.Id, out var known);
            total = Math.Max(total, known.Total);
            current = layer.Status == CompleteStatus ? total : Math.Max(current, known.Current);
            _layers[layer.Id] = (Math.Min(current, total > 0 ? total : current), total);
        }

        return new PullProgress(layer.Status, string.Empty, _layers.Values.Sum(l => l.Current), _layers.Values.Sum(l => l.Total));
    }
}
