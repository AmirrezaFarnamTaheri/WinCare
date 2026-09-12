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
}
