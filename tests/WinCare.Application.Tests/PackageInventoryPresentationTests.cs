using System.Text.Json;
using WinCare.App.ViewModels.Pages;

namespace WinCare.Application.Tests;

public sealed class PackageInventoryPresentationTests
{
    [Fact]
    public void Mixed_outcomes_remain_distinct_and_scope_is_preserved()
    {
        var payload = JsonSerializer.SerializeToElement(new { scope = "CurrentUser", packages = new[] {
            new { packageFullName = "First", outcome = "Succeeded", errorText = "" },
            new { packageFullName = "Second", outcome = "Failed", errorText = "Deployment failed" }
        } });
        string text = PackageInventoryPresentation.Format(payload);
        Assert.Contains("Current-user", text);
        Assert.Contains("First\nSucceeded", text);
        Assert.Contains("Second\nFailed · Deployment failed", text);
    }

    [Fact]
    public void Registered_inventory_preserves_scope_and_backend_removal_guidance()
    {
        var data = JsonSerializer.SerializeToElement(new { packageFullNames = new[] { "Example_1_x64_publisher" }, removalIdentity = "A registration may not have a WinGet manifest." });
        string text = PackageInventoryPresentation.Format(data);
        Assert.Contains("current user", text);
        Assert.Contains("Example_1_x64_publisher", text);
        Assert.Contains("may not have a WinGet manifest", text);
        Assert.DoesNotContain("provisioned", text);
    }

    [Fact]
    public void Provisioned_empty_inventory_is_distinct_from_unrelated_output()
    {
        Assert.Contains("No packages were returned", PackageInventoryPresentation.Format(JsonSerializer.SerializeToElement(new { packageNames = Array.Empty<string>() })));
        Assert.Empty(PackageInventoryPresentation.Format(JsonSerializer.SerializeToElement(new { unrelated = true })));
        Assert.Empty(PackageInventoryPresentation.Format(null));
    }

    [Fact]
    public void Storage_report_surfaces_bounds_and_largest_files()
    {
        var data = JsonSerializer.SerializeToElement(new
        {
            RootPath = @"C:\Data", AccountedBytes = 1536UL, FilesSeen = 3, DirectoriesSeen = 2,
            ReparsePointsSkipped = 1, HardLinkDuplicatesSkipped = 1, IsPartial = true,
            LargestFiles = new[] { new { Path = @"C:\Data\large.bin", Bytes = 1024UL } }
        });
        string text = PackageInventoryPresentation.Format("storage-report", data);
        Assert.Contains("Storage evidence", text);
        Assert.Contains("1.5 KB accounted", text);
        Assert.Contains("Partial evidence", text);
        Assert.Contains("large.bin", text);
    }

    [Fact]
    public void Residual_discovery_keeps_owner_confidence_and_no_removal_claim()
    {
        var data = JsonSerializer.SerializeToElement(new
        {
            ApplicationsInspected = 4, IsPartial = false,
            Candidates = new[] { new { Owner = new { DisplayName = "Example" }, OwnerConfidence = "High", Path = @"C:\Users\A\AppData\Local\Example", Bytes = 2048UL, RecoveryAvailable = false, Reason = "Direct name match." } }
        });
        string text = PackageInventoryPresentation.Format("app-residual-discovery", data);
        Assert.Contains("Example", text);
        Assert.Contains("Ownership confidence: High", text);
        Assert.Contains("Recovery: not available", text);
        Assert.Contains("No files were selected", text);
    }

    [Fact]
    public void Installer_cache_never_presents_unmatched_as_disposable()
    {
        var data = JsonSerializer.SerializeToElement(new
        {
            scanComplete = true,
            files = new[] { new { path = @"C:\Windows\Installer\x.msi", size = 4096UL, registrationState = "unmatched", retentionReason = "Heuristic only; retained." } }
        });
        string text = PackageInventoryPresentation.Format("installer-cache-analysis", data);
        Assert.Contains("1 unmatched", text);
        Assert.Contains("Every file remains retained", text);
        Assert.Contains("not a deletion recommendation", text);
    }

    [Fact]
    public void Winget_inventory_handles_nested_structured_output()
    {
        var data = JsonSerializer.SerializeToElement(new
        {
            inventory = new { Sources = new[] { new { Packages = new[] { new { PackageIdentifier = "Vendor.App", PackageName = "Vendor App", InstalledVersion = "1.0", AvailableVersion = "2.0", Source = "winget" } } } } }
        });
        string text = PackageInventoryPresentation.Format("winget-upgrade-inventory", data);
        Assert.Contains("Vendor App", text);
        Assert.Contains("Vendor.App", text);
        Assert.Contains("1.0 → 2.0", text);
        Assert.Contains("No package was installed", text);
    }
}
