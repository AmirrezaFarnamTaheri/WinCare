using WinCare.Infrastructure.Applications;

namespace WinCare.Infrastructure.Tests;

public sealed class WindowsInstalledApplicationInventoryServiceTests
{
    [Fact]
    public async Task Pre_cancelled_inventory_is_returned_as_partial_evidence()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new WindowsInstalledApplicationInventoryService().GetInstalledApplicationsAsync(cancellation.Token);

        Assert.True(result.IsPartial);
        Assert.Contains(result.Issues, issue => issue.Message.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Vendor\App", null, null, @"C:\Program Files\Vendor\App")]
    [InlineData(null, @"C:\Program Files\Vendor\App\App.exe,0", null, @"C:\Program Files\Vendor\App")]
    [InlineData(null, "\"C:\\Program Files\\Vendor\\App\\App.exe\",1", null, @"C:\Program Files\Vendor\App")]
    [InlineData(null, null, "\"C:\\Program Files\\Vendor\\App\\uninstall.exe\" /quiet", @"C:\Program Files\Vendor\App")]
    [InlineData(null, null, "MsiExec.exe /X{12345678-ABCD-1234-ABCD-1234567890AB}", null)]
    [InlineData(null, null, null, null)]
    public void InferInstallLocation_extracts_correct_directory(string? installLoc, string? icon, string? uninstall, string? expected)
    {
        string? actual = WindowsInstalledApplicationInventoryService.InferInstallLocation(installLoc, icon, uninstall);
        Assert.Equal(expected, actual);
    }
}
