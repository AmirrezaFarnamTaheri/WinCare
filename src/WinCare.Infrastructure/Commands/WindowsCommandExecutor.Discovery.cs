using System.Text.Json;
using WinCare.Application.Applications;
using WinCare.Application.Commands;
using WinCare.Application.Storage;
using WinCare.Infrastructure.Applications;
using WinCare.Infrastructure.Storage;

namespace WinCare.Infrastructure.Commands;

/// <summary>
/// Bounded, read-only discovery routes. These routes return evidence only and never use
/// discovery output as an input to a mutating command.
/// </summary>
internal sealed partial class WindowsCommandExecutor
{
    private async Task<CommandHandlerOutcome> StorageReportAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string rootPath = RequireExistingDirectory(p.RequiredString("RootPath"));
        var request = new StorageReportRequest(
            rootPath,
            p.Int32("MaxEntries", 25_000, 1, 100_000),
            p.Int32("MaxDepth", 6, 0, 64),
            p.Int32("LargestFileCount", 25, 1, 500));

        StorageReport report = await new StorageReportService()
            .CreateReportAsync(request, cancellationToken).ConfigureAwait(false);
        return Success("storage-report", report.IsPartial
            ? "Bounded storage report completed with partial evidence."
            : "Bounded storage report completed.", new
        {
            report.RootPath,
            report.AccountedBytes,
            report.EntriesScanned,
            report.FilesSeen,
            report.DirectoriesSeen,
            report.ReparsePointsSkipped,
            report.HardLinkDuplicatesSkipped,
            report.HardLinkAccountingComplete,
            report.IsPartial,
            report.IsCancelled,
            report.ReachedEntryLimit,
            report.ReachedDepthLimit,
            report.Directories,
            report.LargestFiles,
            report.Issues,
            safety = "Read-only bounded filesystem evidence. Reparse points are skipped and no files are selected for removal."
        });
    }

    private async Task<CommandHandlerOutcome> AppResidualDiscoveryAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        var request = new AppResidualDiscoveryRequest(
            p.Int32("MaxCandidates", 100, 1, 1_000),
            p.Int32("MaxEntriesPerCandidate", 10_000, 1, 100_000),
            p.Int32("MaxDepth", 4, 0, 32),
            p.Int32("LargestFileCount", 10, 1, 100));
        var service = new AppResidualDiscoveryService(
            new WindowsInstalledApplicationInventoryService(),
            new StorageReportService());
        AppResidualDiscoveryReport report = await service.DiscoverAsync(request, cancellationToken).ConfigureAwait(false);

        return Success("app-residual-discovery", report.IsPartial
            ? "Residual candidate discovery completed with partial evidence; no candidate was changed."
            : "Residual candidate discovery completed; no candidate was changed.", new
        {
            report.ApplicationsInspected,
            report.Candidates,
            report.IsPartial,
            report.IsCancelled,
            report.Issues,
            safety = "Read-only candidate evidence. Every candidate is a high-confidence ownership hypothesis, retains its files, and has no removal action."
        });
    }

    private async Task<CommandHandlerOutcome> WingetUpgradeInventoryAsync(CancellationToken cancellationToken)
    {
        string executable = (ExecutableFinderSeam?.Invoke("winget.exe") ?? FindExecutable("winget.exe"))
            ?? throw new CommandDependencyException("winget.exe", "winget.exe is required to inspect available package upgrades.");
        ProcessExecutionResult result = await RunAppxProcessAsync(
            executable,
            ["upgrade", "--output", "json", "--accept-source-agreements", "--disable-interactivity"],
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return CommandHandlerOutcome.Failed(
                "winget-upgrade-inventory.failed",
                "WinGet could not inspect available package upgrades. No package was changed.",
                Data(new { result.ExitCode, result.StandardOutput, result.StandardError, result.OutputTruncated }));
        }

        try
        {
            using JsonDocument parsed = JsonDocument.Parse(result.StandardOutput);
            JsonElement inventory = parsed.RootElement.Clone();
            return Success("winget-upgrade-inventory", "WinGet available-upgrade inventory completed.", new
            {
                source = "WinGet",
                inventory,
                result.OutputTruncated,
                result.StandardError,
                safety = "Read-only WinGet upgrade inventory. This command does not install, upgrade, uninstall, or pin any package."
            });
        }
        catch (JsonException ex)
        {
            return CommandHandlerOutcome.Failed(
                "winget-upgrade-inventory.invalid_output",
                "WinGet returned successful but unparseable structured output. No package was changed.",
                Data(new { result.StandardOutput, result.StandardError, result.OutputTruncated, parseError = ex.Message }));
        }
    }
}
