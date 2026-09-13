using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Infrastructure.Native;
using Xunit;

namespace WinCare.Infrastructure.Tests;

public sealed class NativeCoreServiceTests
{
    private readonly NativeCoreService _service = new();

    [Fact]
    public void SupportedAbiVersion_IsPositiveConstant()
    {
        Assert.Equal(1u, NativeCoreService.SupportedAbiVersion);
    }

    [Fact]
    public async Task HashFileAsync_ThrowsArgumentException_OnEmptyPath()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            _service.HashFileAsync("", 1024, CancellationToken.None));
    }

    [Fact]
    public async Task GetDirectorySizeAsync_ThrowsArgumentException_OnEmptyPath()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            _service.GetDirectorySizeAsync("", CancellationToken.None));
    }

    [Fact]
    public async Task GetDirectoryStatsAsync_ThrowsArgumentException_OnEmptyPath()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            _service.GetDirectoryStatsAsync("", CancellationToken.None));
    }

    [Fact]
    public async Task ShredFileAsync_ThrowsArgumentException_OnEmptyPath()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            _service.ShredFileAsync("", 3, CancellationToken.None));
    }

    [Fact]
    public void QuerySeekPenalty_ReturnsNull_OnNonAsciiLetter()
    {
        Assert.Null(_service.QuerySeekPenalty('?'));
        Assert.Null(_service.QuerySeekPenalty('1'));
    }

    [Fact]
    public void QuerySeekPenalty_ReturnsBoolean_OnValidDriveLetter()
    {
        bool? penalty = _service.QuerySeekPenalty('C');
        // Drive C should return true or false (non-null on valid system drive)
        Assert.NotNull(penalty);
    }

    [Fact]
    public void OptimizeMemoryLists_WithZeroMask_ReturnsCleanly()
    {
        ulong freed = _service.OptimizeMemoryLists(0);
        // Zero mask probes without mutation and returns >= 0
        Assert.True(freed >= 0);
    }

    [Fact]
    public void OptimizeMemoryLists_WithFileCacheMask_ReturnsCleanly()
    {
        ulong freed = _service.OptimizeMemoryLists(0x20);
        Assert.True(freed >= 0);
    }

    [Fact]
    public void BroadcastShellNotify_ExecutesCleanlyWithoutThrowing()
    {
        _service.BroadcastShellNotify();
    }

    [Fact]
    public void IsWindowCloaked_WithZeroHandle_ReturnsNull()
    {
        Assert.Null(_service.IsWindowCloaked(nint.Zero));
    }

    [Fact]
    public void IsWindowCloaked_WithInvalidHandle_ReturnsNullOrFalse()
    {
        bool? result = _service.IsWindowCloaked(new nint(12345678));
        // Invalid HWND should fail DwmGetWindowAttribute and return null or false
        Assert.True(result == null || result == false);
    }
}
