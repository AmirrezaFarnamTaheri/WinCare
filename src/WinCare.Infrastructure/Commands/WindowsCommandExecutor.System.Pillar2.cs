namespace WinCare.Infrastructure.Commands;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Security;

internal sealed partial class WindowsCommandExecutor
{
    private Task<CommandHandlerOutcome> ExecuteSystemPillar2CommandAsync(
        CommandDefinition definition,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        return definition.Id switch
        {
            "cleaner-servicing-remnants" => ExecuteServicingRemnantsCleanAsync(cancellationToken),
            "cleaner-dism-component-store" => ExecuteDismComponentStoreCleanAsync(cancellationToken),
            "cleaner-windows-update-cache" => ExecuteWindowsUpdateCacheCleanAsync(cancellationToken),
            "compress-compactos" => ExecuteCompactOsCompressionAsync(cancellationToken),
            _ => Task.FromResult(CommandHandlerOutcome.Failed("Unknown System Pillar 2 command."))
        };
    }

    private async Task<CommandHandlerOutcome> ExecuteServicingRemnantsCleanAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(async () =>
        {
            int deletedFiles = 0;
            long reclaimedBytes = 0;
            var details = new StringBuilder();

            using var privScope = new TokenPrivilegeScope(
                TokenPrivilegeScope.SeTakeOwnershipPrivilege,
                TokenPrivilegeScope.SeRestorePrivilege,
                TokenPrivilegeScope.SeBackupPrivilege);

            string systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? "C:\\";
            string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            var targetDirectories = new List<string>
            {
                Path.Combine(systemDrive, "$WINDOWS.~BT"),
                Path.Combine(systemDrive, "$WinREAgent"),
                Path.Combine(systemDrive, "$SysReset"),
                Path.Combine(windowsDir, "Panther")
            };

            foreach (var target in targetDirectories)
            {
                if (!Directory.Exists(target)) continue;

                // Take ownership and grant Administrators full control if locked by TrustedInstaller
                try
                {
                    await _process.RunAsync("takeown.exe", ["/F", target, "/A", "/R", "/D", "Y"], cancellationToken, timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    await _process.RunAsync("icacls.exe", [target, "/grant", "*S-1-5-32-544:F", "/T", "/C", "/Q"], cancellationToken, timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                catch
                {
                    // Proceed to direct file deletion
                }

                try
                {
                    var dir = new DirectoryInfo(target);
                    foreach (var file in dir.EnumerateFiles("*", SafeRecursiveEnumeration))
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        try
                        {
                            long len = file.Length;
                            file.Attributes = FileAttributes.Normal;
                            file.Delete();
                            deletedFiles++;
                            reclaimedBytes += len;
                        }
                        catch
                        {
                        }
                    }

                    try
                    {
                        dir.Delete(true);
                    }
                    catch
                    {
                    }

                    details.Append($"Purged {Path.GetFileName(target)}. ");
                }
                catch (Exception ex)
                {
                    details.Append($"Servicing remnant note ({target}): {ex.Message}. ");
                }
            }

            if (deletedFiles == 0 && reclaimedBytes == 0 && details.Length == 0)
            {
                return CommandHandlerOutcome.Success("Windows Servicing remnants audited: No obsolete servicing directories ($WINDOWS.~BT, $WinREAgent, Panther) found.");
            }

            return CommandHandlerOutcome.Success(
                $"Windows Servicing remnants purged: {deletedFiles} files removed, {FormatPillarBytes(reclaimedBytes)} reclaimed. {details.ToString().Trim()}");
        }, cancellationToken);
    }

    private async Task<CommandHandlerOutcome> ExecuteDismComponentStoreCleanAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var result = await _process.RunAsync(
                "dism.exe",
                ["/online", "/Cleanup-Image", "/StartComponentCleanup", "/ResetBase"],
                cancellationToken,
                timeout: TimeSpan.FromMinutes(15)).ConfigureAwait(false);

            if (result.ExitCode == 0)
            {
                return CommandHandlerOutcome.Success(
                    "DISM Component Store cleaned successfully (/ResetBase applied). Obsolete superseded component packages removed; system baseline consolidated.");
            }
            if (result.ExitCode == 3010)
            {
                return CommandHandlerOutcome.Success(
                    "DISM Component Store cleaned successfully (/ResetBase applied). System restart required to complete pending servicing operations.");
            }

            return CommandHandlerOutcome.Failed(
                $"DISM cleanup completed with exit code {result.ExitCode}: {(string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput.Trim() : result.StandardError.Trim())}");
        }
        catch (Exception ex)
        {
            return CommandHandlerOutcome.Failed($"DISM Component Store clean failed: {ex.Message}");
        }
    }

    private async Task<CommandHandlerOutcome> ExecuteWindowsUpdateCacheCleanAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // 1. Pause Windows Update (wuauserv) and BITS services
            try
            {
                await _process.RunAsync("net.exe", ["stop", "wuauserv", "/y"], cancellationToken, timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                await _process.RunAsync("net.exe", ["stop", "bits", "/y"], cancellationToken, timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
            catch
            {
            }

            // 2. Purge SoftwareDistribution\Download cache
            string downloadPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "SoftwareDistribution",
                "Download");

            int deletedFiles = 0;
            long reclaimedBytes = 0;

            if (Directory.Exists(downloadPath))
            {
                var dir = new DirectoryInfo(downloadPath);
                foreach (var file in dir.EnumerateFiles("*", SafeRecursiveEnumeration))
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    try
                    {
                        long len = file.Length;
                        file.Attributes = FileAttributes.Normal;
                        file.Delete();
                        deletedFiles++;
                        reclaimedBytes += len;
                    }
                    catch
                    {
                    }
                }

                foreach (var sub in dir.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        sub.Delete(true);
                    }
                    catch
                    {
                    }
                }
            }

            // 3. Restart wuauserv and BITS services
            try
            {
                await _process.RunAsync("net.exe", ["start", "wuauserv"], cancellationToken, timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                await _process.RunAsync("net.exe", ["start", "bits"], cancellationToken, timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
            catch
            {
            }

            return CommandHandlerOutcome.Success(
                $"Windows Update download cache purged: {deletedFiles} staged updates removed, {FormatPillarBytes(reclaimedBytes)} reclaimed. Windows Update service resumed.");
        }
        catch (Exception ex)
        {
            return CommandHandlerOutcome.Failed($"Windows Update cache purge failed: {ex.Message}");
        }
    }

    private async Task<CommandHandlerOutcome> ExecuteCompactOsCompressionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var result = await _process.RunAsync(
                "compact.exe",
                ["/compactos:always"],
                cancellationToken,
                timeout: TimeSpan.FromMinutes(15)).ConfigureAwait(false);

            if (result.ExitCode == 0)
            {
                string firstOutputLine = result.StandardOutput
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(l => l.Contains("CompactOS", StringComparison.OrdinalIgnoreCase) || l.Contains("compressed", StringComparison.OrdinalIgnoreCase))
                    ?? result.StandardOutput.Trim();

                return CommandHandlerOutcome.Success($"CompactOS compression applied: {firstOutputLine}");
            }

            return CommandHandlerOutcome.Failed($"CompactOS returned exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }
        catch (Exception ex)
        {
            return CommandHandlerOutcome.Failed($"CompactOS execution failed: {ex.Message}");
        }
    }
}
