using WslcDesktop.Images;

namespace WslcDesktop.Tests.Images;

public class PullProgressTrackerTests
{
    [Fact]
    public void Sums_bytes_across_layers_so_the_total_never_resets_per_layer()
    {
        var tracker = new PullProgressTracker();

        tracker.Update(new PullProgress("Downloading", "a", 40, 100));
        var overall = tracker.Update(new PullProgress("Downloading", "b", 10, 50));

        Assert.Equal(new PullProgress("Downloading", string.Empty, 50, 150), overall);
    }

    [Fact]
    public void A_completed_layer_counts_fully_and_a_late_lower_report_does_not_shrink_it()
    {
        var tracker = new PullProgressTracker();

        tracker.Update(new PullProgress("Downloading", "a", 40, 100));
        var done = tracker.Update(new PullProgress("Complete", "a", 0, 0));
        var late = tracker.Update(new PullProgress("Downloading", "a", 5, 100));

        Assert.Equal(100, done.CurrentBytes);
        Assert.Equal(100, late.CurrentBytes);
    }

    [Fact]
    public void Reports_without_a_layer_id_leave_the_totals_alone()
    {
        var tracker = new PullProgressTracker();

        tracker.Update(new PullProgress("Downloading", "a", 40, 100));
        var overall = tracker.Update(new PullProgress("Pulling", string.Empty, 0, 0));

        Assert.Equal(new PullProgress("Pulling", string.Empty, 40, 100), overall);
    }
}
