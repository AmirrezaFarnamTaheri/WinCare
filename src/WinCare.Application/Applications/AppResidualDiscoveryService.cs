using WinCare.Application.Storage;
using WinCare.Domain.Applications;

namespace WinCare.Application.Applications;

/// <summary>Finds narrowly scoped, read-only residual candidates from uninstall-registry evidence.</summary>
public sealed class AppResidualDiscoveryService : IAppResidualDiscoveryService
{
    private readonly IInstalledApplicationInventoryService _inventory;
    private readonly IStorageReportService _storageReports;

    /// <summary>Creates a residual discovery service from read-only evidence providers.</summary>
    public AppResidualDiscoveryService(IInstalledApplicationInventoryService inventory, IStorageReportService storageReports)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _storageReports = storageReports ?? throw new ArgumentNullException(nameof(storageReports));
    }

    /// <inheritdoc />
    public async Task<AppResidualDiscoveryReport> DiscoverAsync(AppResidualDiscoveryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        if (cancellationToken.IsCancellationRequested)
        {
            return new AppResidualDiscoveryReport(0, [], true, true,
                [new AppResidualDiscoveryIssue("installed-applications", "Cancelled", "Discovery was cancelled by the caller.")]);
        }
        InstalledApplicationInventory inventory = await _inventory.GetInstalledApplicationsAsync(cancellationToken).ConfigureAwait(false);
        var candidates = new List<AppResidualCandidate>();
        var issues = inventory.Issues.Select(issue => new AppResidualDiscoveryIssue(issue.Source, "Inventory", issue.Message)).ToList();
        bool partial = inventory.IsPartial;
        bool cancelled = cancellationToken.IsCancellationRequested;
        string localAppData = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (InstalledApplication application in inventory.Applications.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            if (cancelled) break;
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = partial = true;
                issues.Add(new AppResidualDiscoveryIssue(localAppData, "Cancelled", "Discovery was cancelled by the caller."));
                break;
            }
            if (!HasMissingInstallLocation(application)) continue;
            if (candidates.Count >= request.MaxCandidates)
            {
                partial = true;
                issues.Add(new AppResidualDiscoveryIssue(application.RegistryPath, "CandidateLimit", $"The {request.MaxCandidates:N0} candidate limit was reached."));
                break;
            }

            string? path = TryGetDirectLocalAppDataPath(localAppData, application.DisplayName);
            if (path is null || !seenPaths.Add(path) || !Directory.Exists(path)) continue;

            StorageReport storage = await _storageReports.CreateReportAsync(
                new StorageReportRequest(path, request.MaxEntriesPerCandidate, request.MaxDepth, request.LargestFileCount), cancellationToken).ConfigureAwait(false);
            partial |= storage.IsPartial;
            cancelled |= storage.IsCancelled;
            candidates.Add(new AppResidualCandidate(
                application,
                AppResidualOwnerConfidence.High,
                $"Uninstall registry evidence: {application.RegistryPath}",
                path,
                storage.AccountedBytes,
                "The uninstall registry still names this application, its recorded install location is missing, and a direct LocalAppData folder matches its display name.",
                RecoveryAvailable: false,
                storage));
        }

        return new AppResidualDiscoveryReport(inventory.Applications.Count, candidates.ToArray(), partial, cancelled, issues.ToArray());
    }

    private static bool HasMissingInstallLocation(InstalledApplication application)
    {
        if (string.IsNullOrWhiteSpace(application.InstallLocation)) return false;
        try { return !Directory.Exists(Path.GetFullPath(application.InstallLocation)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return true; }
    }

    private static string? TryGetDirectLocalAppDataPath(string localAppData, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName is "." or ".." || displayName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        string candidate = Path.GetFullPath(Path.Combine(localAppData, displayName));
        string rootWithSeparator = Path.TrimEndingDirectorySeparator(localAppData) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) ? candidate : null;
    }
}

/// <summary>Read-only app residual discovery contract.</summary>
public interface IAppResidualDiscoveryService
{
    /// <summary>Builds bounded candidate evidence without changing files or registry state.</summary>
    Task<AppResidualDiscoveryReport> DiscoverAsync(AppResidualDiscoveryRequest request, CancellationToken cancellationToken);
}

/// <summary>Bounds for direct LocalAppData residual discovery.</summary>
public sealed record AppResidualDiscoveryRequest(int MaxCandidates = 100, int MaxEntriesPerCandidate = 10_000, int MaxDepth = 4, int LargestFileCount = 10)
{
    /// <summary>Validates discovery limits.</summary>
    public void Validate()
    {
        if (MaxCandidates is < 1 or > 1_000) throw new ArgumentOutOfRangeException(nameof(MaxCandidates));
        if (MaxEntriesPerCandidate is < 1 or > 100_000) throw new ArgumentOutOfRangeException(nameof(MaxEntriesPerCandidate));
        if (MaxDepth is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(MaxDepth));
        if (LargestFileCount is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(LargestFileCount));
    }
}

/// <summary>Bounded residual discovery output.</summary>
public sealed record AppResidualDiscoveryReport(int ApplicationsInspected, IReadOnlyList<AppResidualCandidate> Candidates, bool IsPartial, bool IsCancelled, IReadOnlyList<AppResidualDiscoveryIssue> Issues);

/// <summary>A potential residual with a single, reviewable owner hypothesis.</summary>
public sealed record AppResidualCandidate(InstalledApplication Owner, AppResidualOwnerConfidence OwnerConfidence, string OwnerEvidence, string Path, ulong Bytes, string Reason, bool RecoveryAvailable, StorageReport StorageReport);

/// <summary>Strength of the owner association shown to the user.</summary>
public enum AppResidualOwnerConfidence { High }

/// <summary>An inventory or bounded scan issue that affected discovery.</summary>
public sealed record AppResidualDiscoveryIssue(string Source, string Kind, string Message);
