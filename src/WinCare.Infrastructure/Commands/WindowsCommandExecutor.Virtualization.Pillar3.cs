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

internal sealed partial class WindowsCommandExecutor
{
    private Task<CommandHandlerOutcome> ExecuteVirtualizationPillar3CommandAsync(
        CommandDefinition definition,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        return definition.Id switch
        {
            "wsl-disk-compact" => ExecuteWslDiskCompactAsync(cancellationToken),
            "docker-volume-prune" => ExecuteDockerVolumePruneAsync(cancellationToken),
            _ => Task.FromResult(CommandHandlerOutcome.Failed("Unknown Virtualization Pillar 3 command."))
        };
    }

    private async Task<CommandHandlerOutcome> ExecuteWslDiskCompactAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var details = new StringBuilder();
        var trimmedDistros = new List<string>();

        // Phase 1: Enumerate installed WSL distributions and execute guest fstrim
        try
        {
            var listResult = await _process.RunAsync("wsl.exe", ["-l", "-q"], cancellationToken, timeout: TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            if (listResult.ExitCode == 0)
            {
                var distros = listResult.StandardOutput
                    .Split(new[] { '\r', '\n', '\0' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .ToList();

                foreach (var distro in distros)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    try
                    {
                        var trimResult = await _process.RunAsync(
                            "wsl.exe",
                            ["-d", distro, "-u", "root", "-e", "fstrim", "-v", "/"],
                            cancellationToken,
                            timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);

                        if (trimResult.ExitCode == 0)
                        {
                            trimmedDistros.Add(distro);
                        }
                    }
                    catch
                    {
                        // Distro may be stopped or not have fstrim installed
                    }
                }
            }
        }
        catch
        {
            // WSL may not be enabled or installed
        }

        // Phase 2: Terminate running WSL instances to release file locks on virtual disks
        try
        {
            await _process.RunAsync("wsl.exe", ["--shutdown"], cancellationToken, timeout: TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }

        // Phase 3: Discover ext4.vhdx files across user profiles and packages
        var vhdxFiles = new List<FileInfo>();
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string packagesPath = Path.Combine(localAppData, "Packages");
        string dockerWslPath = Path.Combine(localAppData, "Docker", "wsl");

        var searchRoots = new[] { packagesPath, dockerWslPath };
        foreach (var root in searchRoots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                var dir = new DirectoryInfo(root);
                foreach (var file in dir.EnumerateFiles("ext4.vhdx", SafeRecursiveEnumeration))
                {
                    vhdxFiles.Add(file);
                }
            }
            catch
            {
            }
        }

        if (vhdxFiles.Count == 0 && trimmedDistros.Count == 0)
        {
            return CommandHandlerOutcome.Success("WSL2 virtual disk compaction audited: No active WSL distros or ext4.vhdx images found.");
        }

        // Phase 4: Compact virtual disks via host diskpart script
        int compactedCount = 0;
        long totalReclaimed = 0;

        foreach (var vhdx in vhdxFiles)
        {
            if (cancellationToken.IsCancellationRequested) break;

            string scriptPath = Path.Combine(Path.GetTempPath(), $"wincare_compact_{Guid.NewGuid():N}.txt");
            try
            {
                long beforeSize = vhdx.Length;
                string scriptContent = $"select vdisk file=\"{vhdx.FullName}\"\r\nattach vdisk readonly\r\ncompact vdisk\r\ndetach vdisk\r\n";
                await File.WriteAllTextAsync(scriptPath, scriptContent, cancellationToken).ConfigureAwait(false);

                var diskpartResult = await _process.RunAsync(
                    "diskpart.exe",
                    ["/s", scriptPath],
                    cancellationToken,
                    timeout: TimeSpan.FromMinutes(5)).ConfigureAwait(false);

                if (diskpartResult.ExitCode == 0)
                {
                    vhdx.Refresh();
                    long afterSize = vhdx.Length;
                    long reclaimed = Math.Max(0, beforeSize - afterSize);
                    totalReclaimed += reclaimed;
                    compactedCount++;
                    details.Append($"Compacted {Path.GetFileName(vhdx.DirectoryName)}: reclaimed {FormatPillarBytes(reclaimed)}. ");
                }
            }
            catch (Exception ex)
            {
                details.Append($"VHDX compaction note ({vhdx.Name}): {ex.Message}. ");
            }
            finally
            {
                try { if (File.Exists(scriptPath)) File.Delete(scriptPath); } catch { }
            }
        }

        string trimmedSummary = trimmedDistros.Count > 0 ? $"Guest fstrim succeeded on: {string.Join(", ", trimmedDistros)}. " : string.Empty;
        return CommandHandlerOutcome.Success(
            $"WSL2 virtual disk compaction completed: {trimmedSummary}{compactedCount} VHDX disks compacted, {FormatPillarBytes(totalReclaimed)} reclaimed. {details.ToString().Trim()}");
    }

    private async Task<CommandHandlerOutcome> ExecuteDockerVolumePruneAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var result = await _process.RunAsync(
                "docker.exe",
                ["volume", "prune", "-f"],
                cancellationToken,
                timeout: TimeSpan.FromMinutes(2)).ConfigureAwait(false);

            if (result.ExitCode == 0)
            {
                string outputSummary = result.StandardOutput.Trim();
                if (string.IsNullOrWhiteSpace(outputSummary))
                {
                    outputSummary = "All dangling anonymous Docker volumes were successfully pruned.";
                }

                return CommandHandlerOutcome.Success($"Docker Desktop Volume Pruner: {outputSummary}");
            }

            return CommandHandlerOutcome.Success(
                $"Docker volume prune note: Docker CLI returned code {result.ExitCode} (Docker engine may not be currently running).");
        }
        catch (CommandDependencyException)
        {
            return CommandHandlerOutcome.Success("Docker Desktop Volume Pruner: docker.exe not found on PATH; operation skipped safely.");
        }
        catch (Exception ex)
        {
            return CommandHandlerOutcome.Success($"Docker Desktop Volume Pruner: {ex.Message}");
        }
    }
}
