using WinCare.Domain.Applications;

namespace WinCare.Application.Applications;

/// <summary>Read-only source of installed-application registry evidence.</summary>
public interface IInstalledApplicationInventoryService
{
    /// <summary>Enumerates installed applications without changing the registry.</summary>
    Task<InstalledApplicationInventory> GetInstalledApplicationsAsync(CancellationToken cancellationToken);
}
