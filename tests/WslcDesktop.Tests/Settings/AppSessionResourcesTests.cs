using WslcDesktop.Settings;

namespace WslcDesktop.Tests.Settings;

public class AppSessionResourcesTests
{
    [Fact]
    public void Valid_values_are_applied()
    {
        var settings = new AppSettings();

        var applied = settings.TryApplySessionResources(2, 8192, gpu: true, out var problem);

        Assert.True(applied);
        Assert.Null(problem);
        Assert.Equal(2u, settings.CpuCount);
        Assert.Equal(8192u, settings.MemoryMb);
        Assert.True(settings.EnableGpu);
    }

    [Fact]
    public void Cpu_count_is_clamped_between_one_and_the_machines_processors()
    {
        var settings = new AppSettings();

        settings.TryApplySessionResources(0, 4096, false, out _);
        Assert.Equal(1u, settings.CpuCount);

        settings.TryApplySessionResources(10_000, 4096, false, out _);
        Assert.Equal((uint)Environment.ProcessorCount, settings.CpuCount);
    }

    [Fact]
    public void Memory_never_drops_below_512_mb()
    {
        var settings = new AppSettings();

        settings.TryApplySessionResources(1, 64, false, out _);

        Assert.Equal(512u, settings.MemoryMb);
    }

    [Theory]
    [InlineData(double.NaN, 4096)]
    [InlineData(2, double.NaN)]
    public void Non_numbers_are_rejected_and_nothing_changes(double cpu, double memory)
    {
        var settings = new AppSettings { CpuCount = 3, MemoryMb = 2048, EnableGpu = false };

        var applied = settings.TryApplySessionResources(cpu, memory, gpu: true, out var problem);

        Assert.False(applied);
        Assert.Equal("CPU count and memory must be numbers.", problem);
        Assert.Equal(3u, settings.CpuCount);
        Assert.Equal(2048u, settings.MemoryMb);
        Assert.False(settings.EnableGpu);
    }

    [Fact]
    public void Reset_restores_the_session_defaults()
    {
        var settings = new AppSettings { CpuCount = 1, MemoryMb = 9999, EnableGpu = true };

        settings.ResetSessionResources();

        Assert.Equal(AppSettings.DefaultCpuCount, settings.CpuCount);
        Assert.Equal(AppSettings.DefaultMemoryMb, settings.MemoryMb);
        Assert.False(settings.EnableGpu);
    }
}
