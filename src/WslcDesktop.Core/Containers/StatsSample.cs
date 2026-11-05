namespace WslcDesktop.Containers;

public sealed record StatsSample(
    DateTimeOffset Time,
    double CpuPercent,
    double MemoryPercent,
    ulong MemoryBytes,
    ulong MemoryLimitBytes,
    ulong NetRxBytes,
    ulong NetTxBytes,
    ulong BlockReadBytes,
    ulong BlockWriteBytes,
    int Pids);
