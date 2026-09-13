namespace WinCare.Application.Storage;

/// <summary>
/// Builds bounded, read-only storage reports for a caller-selected directory.
/// </summary>
public interface IStorageReportService
{
    /// <summary>
    /// Scans a directory without changing it. Cancellation is represented in the returned partial report.
    /// </summary>
    Task<StorageReport> CreateReportAsync(StorageReportRequest request, CancellationToken cancellationToken);
}
