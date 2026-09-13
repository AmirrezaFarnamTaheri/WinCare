using WinCare.Application.Applications;
using WinCare.Application.Storage;
using WinCare.Domain.Applications;

namespace WinCare.Application.Tests;

public sealed class AppResidualDiscoveryServiceTests : IDisposable
{
    private readonly string _displayName = "WinCare Residual Test " + Guid.NewGuid().ToString("N");
    private readonly string _candidatePath;
    private readonly List<string> _directoriesToClean = [];

    public AppResidualDiscoveryServiceTests()
    {
        _candidatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), _displayName);
        Directory.CreateDirectory(_candidatePath);
        _directoriesToClean.Add(_candidatePath);
    }

    [Fact]
    public async Task Missing_install_location_and_direct_local_data_folder_form_a_high_confidence_read_only_candidate()
    {
        var application = new InstalledApplication("registry-id", _displayName, "Test Publisher", Path.Combine(_candidatePath, "missing-install"), "HKCU\\Test");
        var storage = new FakeStorageReports(new StorageReport(_candidatePath, 123, 3, 2, 1, 0, 0, true, false, false, false, false, [], [], []));
        var service = new AppResidualDiscoveryService(new FakeInventory(application), storage);

        AppResidualDiscoveryReport report = await service.DiscoverAsync(new AppResidualDiscoveryRequest(), CancellationToken.None);

        AppResidualCandidate candidate = Assert.Single(report.Candidates);
        Assert.Equal(AppResidualOwnerConfidence.High, candidate.OwnerConfidence);
        Assert.Contains("HKCU", candidate.OwnerEvidence);
        Assert.Equal<ulong>(123, candidate.Bytes);
        Assert.False(candidate.RecoveryAvailable);
        Assert.Contains("install location is missing", candidate.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, storage.Calls);
    }

    [Fact]
    public async Task Existing_install_location_is_not_presented_as_a_residual()
    {
        var application = new InstalledApplication("registry-id", _displayName, "Test Publisher", _candidatePath, "HKCU\\Test");
        var storage = new FakeStorageReports(new StorageReport(_candidatePath, 1, 0, 0, 1, 0, 0, true, false, false, false, false, [], [], []));
        var service = new AppResidualDiscoveryService(new FakeInventory(application), storage);

        AppResidualDiscoveryReport report = await service.DiscoverAsync(new AppResidualDiscoveryRequest(), CancellationToken.None);

        Assert.Empty(report.Candidates);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task Cancelled_discovery_returns_an_explicit_partial_result_without_inventory_access()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var inventory = new FakeInventory();
        var service = new AppResidualDiscoveryService(inventory, new FakeStorageReports(StorageReportFor(_candidatePath)));

        AppResidualDiscoveryReport report = await service.DiscoverAsync(new AppResidualDiscoveryRequest(), cancellation.Token);

        Assert.True(report.IsPartial);
        Assert.True(report.IsCancelled);
        Assert.Equal(0, inventory.Calls);
        Assert.Contains(report.Issues, issue => issue.Kind == "Cancelled");
    }

    [Fact]
    public async Task Partial_storage_evidence_keeps_the_candidate_and_marks_discovery_partial()
    {
        var application = new InstalledApplication("registry-id", _displayName, "Test Publisher", Path.Combine(_candidatePath, "missing-install"), "HKCU\\Test");
        var partialStorage = StorageReportFor(_candidatePath) with { IsPartial = true, ReachedEntryLimit = true };
        var service = new AppResidualDiscoveryService(new FakeInventory(application), new FakeStorageReports(partialStorage));

        AppResidualDiscoveryReport report = await service.DiscoverAsync(new AppResidualDiscoveryRequest(), CancellationToken.None);

        Assert.True(report.IsPartial);
        Assert.True(Assert.Single(report.Candidates).StorageReport.ReachedEntryLimit);
    }

    [Fact]
    public async Task Multi_root_roaming_application_data_folder_is_discovered_as_candidate()
    {
        string roamingName = "WinCare Roaming Test " + Guid.NewGuid().ToString("N");
        string roamingRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string roamingPath = Path.Combine(roamingRoot, roamingName);
        Directory.CreateDirectory(roamingPath);
        _directoriesToClean.Add(roamingPath);

        var application = new InstalledApplication("registry-roaming", roamingName, "Test Publisher", Path.Combine(roamingPath, "missing-install"), "HKCU\\Test");
        var storage = new FakeStorageReports(new StorageReport(roamingPath, 456, 1, 1, 1, 0, 0, true, false, false, false, false, [], [], []));
        var service = new AppResidualDiscoveryService(new FakeInventory(application), storage);

        AppResidualDiscoveryReport report = await service.DiscoverAsync(new AppResidualDiscoveryRequest(), CancellationToken.None);

        AppResidualCandidate candidate = Assert.Single(report.Candidates);
        Assert.Equal(roamingPath, candidate.Path);
        Assert.Equal<ulong>(456, candidate.Bytes);
        Assert.Equal(1, storage.Calls);
    }

    [Fact]
    public async Task Multi_root_locallow_application_data_folder_is_discovered_as_candidate()
    {
        string localLowRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "..", "LocalLow");
        if (!Directory.Exists(localLowRoot))
        {
            return;
        }

        string localLowName = "WinCare LocalLow Test " + Guid.NewGuid().ToString("N");
        string localLowPath = Path.Combine(localLowRoot, localLowName);
        Directory.CreateDirectory(localLowPath);
        _directoriesToClean.Add(localLowPath);

        var application = new InstalledApplication("registry-locallow", localLowName, "Test Publisher", Path.Combine(localLowPath, "missing-install"), "HKCU\\Test");
        var storage = new FakeStorageReports(new StorageReport(localLowPath, 789, 1, 1, 1, 0, 0, true, false, false, false, false, [], [], []));
        var service = new AppResidualDiscoveryService(new FakeInventory(application), storage);

        AppResidualDiscoveryReport report = await service.DiscoverAsync(new AppResidualDiscoveryRequest(), CancellationToken.None);

        AppResidualCandidate candidate = Assert.Single(report.Candidates);
        Assert.Equal(Path.GetFullPath(localLowPath), candidate.Path);
        Assert.Equal<ulong>(789, candidate.Bytes);
        Assert.Equal(1, storage.Calls);
    }

    [Fact]
    public async Task System_owned_display_names_are_excluded_from_residual_discovery()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string msDir = Path.Combine(localAppData, "Microsoft");
        // Microsoft folder in LocalAppData almost certainly exists on Windows
        var application = new InstalledApplication("reg-ms", "Microsoft", "Microsoft Corporation", @"C:\Missing\Install", "HKCU\\Test");
        var storage = new FakeStorageReports(new StorageReport(msDir, 9999, 10, 10, 1, 0, 0, true, false, false, false, false, [], [], []));
        var service = new AppResidualDiscoveryService(new FakeInventory(application), storage);

        AppResidualDiscoveryReport report = await service.DiscoverAsync(new AppResidualDiscoveryRequest(), CancellationToken.None);

        Assert.Empty(report.Candidates);
        Assert.Equal(0, storage.Calls);
    }

    public void Dispose()
    {
        foreach (string path in _directoriesToClean)
        {
            if (Directory.Exists(path))
            {
                try { Directory.Delete(path, recursive: true); } catch { }
            }
        }
    }

    private static StorageReport StorageReportFor(string path) => new(path, 0, 0, 0, 1, 0, 0, true, false, false, false, false, [], [], []);

    private sealed class FakeInventory(params InstalledApplication[] applications) : IInstalledApplicationInventoryService
    {
        public int Calls { get; private set; }
        public Task<InstalledApplicationInventory> GetInstalledApplicationsAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new InstalledApplicationInventory(applications, false, []));
        }
    }

    private sealed class FakeStorageReports(StorageReport report) : IStorageReportService
    {
        public int Calls { get; private set; }
        public Task<StorageReport> CreateReportAsync(StorageReportRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(report);
        }
    }
}
