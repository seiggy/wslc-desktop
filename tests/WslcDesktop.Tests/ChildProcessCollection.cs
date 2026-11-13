namespace WslcDesktop.Tests;

/// <summary>
/// Tests that start real processes and wait on them with deadlines. They run alone, not alongside other test
/// classes, so a busy CI runner can't starve a child's startup past those deadlines.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ChildProcessCollection
{
    public const string Name = "Child processes";
}
