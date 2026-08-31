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
}
