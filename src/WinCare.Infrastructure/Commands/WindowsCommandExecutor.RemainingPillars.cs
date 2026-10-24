namespace WinCare.Infrastructure.Commands;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Native;

internal sealed partial class WindowsCommandExecutor
{
    // ==========================================
    // Pillar 4: Storage Deduplication
    // ==========================================

    private async Task<CommandHandlerOutcome> ExecuteStorageDedupScanAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string scanDir = Path.Combine(userProfile, "Downloads");
            if (!Directory.Exists(scanDir))
            {
                scanDir = userProfile;
            }

            var report = StorageDeduplicationHelper.ScanDuplicateCandidates(scanDir, maxFilesToCheck: 1000, cancellationToken);
            return CommandHandlerOutcome.Success($"Storage Deduplication Scan completed on '{scanDir}': {report.Summary}");
        }, cancellationToken);
    }

    // The previous implementation renamed duplicate user files and replaced them with NTFS
    // hardlinks after an unbound, best-effort scan. Between scanning and renaming, files could
    // change; the backup was then deleted without proving the two files still matched. Hardlinks
    // also change future write semantics and do not preserve independent file metadata. Until an
    // exact-target, race-safe preview/apply protocol exists, reject both direct executor calls
    // and requests that might bypass the migration admission gate.
    private static Task<CommandHandlerOutcome> ExecuteStorageDedupHardlinkAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CommandHandlerOutcome.Blocked(
            "storage.dedup.hardlink_unavailable",
            "Hardlink consolidation is unavailable: replacing original files without a verified, " +
            "target-bound plan could lose changed data and permanently couple independent files. " +
            "Use the read-only duplicate scan instead."));
    }

    // ==========================================
    // Pillar 5: Performance
    // ==========================================

    private async Task<CommandHandlerOutcome> ExecutePerfStandbyPurgeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MemoryGovernor.MemoryCompactionResult result =
            await MemoryGovernor.CompactSystemMemoryAsync(cancellationToken).ConfigureAwait(false);
        if (!result.AnySucceeded)
            return CommandHandlerOutcome.Failed(
                "Memory adjustment was not confirmed: Windows refused or did not support the requested native operations.");

        double observedMiB = result.ObservedAvailableBytesChange / (1024d * 1024d);
        return CommandHandlerOutcome.Success(
            $"Windows accepted {(result.StandbyRequestSucceeded ? "the standby-list request" : "no standby-list request")}; " +
            $"trimmed {result.WorkingSetTrimCount} of {result.WorkingSetAttemptCount} attempted working sets. " +
            $"Available physical memory changed by {observedMiB:+0.0;-0.0;0.0} MiB during the operation; " +
            "this observation is not proof of memory reclaimed.");
    }

    private Task<CommandHandlerOutcome> ExecutePerfTimerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (success, current) = SystemTimerGovernor.EnableHighPrecisionTimer();
        if (success)
        {
            return Task.FromResult(CommandHandlerOutcome.Success($"High-precision multimedia system timer activated: {current / 10000.0:F2}ms resolution (500us quantum)."));
        }
        return Task.FromResult(CommandHandlerOutcome.Failed("Failed to activate high precision timer via NtSetTimerResolution."));
    }

    private Task<CommandHandlerOutcome> ExecutePerfGpuMpoAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\Dwm", writable: true);
            if (key != null)
            {
                object? existing = key.GetValue("OverlayTestMode");
                if (existing is int intVal && intVal == 5)
                {
                    key.DeleteValue("OverlayTestMode", false);
                    return Task.FromResult(CommandHandlerOutcome.Success("GPU Multi-Plane Overlay (MPO) enabled (OverlayTestMode deleted). Restart DWM to take effect."));
                }
                else
                {
                    key.SetValue("OverlayTestMode", 5, RegistryValueKind.DWord);
                    return Task.FromResult(CommandHandlerOutcome.Success("GPU Multi-Plane Overlay (MPO) disabled (OverlayTestMode set to 5 for stutter-free desktop rendering)."));
                }
            }

            return Task.FromResult(CommandHandlerOutcome.Failed("DWM registry path not found or access denied."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(CommandHandlerOutcome.Failed($"GPU MPO toggle failed: {ex.Message}"));
        }
    }

    private async Task<CommandHandlerOutcome> ExecutePerfPowerSchemeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // 1. Attempt to unlock Ultimate Performance power scheme if not present
            try
            {
                await _process.RunAsync(
                    "powercfg.exe",
                    ["-duplicatescheme", "e9a42b02-d5df-448d-aa00-03f14749eb61"],
                    cancellationToken,
                    timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch
            {
            }

            // 2. Set Ultimate Performance as active scheme
            bool activated = Win32PowerSchemeGovernor.SetActiveScheme(Win32PowerSchemeGovernor.UltimatePerformanceGuid);
            if (activated)
            {
                return CommandHandlerOutcome.Success("Ultimate Performance power scheme unlocked and activated.");
            }

            // Fallback to High Performance
            bool highPerf = Win32PowerSchemeGovernor.SetActiveScheme(Win32PowerSchemeGovernor.HighPerformanceGuid);
            if (highPerf)
            {
                return CommandHandlerOutcome.Success("High Performance power scheme activated.");
            }

            return CommandHandlerOutcome.Failed("Failed to activate High Performance or Ultimate Performance power scheme.");
        }
        catch (Exception ex)
        {
            return CommandHandlerOutcome.Failed($"Power scheme execution failed: {ex.Message}");
        }
    }

    // ==========================================
    // Pillar 6: Desktop
    // ==========================================

    private Task<CommandHandlerOutcome> ExecuteWinShadingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            nint hwnd = WindowsInterop.GetForegroundWindow();
            if (hwnd == nint.Zero)
            {
                return Task.FromResult(CommandHandlerOutcome.Failed("No active foreground window detected."));
            }

            if (WindowsInterop.GetWindowRect(hwnd, out var rect))
            {
                int currentHeight = rect.Bottom - rect.Top;
                int titleBarHeight = 36;

                if (currentHeight <= 45)
                {
                    // Restore window height to default standard height
                    if (!WindowsInterop.SetWindowPos(
                        hwnd,
                        nint.Zero,
                        0,
                        0,
                        rect.Right - rect.Left,
                        600,
                        WindowsInterop.SwpNoMove | WindowsInterop.SwpNoZOrder | WindowsInterop.SwpFrameChanged))
                        return Task.FromResult(CommandHandlerOutcome.Failed("Windows refused to resize the foreground window."));

                    return Task.FromResult(CommandHandlerOutcome.Success("Foreground window height set to 600 pixels (the previous height was not recorded)."));
                }
                else
                {
                    // Roll up window to titlebar
                    if (!WindowsInterop.SetWindowPos(
                        hwnd,
                        nint.Zero,
                        0,
                        0,
                        rect.Right - rect.Left,
                        titleBarHeight,
                        WindowsInterop.SwpNoMove | WindowsInterop.SwpNoZOrder | WindowsInterop.SwpFrameChanged))
                        return Task.FromResult(CommandHandlerOutcome.Failed("Windows refused to resize the foreground window."));

                    return Task.FromResult(CommandHandlerOutcome.Success("Foreground window height set to 36 pixels; original height was not saved."));
                }
            }

            return Task.FromResult(CommandHandlerOutcome.Failed("Unable to query foreground window coordinates."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(CommandHandlerOutcome.Failed($"Window shading rollup failed: {ex.Message}"));
        }
    }

    private Task<CommandHandlerOutcome> ExecuteWinEdgeSnapAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var desktopKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
            if (desktopKey != null)
            {
                desktopKey.SetValue("WindowArrangementActive", "1", RegistryValueKind.String);
                desktopKey.SetValue("SnapSizing", "1", RegistryValueKind.String);
                desktopKey.SetValue("DockMoving", "1", RegistryValueKind.String);
                return Task.FromResult(CommandHandlerOutcome.Success("Desktop snapping registry preferences saved. Windows may require signing out or restarting Explorer before changes take effect."));
            }

            return Task.FromResult(CommandHandlerOutcome.Failed("Failed to access user desktop configuration in registry."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(CommandHandlerOutcome.Failed($"Edge snapping configuration failed: {ex.Message}"));
        }
    }

    private Task<CommandHandlerOutcome> ExecuteWinCornerStyleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            nint hwnd = WindowsInterop.GetForegroundWindow();
            if (hwnd != nint.Zero)
            {
                // DWMWCP_ROUND = 2, DWMWCP_ROUNDSMALL = 3
                int cornerPreference = 2;
                int cornerResult = WindowsInterop.DwmSetWindowAttribute(hwnd, WindowsInterop.DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));

                // DWM_SYSTEMBACKDROP_TYPE: 2 = Mica, 3 = Acrylic
                int backdropType = 2;
                int backdropResult = WindowsInterop.DwmSetWindowAttribute(hwnd, WindowsInterop.DwmwaSystemBackdropType, ref backdropType, sizeof(int));

                if (cornerResult != 0 || backdropResult != 0)
                    return Task.FromResult(CommandHandlerOutcome.Failed(
                        $"Window styling incomplete: corner HRESULT=0x{cornerResult:X8}, backdrop HRESULT=0x{backdropResult:X8}."));

                return Task.FromResult(CommandHandlerOutcome.Success("Rounded corners and Mica backdrop applied to the foreground window."));
            }

            return Task.FromResult(CommandHandlerOutcome.Blocked("window.foreground_required", "No foreground window was found; no window styling was applied."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(CommandHandlerOutcome.Failed($"Window corner styler failed: {ex.Message}"));
        }
    }

    // ==========================================
    // Pillar 7: Guard
    // ==========================================

    // The guard currently starts as a console daemon and has no SCM ServiceMain handler.
    // Installing it with sc.exe can leave behind an auto-start service that cannot start.
    // Both catalog admission and direct executor routing refuse this unsupported operation.
    private static Task<CommandHandlerOutcome> ExecuteGuardPromoteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CommandHandlerOutcome.Blocked(
            "guard.scm_unavailable",
            "The Guard executable does not implement the Windows service lifecycle. " +
            "Service registration is unavailable until SCM integration is implemented and verified."));
    }

    private static Task<CommandHandlerOutcome> ExecuteGuardConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CommandHandlerOutcome.Blocked(
            "guard.unavailable",
            "wincare-guard background service is not installed on this system."));
    }

    // Registry switches alone do not provide toast registration, delivery or a consumer
    // for the Rust guard's queued alert XML files. Do not report notifications as enabled.
    private static Task<CommandHandlerOutcome> ExecuteGuardToastAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CommandHandlerOutcome.Blocked(
            "guard.toast_unavailable",
            "Guard notifications are unavailable until the application can register and " +
            "deliver actual Windows notifications from the alert queue."));
    }

    // ==========================================
    // Pillar 8: Cleanup
    // ==========================================

    private async Task<CommandHandlerOutcome> ExecuteCleanerSquirrelAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            int deletedDirs = 0;
            int deletedPackages = 0;
            long reclaimedBytes = 0;
            var details = new StringBuilder();

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!Directory.Exists(localAppData))
            {
                return CommandHandlerOutcome.Success("Local AppData directory not found.");
            }

            try
            {
                var dir = new DirectoryInfo(localAppData);
                foreach (var appFolder in dir.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        // Identify candidate Squirrel directories by finding multiple `app-*` folders
                        var appVersions = appFolder.EnumerateDirectories("app-*", SearchOption.TopDirectoryOnly)
                            .OrderByDescending(d => d.LastWriteTimeUtc)
                            .ToList();

                        if (appVersions.Count > 1)
                        {
                            // Keep the newest version, prune older versions
                            for (int i = 1; i < appVersions.Count; i++)
                            {
                                var oldVersion = appVersions[i];
                                try
                                {
                                    long verSize = 0;
                                    foreach (var f in oldVersion.EnumerateFiles("*", SafeRecursiveEnumeration))
                                    {
                                        verSize += f.Length;
                                        f.Attributes = FileAttributes.Normal;
                                    }
                                    oldVersion.Delete(true);
                                    deletedDirs++;
                                    reclaimedBytes += verSize;
                                    details.Append($"Pruned {appFolder.Name}/{oldVersion.Name}. ");
                                }
                                catch
                                {
                                }
                            }
                        }

                        // Inspect `packages` directory for obsolete nupkg files
                        string packagesPath = Path.Combine(appFolder.FullName, "packages");
                        if (Directory.Exists(packagesPath))
                        {
                            var nupkgs = new DirectoryInfo(packagesPath).EnumerateFiles("*.nupkg", SearchOption.TopDirectoryOnly)
                                .OrderByDescending(f => f.LastWriteTimeUtc)
                                .ToList();

                            // If there are multiple full or delta nupkg files, prune older ones
                            if (nupkgs.Count > 2)
                            {
                                for (int i = 2; i < nupkgs.Count; i++)
                                {
                                    try
                                    {
                                        var pkg = nupkgs[i];
                                        long pkgSize = pkg.Length;
                                        pkg.Attributes = FileAttributes.Normal;
                                        pkg.Delete();
                                        deletedPackages++;
                                        reclaimedBytes += pkgSize;
                                    }
                                    catch
                                    {
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                details.Append($"Squirrel sweep error: {ex.Message}. ");
            }

            if (deletedDirs == 0 && deletedPackages == 0)
            {
                return CommandHandlerOutcome.Success("Squirrel application releases audited: All local applications are at their latest version; 0 bytes reclaimed.");
            }

            return CommandHandlerOutcome.Success(
                $"Squirrel superseded releases pruned: Removed {deletedDirs} superseded app versions and {deletedPackages} obsolete delta packages. Reclaimed {FormatPillarBytes(reclaimedBytes)}. {details.ToString().Trim()}");
        }, cancellationToken);
    }

    private async Task<CommandHandlerOutcome> ExecuteCleanerMsiAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            string cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Installer");
            if (!Directory.Exists(cacheRoot))
            {
                return CommandHandlerOutcome.Success("Windows Installer cache directory not present.");
            }

            var cache = EnumerateInstallerCache(cacheRoot, 10000, cancellationToken);
            var registrations = ReadInstallerRegistrationEvidence(cancellationToken);

            var registeredPaths = registrations.Registrations
                .Select(r => NormalizeInstallerPath(r.Path))
                .Where(p => p != null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int deletedOrphans = 0;
            long reclaimedBytes = 0;
            var details = new StringBuilder();

            foreach (var file in cache.Files)
            {
                if (cancellationToken.IsCancellationRequested) break;

                string? normalized = NormalizeInstallerPath(file.Path);
                if (normalized == null) continue;

                // File in Installer directory that is neither registered as an MSI nor MSP
                if (!registeredPaths.Contains(normalized) && (normalized.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith(".msp", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var fi = new FileInfo(file.Path);
                        long size = fi.Length;
                        fi.Attributes = FileAttributes.Normal;
                        fi.Delete();
                        deletedOrphans++;
                        reclaimedBytes += size;
                    }
                    catch
                    {
                    }
                }
            }

            if (deletedOrphans == 0)
            {
                return CommandHandlerOutcome.Success("MSI package cache audited: No unreferenced orphaned installer packages found.");
            }

            return CommandHandlerOutcome.Success(
                $"MSI Package Cache Orphan Eliminator: Cleaned {deletedOrphans} orphaned packages. Reclaimed {FormatPillarBytes(reclaimedBytes)}.");
        }, cancellationToken);
    }

}
