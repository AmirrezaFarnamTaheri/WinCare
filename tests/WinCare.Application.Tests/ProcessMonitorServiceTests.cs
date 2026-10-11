using WinCare.Application.Diagnostics;
using Xunit;

namespace WinCare.Application.Tests;

public sealed class ProcessMonitorServiceTests
{
    [Fact]
    public void Cpu_usage_is_normalized_to_logical_processor_capacity()
    {
        double? usage = ProcessMonitorService.CalculateCpuPercent(
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(1), 4);
        Assert.Equal(25d, usage);
        Assert.Null(ProcessMonitorService.CalculateCpuPercent(
            TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), 4));
        Assert.Null(ProcessMonitorService.CalculateCpuPercent(
            TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.Zero, 4));
        Assert.Null(ProcessMonitorService.CalculateCpuPercent(
            TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 0));
    }

    [Theory]
    [InlineData("svchost")]
    [InlineData("lsass")]
    [InlineData("winlogon")]
    [InlineData("explorer")]
    public void Core_windows_processes_cannot_be_ended_from_monitor(string name)
    {
        var sample = new MonitoredProcess(600, name, 1024, null, DateTimeOffset.UtcNow);
        Assert.False(ProcessMonitorService.CanEnd(sample));
        Assert.False(ProcessMonitorService.EndTask(sample).Succeeded);
    }

    [Fact]
    public void Current_and_unidentified_processes_cannot_be_ended()
    {
        Assert.False(ProcessMonitorService.CanEnd(new(
            Environment.ProcessId, "app", 10, 2, DateTimeOffset.UtcNow)));
        Assert.False(ProcessMonitorService.CanEnd(new(100, "unknown", 10, 2, null)));
        Assert.False(ProcessMonitorService.CanEnd(new(4, "worker", 10, 2, DateTimeOffset.UtcNow)));
    }
}
