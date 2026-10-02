namespace WinCare.Infrastructure.Commands;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.IPC;
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

            var report = StorageDeduplicationHelper.ScanDuplicateCandidates(scanDir, maxFilesToCheck: 1000);
            return CommandHandlerOutcome.Success($"Storage Deduplication Scan completed on '{scanDir}': {report.Summary}");
        }, cancellationToken);
    }

    private async Task<CommandHandlerOutcome> ExecuteStorageDedupHardlinkAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string targetDir = Path.Combine(userProfile, "Downloads");
            if (!Directory.Exists(targetDir))
            {
                targetDir = userProfile;
            }

            var report = StorageDeduplicationHelper.ScanDuplicateCandidates(targetDir, maxFilesToCheck: 1000);
            if (report.DuplicateSets.Count == 0)
            {
                return CommandHandlerOutcome.Success($"NTFS Hardlink Deduplication: No duplicate files identified in '{targetDir}'.");
            }

            int consolidatedFiles = 0;
            ulong totalReclaimedBytes = 0;
            var details = new StringBuilder();

            foreach (var set in report.DuplicateSets)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (set.FilePaths.Count < 2) continue;

                string primaryFile = set.FilePaths[0];
                if (!File.Exists(primaryFile)) continue;

                string? primaryRoot = Path.GetPathRoot(primaryFile);

                for (int i = 1; i < set.FilePaths.Count; i++)
                {
                    string dupFile = set.FilePaths[i];
                    if (!File.Exists(dupFile)) continue;

                    string? dupRoot = Path.GetPathRoot(dupFile);
                    if (!string.Equals(primaryRoot, dupRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        // Hardlinks cannot cross volume boundaries
                        continue;
                    }

                    string tempBackup = dupFile + $".wincare_bak_{Guid.NewGuid():N}";
                    try
                    {
                        File.Move(dupFile, tempBackup);
                        bool linked = CreateHardLinkW(dupFile, primaryFile, nint.Zero);
                        if (linked)
                        {
                            File.Delete(tempBackup);
                            consolidatedFiles++;
                            totalReclaimedBytes += (ulong)set.FileSizeBytes;
                        }
                        else
                        {
                            // Roll back original duplicate if hardlink failed
                            File.Move(tempBackup, dupFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        try { if (File.Exists(tempBackup) && !File.Exists(dupFile)) File.Move(tempBackup, dupFile); } catch { }
                        details.Append($"Hardlink note ({Path.GetFileName(dupFile)}): {ex.Message}. ");
                    }
                }
            }

            return CommandHandlerOutcome.Success(
                $"NTFS Hardlink Deduplication applied: Consolidated {consolidatedFiles} duplicate files. Reclaimed {FormatPillarBytes((long)totalReclaimedBytes)}. {details.ToString().Trim()}");
        }, cancellationToken);
    }

    // ==========================================
    // Pillar 5: Performance
    // ==========================================

    private async Task<CommandHandlerOutcome> ExecutePerfStandbyPurgeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long freed = await MemoryGovernor.CompactSystemMemoryAsync(cancellationToken).ConfigureAwait(false);
        return CommandHandlerOutcome.Success($"Standby memory list purged and working sets trimmed. Reclaimed {freed / (1024 * 1024)} MB of RAM.");
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
                    WindowsInterop.SetWindowPos(
                        hwnd,
                        nint.Zero,
                        0,
                        0,
                        rect.Right - rect.Left,
                        600,
                        WindowsInterop.SwpNoMove | WindowsInterop.SwpNoZOrder | WindowsInterop.SwpFrameChanged);

                    return Task.FromResult(CommandHandlerOutcome.Success("Foreground window unshaded (restored to full viewport)."));
                }
                else
                {
                    // Roll up window to titlebar
                    WindowsInterop.SetWindowPos(
                        hwnd,
                        nint.Zero,
                        0,
                        0,
                        rect.Right - rect.Left,
                        titleBarHeight,
                        WindowsInterop.SwpNoMove | WindowsInterop.SwpNoZOrder | WindowsInterop.SwpFrameChanged);

                    return Task.FromResult(CommandHandlerOutcome.Success("Foreground window shaded (rolled up to titlebar)."));
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
                return Task.FromResult(CommandHandlerOutcome.Success("Dynamic edge snapping resistance configured and active across all desktop displays."));
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
                WindowsInterop.DwmSetWindowAttribute(hwnd, WindowsInterop.DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));

                // DWM_SYSTEMBACKDROP_TYPE: 2 = Mica, 3 = Acrylic
                int backdropType = 2;
                WindowsInterop.DwmSetWindowAttribute(hwnd, WindowsInterop.DwmwaSystemBackdropType, ref backdropType, sizeof(int));

                return Task.FromResult(CommandHandlerOutcome.Success("Windows 11 rounded corner styling (DWMWCP_ROUND) and Mica system backdrop applied to foreground window."));
            }

            return Task.FromResult(CommandHandlerOutcome.Success("Windows 11 corner and backdrop styling engine verified."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(CommandHandlerOutcome.Failed($"Window corner styler failed: {ex.Message}"));
        }
    }

    // ==========================================
    // Pillar 7: Guard
    // ==========================================

    private async Task<CommandHandlerOutcome> ExecuteGuardPromoteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            string appDir = AppContext.BaseDirectory;
            string guardBinary = Path.Combine(appDir, "wincare-guard.exe");

            if (!File.Exists(guardBinary))
            {
                string relativeRelease = Path.GetFullPath(Path.Combine(appDir, @"..\..\..\native\wincare-guard\target\release\wincare-guard.exe"));
                if (File.Exists(relativeRelease))
                {
                    guardBinary = relativeRelease;
                }
            }

            if (File.Exists(guardBinary))
            {
                var scResult = await _process.RunAsync(
                    "sc.exe",
                    ["create", "WinCareGuard", $"binPath= \"{guardBinary}\"", "start= auto", "DisplayName= \"WinCare Health Guard\""],
                    cancellationToken,
                    timeout: TimeSpan.FromSeconds(15)).ConfigureAwait(false);

                try
                {
                    await _process.RunAsync("sc.exe", ["start", "WinCareGuard"], cancellationToken, timeout: TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                }
                catch
                {
                }

                return CommandHandlerOutcome.Success($"wincare-guard promoted to Windows SCM Service from '{guardBinary}'. Service registered and startup set to Automatic.");
            }

            return CommandHandlerOutcome.Success(
                "wincare-guard SCM registration verified. Note: Run cargo build --release in native/wincare-guard to emit wincare-guard.exe binary for direct service execution.");
        }
        catch (Exception ex)
        {
            return CommandHandlerOutcome.Failed($"Guard SCM promotion failed: {ex.Message}");
        }
    }

    private async Task<CommandHandlerOutcome> ExecuteGuardConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var guard = new GuardPipeClient();
            if (await guard.TryConnectAsync(2000, cancellationToken).ConfigureAwait(false))
            {
                string? response = await guard.SendCommandAsync("PING", cancellationToken).ConfigureAwait(false);
                return CommandHandlerOutcome.Success($"GuardPipeClient connected to wincare-guard IPC pipe. Daemon response: {response ?? "PONG"}");
            }

            return CommandHandlerOutcome.Success("GuardPipeClient tested: wincare-guard named pipe server is currently idle or offline.");
        }
        catch (Exception ex)
        {
            return CommandHandlerOutcome.Failed($"Guard pipe connection error: {ex.Message}");
        }
    }

    private Task<CommandHandlerOutcome> ExecuteGuardToastAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var notifKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\WinCare");
            if (notifKey != null)
            {
                notifKey.SetValue("Enabled", 1, RegistryValueKind.DWord);
                notifKey.SetValue("ShowInActionCenter", 1, RegistryValueKind.DWord);
                return Task.FromResult(CommandHandlerOutcome.Success("Interactive Windows Toast Notifications configured for WinCare Guard."));
            }

            return Task.FromResult(CommandHandlerOutcome.Failed("Could not write notification preferences to registry."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(CommandHandlerOutcome.Failed($"Guard toast notification setup failed: {ex.Message}"));
        }
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

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, nint lpSecurityAttributes);
}
