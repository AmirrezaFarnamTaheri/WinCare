using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Win32;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using WinCare.Application.Commands;

namespace WinCare.Infrastructure.Commands;

internal sealed partial class WindowsCommandExecutor
{
    private CommandHandlerOutcome StaticProfileCatalog(string profileFamily)
    {
        object[] profiles = profileFamily switch
        {
            "offline-reduction" =>
            [
                new { id = "conservative", title = "Conservative offline reduction", description = "Remove only explicitly named optional features or packages from an offline Windows image." },
                new { id = "audit-only", title = "Audit only", description = "Inspect candidates without changing the image." },
            ],
            "hardening" =>
            [
                new { id = "balanced", title = "Balanced hardening", description = "Keep UAC, firewall, LSA protection, and supported Defender policy enabled." },
                new { id = "hail-mary", title = "Strict hardening", description = "Balanced hardening plus stricter Defender policy; intended for recovery/hardening scenarios." },
            ],
            "experience-remote" =>
            [
                new { id = "local-only", title = "Local only", description = "No remote-control service changes; consent stays local." },
                new { id = "support", title = "Support session", description = "Time-bounded local consent record for remote support." },
            ],
            "experience-power" =>
            [
                new { id = "balanced", title = "Balanced", scheme = "381b4222-f694-41f0-9685-ff5bb260df2e" },
                new { id = "high-performance", title = "High performance", scheme = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" },
                new { id = "power-saver", title = "Power saver", scheme = "a1841308-3541-4fab-bc81-f71556f20b4a" },
            ],
            "experience-privacy" => WinCare.CommandCatalog.RemediationCatalog.LoadPresets().Where(x => x.Id.Equals("privacy", StringComparison.OrdinalIgnoreCase)).Select(x => (object)new { x.Id, x.Title, x.Description, x.RuleIds }).ToArray(),
            "cleaner-relocation" =>
            [
                new { id = "move", title = "Move with verification", description = "Copy, verify byte count, then remove source only after destination is complete." },
                new { id = "copy", title = "Copy only", description = "Copy data without removing source." },
            ],
            "file-preview" =>
            [
                new { id = "safe-text", title = "Safe text", maximumBytes = 262144 },
                new { id = "metadata", title = "Metadata only", maximumBytes = 67108864 },
            ],
            "deep-clean" =>
            [
                new { id = "temp", title = "Temporary files", targets = new[] { "UserTemp", "WindowsTemp" } },
                new { id = "wincare-cache", title = "WinCare cache", targets = new[] { "WinCareCache" } },
            ],
            "appx-selection" =>
            [
                new { id = "explicit", title = "Explicit package list", description = "Only packages named by PackageNames are admitted." },
            ],
            "legacy-unsafe" =>
            [
                new { id = "flush-dns", title = "Flush DNS cache", description = "Bounded, reversible-by-repopulation network cache operation." },
                new { id = "restart-explorer", title = "Restart Explorer shell", description = "Restart current user's Explorer shell only." },
            ],
            _ => [new { id = "default", title = profileFamily, description = "Native WinCare profile family." }],
        };
        return Success(profileFamily + "-profiles", $"Returned {profiles.Length} native {profileFamily} profiles.", profiles);
    }

    private CommandHandlerOutcome ExperienceCards(CommandParameters p)
    {
        DriveInfo[] drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToArray();
        long total = drives.Sum(d => d.TotalSize);
        long free = drives.Sum(d => d.AvailableFreeSpace);
        WindowsInterop.MemoryStatusEx memory = WindowsInterop.ReadMemoryStatus();
        var cards = new object[]
        {
            new { id = "storage", title = "Storage", value = total == 0 ? "Unknown" : $"{free * 100d / total:0.#}% free", status = total > 0 && free * 100d / total < 10 ? "Attention" : "Healthy" },
            new { id = "memory", title = "Memory", value = $"{memory.MemoryLoad}% used", status = memory.MemoryLoad >= 90 ? "Attention" : "Healthy" },
            new { id = "security", title = "Security", value = IsAdministrator() ? "Elevated session" : "Standard session", status = "Informational" },
            new { id = "network", title = "Network", value = NetworkInterface.GetIsNetworkAvailable() ? "Connected" : "Offline", status = NetworkInterface.GetIsNetworkAvailable() ? "Healthy" : "Attention" },
        };
        return Success("experience-cards", "Experience overview cards built from live host state.", cards);
    }

    private CommandHandlerOutcome FileDescriptor(CommandParameters p, string parameterName)
    {
        string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(p.RequiredString(parameterName)));
        if (!File.Exists(path) && !Directory.Exists(path)) throw new CommandParameterException(parameterName, "Path does not exist.");
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            return Success("experience-visual-asset", "File descriptor read.", new { path, kind = "File", info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc, attributes = info.Attributes.ToString(), sha256 = info.Length <= 64L * 1024 * 1024 ? HashFile(path) : null });
        }
        var directory = new DirectoryInfo(path);
        return Success("experience-visual-asset", "Directory descriptor read.", new { path, kind = "Directory", directory.CreationTimeUtc, directory.LastWriteTimeUtc, attributes = directory.Attributes.ToString() });
    }

    private CommandHandlerOutcome SpriteLayout(CommandParameters p)
    {
        int frameWidth = p.Int32("FrameWidth", 0, 1, 16384);
        int frameHeight = p.Int32("FrameHeight", 0, 1, 16384);
        int sheetWidth = p.Int32("SheetWidth", 0, 1, 65536);
        int sheetHeight = p.Int32("SheetHeight", 0, 1, 65536);
        if (sheetWidth % frameWidth != 0 || sheetHeight % frameHeight != 0) throw new CommandParameterException("FrameWidth", "Sheet dimensions must divide evenly by frame dimensions.");
        int columns = sheetWidth / frameWidth; int rows = sheetHeight / frameHeight;
        return Success("experience-sprite-layout", "Sprite sheet layout calculated.", new { sheetWidth, sheetHeight, frameWidth, frameHeight, columns, rows, frameCount = columns * rows });
    }

    private CommandHandlerOutcome LocationState()
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        RegionInfo? region = null; try { region = new RegionInfo(culture.Name); } catch (ArgumentException) { }
        return Success("experience-location-state", "Current locale, region, and time zone inspected.", new { culture = culture.Name, uiCulture = CultureInfo.CurrentUICulture.Name, region = region?.Name, regionEnglishName = region?.EnglishName, timeZone = TimeZoneInfo.Local.Id, utcOffset = TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now).ToString() });
    }

    private CommandHandlerOutcome ExperienceLocationSet(CommandParameters p)
    {
        int geoId = p.Int32("GeoId", 0, 1, int.MaxValue);
        if (!WindowsInterop.SetUserGeoID(geoId)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SetUserGeoID failed.");
        return Success("experience-location-set", "Windows home geographic location updated.", new { geoId }, undo: false);
    }

    private async Task<CommandHandlerOutcome> PowerAssessmentAsync(CancellationToken cancellationToken)
    {
        ProcessExecutionResult active = await _process.RunAsync("powercfg.exe", ["/getactivescheme"], cancellationToken).ConfigureAwait(false);
        ProcessExecutionResult schemes = await _process.RunAsync("powercfg.exe", ["/list"], cancellationToken).ConfigureAwait(false);
        return Success("experience-power-assess", "Windows power schemes assessed with powercfg.", new { active = active.StandardOutput.Trim(), schemes = schemes.StandardOutput });
    }

    private async Task<CommandHandlerOutcome> ExperiencePowerApplyAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string profile = p.String("ProfileId", "balanced");
        string scheme = profile.ToLowerInvariant() switch
        {
            "balanced" => "381b4222-f694-41f0-9685-ff5bb260df2e",
            "high-performance" => "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
            "power-saver" => "a1841308-3541-4fab-bc81-f71556f20b4a",
            _ => p.String("Scheme").Trim(),
        };
        if (!Guid.TryParse(scheme, out _)) throw new CommandParameterException("ProfileId", "ProfileId must be balanced, high-performance, power-saver, or Scheme must be a GUID.");
        ProcessExecutionResult result = await _process.RunAsync("powercfg.exe", ["/setactive", scheme], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) return Block("experience-power-apply", $"powercfg rejected the selected scheme: {FirstLine(result.StandardError)}");
        return Success("experience-power-apply", "Windows power scheme activated.", new { profile, scheme }, undo: false);
    }

    private CommandHandlerOutcome DownloadStrategy()
    {
        return Success("experience-download-strategy", "Native download strategy described from runtime limits.", new { protocols = new[] { "https", "http" }, maximumBytesPerJob = 2L * 1024 * 1024 * 1024, checksum = "SHA-256 optional/verified when supplied", retries = "caller-controlled; no hidden infinite retries", cancellation = true, temporaryFileThenAtomicMove = true });
    }

    private async Task<CommandHandlerOutcome> ExperiencePrivacyApplyAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string profile = p.String("ProfileId", "privacy");
        if (!profile.Equals("privacy", StringComparison.OrdinalIgnoreCase)) throw new CommandParameterException("ProfileId", "Only the reviewed 'privacy' remediation preset is admitted by this command.");
        return await ApplyPresetAsync(new CommandParameters(Data(new { PresetId = "privacy" })), cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandHandlerOutcome> ExperienceVisualManifestAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(p.RequiredString("Path")));
        JsonElement entries = p.Element("Entries");
        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 5000) throw new CommandParameterException("Entries", "Entries must be an array with at most 5000 items.");
        await WriteJsonExportAsync(path, entries, cancellationToken).ConfigureAwait(false);
        return Success("experience-visual-manifest", "Visual manifest written atomically.", new { path, entries = entries.GetArrayLength() }, undo: false);
    }

    private CommandHandlerOutcome ShellHardwareCards()
    {
        WindowsInterop.MemoryStatusEx memory = WindowsInterop.ReadMemoryStatus();
        DriveInfo[] drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToArray();
        return Success("shell-hardware-cards", "Hardware summary cards generated from native host APIs.", new object[]
        {
            new { id = "cpu", label = "CPU", value = $"{Environment.ProcessorCount} logical processors" },
            new { id = "memory", label = "Memory", value = $"{memory.TotalPhys / (1024d*1024*1024):0.##} GiB" },
            new { id = "storage", label = "Storage", value = $"{drives.Sum(d => d.TotalSize) / (1024d*1024*1024):0.#} GiB across {drives.Length} ready volumes" },
            new { id = "display", label = "Displays", value = MonitorControls().Data },
        });
    }

    private CommandHandlerOutcome HardwareInventory(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using RegistryKey? bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        using RegistryKey? cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        WindowsInterop.MemoryStatusEx memory = WindowsInterop.ReadMemoryStatus();
        object[] drives = DriveInfo.GetDrives().Select(d => { try { return (object)new { d.Name, type = d.DriveType.ToString(), ready = d.IsReady, totalBytes = d.IsReady ? d.TotalSize : 0L, freeBytes = d.IsReady ? d.AvailableFreeSpace : 0L }; } catch (IOException ex) { return (object)new { d.Name, error = ex.Message }; } }).ToArray();
        object[] adapters = NetworkInterface.GetAllNetworkInterfaces().Select(n => new { n.Id, n.Name, type = n.NetworkInterfaceType.ToString(), n.Description, n.OperationalStatus, mac = n.GetPhysicalAddress().ToString() }).ToArray();
        return Success("hardware-inventory", "Hardware inventory collected from native Windows/BCL surfaces.", new
        {
            machine = Environment.MachineName,
            os = Environment.OSVersion.VersionString,
            architecture = RuntimeInformation.OSArchitecture.ToString(),
            cpu = new { name = Convert.ToString(cpu?.GetValue("ProcessorNameString")), vendor = Convert.ToString(cpu?.GetValue("VendorIdentifier")), logicalProcessors = Environment.ProcessorCount },
            memory = new { memory.TotalPhys, memory.AvailPhys, memory.MemoryLoad },
            bios = RegistryValues(bios),
            drives,
            adapters,
            displays = MonitorInventory().Data,
        });
    }

    private async Task<CommandHandlerOutcome> HardwareReportAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        CommandHandlerOutcome inventory = HardwareInventory(cancellationToken);
        JsonElement data = inventory.Data ?? Data(new { });
        string path = _state.ResolveExportPath(p.String("Path"), "wincare-hardware-report.json");
        await WriteJsonExportAsync(path, data, cancellationToken).ConfigureAwait(false);
        return Success("hardware-report", "Hardware report exported.", new { path });
    }

    private CommandHandlerOutcome AppxRuntime()
    {
        string windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        using RegistryKey? packages = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
        string[] packageKeys = packages?.GetSubKeyNames().Take(5000).ToArray() ?? [];
        return Success("appx-runtime", "AppX/MSIX runtime repository inspected.", new { windowsAppsPresent = Directory.Exists(windowsApps), registeredPackageCount = packageKeys.Length, packageKeys });
    }

    private CommandHandlerOutcome AppxLaunchTargets(CommandParameters p)
    {
        string query = p.String("Query").Trim();
        using RegistryKey? packages = Registry.CurrentUser.OpenSubKey(@"Software\Classes\ActivatableClasses\Package");
        var rows = new List<object>();
        if (packages is not null)
        {
            foreach (string packageName in packages.GetSubKeyNames().Take(5000))
            {
                if (query.Length > 0 && !packageName.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                using RegistryKey? package = packages.OpenSubKey(packageName + @"\Server");
                rows.Add(new { packageName, activationServerCount = package?.SubKeyCount ?? 0 });
            }
        }
        return Success("appx-launch-targets", $"Found {rows.Count} matching AppX activation packages.", rows);
    }

    private CommandHandlerOutcome AppxLaunch(CommandParameters p)
    {
        string appUserModelId = p.RequiredString("AppUserModelId");
        if (appUserModelId.Length > 512 || appUserModelId.IndexOfAny(['\r', '\n', '"']) >= 0) throw new CommandParameterException("AppUserModelId", "AppUserModelId contains unsupported characters.");
        string target = "shell:AppsFolder\\" + appUserModelId;
        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = false, ArgumentList = { target } });
        return Success("appx-launch", "AppX application activation requested through shell:AppsFolder.", new { appUserModelId, target });
    }

    private CommandHandlerOutcome ArchiveInspect(CommandParameters p)
    {
        string path = RequireExistingFile(p.RequiredString("Path"), 1024L * 1024 * 1024);
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return Block("archive-inspect", "Native archive inspection currently admits ZIP containers only; unsupported formats are not guessed or executed.");
        using ZipArchive archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count > 100_000) throw new InvalidDataException("ZIP contains more than 100,000 entries.");
        var rows = archive.Entries.Take(p.Int32("Limit", 5000, 1, 100000)).Select(entry => new { entry.FullName, entry.Length, entry.CompressedLength, entry.LastWriteTime }).ToArray();
        long uncompressed = archive.Entries.Sum(entry => entry.Length);
        return Success("archive-inspect", $"ZIP inspected: {archive.Entries.Count} entries.", new { path, entryCount = archive.Entries.Count, totalUncompressedBytes = uncompressed, entries = rows });
    }

    private CommandHandlerOutcome TerminalEnvironment()
    {
        var env = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>().Select(entry => new { name = Convert.ToString(entry.Key), value = Convert.ToString(entry.Value) }).OrderBy(entry => entry.name, StringComparer.OrdinalIgnoreCase).ToArray();
        var shells = new[] { "cmd.exe", "wt.exe", "wezterm.exe", "bash.exe", "wsl.exe" }.Select(name => new { name, path = FindExecutable(name) }).ToArray();
        return Success("terminal-environment", "Terminal environment and shell capabilities inspected.", new { currentDirectory = Environment.CurrentDirectory, shells, variables = env });
    }

    private async Task<CommandHandlerOutcome> TerminalExportAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        JsonElement data = TerminalEnvironment().Data ?? Data(new { });
        string path = _state.ResolveExportPath(p.String("Path"), "wincare-terminal-environment.json");
        await WriteJsonExportAsync(path, data, cancellationToken).ConfigureAwait(false);
        return Success("terminal-export", "Terminal environment exported.", new { path });
    }

    private CommandHandlerOutcome GuiResourceAudit()
    {
        using Process current = Process.GetCurrentProcess();
        uint gdi = WindowsInterop.GetGuiResources(current.Handle, 0);
        uint user = WindowsInterop.GetGuiResources(current.Handle, 1);
        return Success("gui-resource-audit", "WinCare GUI resource usage inspected.", new { processId = current.Id, gdiObjects = gdi, userObjects = user, handleCount = SafeHandleCount(current) });
    }

    private CommandHandlerOutcome FullscreenShellAssess()
    {
        nint foreground = WindowsInterop.GetForegroundWindow();
        if (foreground == 0) return Success("fullscreen-shell-assess", "No foreground window available.", new { foreground = false });
        WindowsInterop.GetWindowRect(foreground, out WindowsInterop.Rect rect);
        int screenWidth = WindowsInterop.GetSystemMetrics(0); int screenHeight = WindowsInterop.GetSystemMetrics(1);
        bool fullscreen = rect.Left <= 0 && rect.Top <= 0 && rect.Right >= screenWidth && rect.Bottom >= screenHeight;
        return Success("fullscreen-shell-assess", "Foreground shell geometry assessed.", new { handle = foreground.ToInt64(), rect.Left, rect.Top, rect.Right, rect.Bottom, screenWidth, screenHeight, fullscreen });
    }

    private async Task<CommandHandlerOutcome> CleanerPreviewCardsAsync(CancellationToken cancellationToken)
    {
        CommandHandlerOutcome targets = await CleanupTargetsAsync(cancellationToken).ConfigureAwait(false);
        return Success("cleaner-preview-cards", "Cleanup preview generated from measured native targets.", new { targets = targets.Data, safety = "Preview only; no files removed." });
    }

    private CommandHandlerOutcome CleanerDiskPressure(CommandParameters p, CancellationToken cancellationToken)
    {
        int olderThanDays = p.Int32("OlderThanDays", 7, 0, 3650);
        DateTime threshold = DateTime.UtcNow.AddDays(-olderThanDays);
        string[] roots = [Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")];
        long freed = 0; int removed = 0; int skipped = 0;
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            foreach (string file in Directory.EnumerateFiles(root, "*", SafeRecursiveEnumeration).Take(200_000))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    FileInfo info = new(file);
                    if (info.LastWriteTimeUtc > threshold || (info.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                    long length = info.Length; info.Delete(); freed += length; removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
            }
        }
        return Success("cleaner-disk-pressure", $"Removed {removed} eligible temporary files.", new { olderThanDays, removed, skipped, freedBytes = freed }, undo: false);
    }

    private CommandHandlerOutcome Winapp2Admission(CommandParameters p)
    {
        string path = RequireExistingFile(p.RequiredString("Path"), 16 * 1024 * 1024);
        int sections = File.ReadLines(path).Count(line => line.TrimStart().StartsWith("[", StringComparison.Ordinal) && line.TrimEnd().EndsWith("]", StringComparison.Ordinal));
        bool containsExecutableDirective = File.ReadLines(path).Any(line => line.Contains("Run=", StringComparison.OrdinalIgnoreCase) || line.Contains("Command=", StringComparison.OrdinalIgnoreCase));
        return Success("cleaner-winapp2", "Winapp2-style INI inspected for admission.", new { path, sections, containsExecutableDirective, admitted = sections > 0 && !containsExecutableDirective });
    }

    private CommandHandlerOutcome CleanerWinapp2Run(CommandParameters p, CancellationToken cancellationToken)
    {
        string path = RequireExistingFile(p.RequiredString("Path"), 16 * 1024 * 1024);
        if (Winapp2Admission(new CommandParameters(Data(new { Path = path }))).Data is not JsonElement admission || !admission.TryGetProperty("admitted", out JsonElement admitted) || !admitted.GetBoolean()) return Block("cleaner-winapp2-run", "Winapp2 file was not admitted: executable directives or missing sections detected.");
        string[] allowedRoots = [Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")];
        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in File.ReadLines(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string trimmed = line.Trim();
            if (trimmed.StartsWith("FileKey", StringComparison.OrdinalIgnoreCase) && trimmed.Contains('='))
            {
                string value = trimmed[(trimmed.IndexOf('=') + 1)..];
                string[] parts = value.Split('|');
                if (parts.Length >= 2) patterns.Add(parts[1].Trim());
            }
        }
        long freed = 0; int removed = 0;
        foreach (string root in allowedRoots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (string pattern in patterns.Where(IsSafeFilePattern))
            {
                foreach (string file in Directory.EnumerateFiles(root, pattern, SafeRecursiveEnumeration).Take(100_000))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { var info = new FileInfo(file); if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue; long len = info.Length; info.Delete(); freed += len; removed++; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
        }
        return Success("cleaner-winapp2-run", "Admitted Winapp2 file patterns applied only inside WinCare temporary-file boundaries.", new { removed, freedBytes = freed, patterns = patterns.ToArray() });
    }

    private CommandHandlerOutcome CleanerRelocationAssess(CommandParameters p)
    {
        string source = RequireExistingDirectory(p.RequiredString("Source"));
        string destination = Path.GetFullPath(Environment.ExpandEnvironmentVariables(p.RequiredString("Destination")));
        long bytes = (long)SumDirectoryBytes(source, CancellationToken.None, 500_000);
        string destinationRoot = Path.GetPathRoot(destination) ?? destination;
        DriveInfo? drive = DriveInfo.GetDrives().FirstOrDefault(d => destinationRoot.StartsWith(d.Name, StringComparison.OrdinalIgnoreCase) && d.IsReady);
        return Success("cleaner-relocation-assess", "Relocation source and destination capacity assessed.", new { source, destination, bytes, destinationFreeBytes = drive?.AvailableFreeSpace, enoughSpace = drive is null || drive.AvailableFreeSpace > bytes });
    }

    private async Task<CommandHandlerOutcome> CleanerRelocationAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string source = RequireExistingDirectory(p.RequiredString("Source"));
        string destination = Path.GetFullPath(Environment.ExpandEnvironmentVariables(p.RequiredString("Destination")));
        bool move = p.Boolean("Move", true);
        if (IsSubPath(source, destination) || IsSubPath(destination, source)) throw new CommandParameterException("Destination", "Source and destination must not contain one another.");
        long sourceBytes = (long)SumDirectoryBytes(source, cancellationToken, 500_000);
        long copied = await CopyDirectoryAsync(source, destination, overwrite: p.Boolean("Overwrite"), cancellationToken).ConfigureAwait(false);
        long destinationBytes = (long)SumDirectoryBytes(destination, cancellationToken, 500_000);
        if (destinationBytes < sourceBytes || copied < sourceBytes) return CommandHandlerOutcome.Failed("cleaner-relocation.verification_failed", "Destination verification failed; source was left untouched.");
        if (move) DeleteDirectoryTreeSafe(source, cancellationToken);
        return Success("cleaner-relocation", move ? "Relocation copied, verified, then removed source." : "Relocation copy completed and verified.", new { source, destination, bytes = copied, moved = move }, undo: !move);
    }

    private CommandHandlerOutcome FilePreview(CommandParameters p)
    {
        string path = RequireExistingFile(p.RequiredString("Path"), 512L * 1024 * 1024);
        FileInfo info = new(path);
        string extension = info.Extension.ToLowerInvariant();
        int maxBytes = p.Int32("MaximumBytes", 262144, 1024, 1048576);
        if (new[] { ".txt", ".log", ".md", ".json", ".xml", ".csv", ".ini", ".yaml", ".yml", ".cs", ".rs" }.Contains(extension))
        {
            int requested = (int)Math.Min(info.Length, maxBytes);
            byte[] bytes = new byte[requested];
            using (FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan))
            {
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read == 0) break;
                    offset += read;
                }
                if (offset != bytes.Length) Array.Resize(ref bytes, offset);
            }
            string text = Encoding.UTF8.GetString(bytes);
            return Success("file-preview", "Text preview read with a strict byte limit.", new { path, info.Length, text, truncated = info.Length > bytes.Length });
        }
        if (extension == ".zip") return ArchiveInspect(new CommandParameters(Data(new { Path = path, Limit = 200 })));
        return Success("file-preview", "Binary file preview limited to metadata and digest.", new { path, info.Length, extension, sha256 = info.Length <= 64L * 1024 * 1024 ? HashFile(path) : null });
    }

    private async Task<CommandHandlerOutcome> FilePreviewExportAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        CommandHandlerOutcome preview = FilePreview(p);
        string output = _state.ResolveExportPath(p.String("OutputPath"), "wincare-file-preview.json");
        JsonElement data = preview.Data ?? Data(new { });
        await WriteJsonExportAsync(output, data, cancellationToken).ConfigureAwait(false);
        return Success("file-preview-export", "Safe file preview exported.", new { path = output });
    }

    private CommandHandlerOutcome PreviewHandlerAssessment()
    {
        using RegistryKey? handlers = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers");
        var rows = handlers?.GetValueNames().Select(name => new { clsid = name, description = Convert.ToString(handlers.GetValue(name)) }).ToArray() ?? [];
        return Success("preview-handlers", $"Enumerated {rows.Length} registered preview handlers.", rows);
    }

    private CommandHandlerOutcome PeerLogTail(CommandParameters p)
    {
        string path = RequireExistingFile(p.RequiredString("Path"), 1024L * 1024 * 1024);
        int lines = p.Int32("Lines", 200, 1, 5000);
        string[] tail = ReadTailLines(path, lines, 2 * 1024 * 1024);
        return Success("peer-log-tail", $"Read last {tail.Length} lines from log.", new { path, lines = tail });
    }

    private CommandHandlerOutcome PeInspection(CommandParameters p)
    {
        string path = RequireExistingFile(p.RequiredString("Path"), 1024L * 1024 * 1024);
        using FileStream stream = File.OpenRead(path);
        using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
        if (!reader.HasMetadata && reader.PEHeaders.PEHeader is null) return Block("peer-pe", "File is not a valid Portable Executable image.");
        var pe = reader.PEHeaders.PEHeader;
        return Success("peer-pe", "Portable Executable headers inspected without loading the binary.", new { path, machine = reader.PEHeaders.CoffHeader.Machine.ToString(), sections = reader.PEHeaders.SectionHeaders.Select(section => new { section.Name, section.VirtualSize, section.SizeOfRawData, characteristics = section.SectionCharacteristics.ToString() }).ToArray(), subsystem = pe?.Subsystem.ToString(), imageBase = pe?.ImageBase, entryPointRva = pe?.AddressOfEntryPoint, hasManagedMetadata = reader.HasMetadata, sha256 = HashFile(path) });
    }

    private CommandHandlerOutcome AppxSelectionAssess(CommandParameters p)
    {
        IReadOnlyList<string> packages = p.StringArray("PackageNames");
        if (packages.Count == 0) throw new CommandParameterException("PackageNames", "Provide at least one exact package name.");
        return Success("appx-selection-assess", "Explicit AppX package selection validated.", new { packages, exactMatchOnly = true, wildcardExpansion = false, destructiveScope = "selected packages only" });
    }

    private async Task<CommandHandlerOutcome> AppxSelectionAsync(CommandParameters p, bool online, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> packages = p.StringArray("PackageNames");
        if (packages.Count == 0) throw new CommandParameterException("PackageNames", "Provide at least one exact package name.");
        var results = new List<object>();
        foreach (string package in packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (package.IndexOfAny(['*', '?', '"', '\r', '\n']) >= 0) throw new CommandParameterException("PackageNames", "Package names must be exact; wildcards are not admitted.");
            ProcessExecutionResult result;
            if (online)
            {
                string winget = FindExecutable("winget.exe") ?? throw new CommandDependencyException("winget.exe", "winget.exe is required for online AppX selection removal.");
                result = await _process.RunAsync(winget, ["uninstall", "--id", package, "--exact", "--silent", "--accept-source-agreements"], cancellationToken, TimeSpan.FromMinutes(5)).ConfigureAwait(false);
            }
            else
            {
                string image = RequireExistingDirectory(p.RequiredString("ImagePath"));
                result = await _process.RunAsync("dism.exe", ["/Image:" + image, "/Remove-ProvisionedAppxPackage", "/PackageName:" + package, "/English"], cancellationToken, TimeSpan.FromMinutes(5)).ConfigureAwait(false);
            }
            results.Add(new { package, result.ExitCode, result.StandardOutput, result.StandardError });
            if (result.ExitCode != 0) return CommandHandlerOutcome.Failed("appx-selection.failed", $"Package operation failed for '{package}'.");
        }
        return Success(online ? "appx-selection" : "offline-appx-selection", "Selected AppX package operations completed.", results, undo: false);
    }

    private CommandHandlerOutcome ExplorerQuick(CommandParameters p)
    {
        string path = RequireExistingDirectory(p.RequiredString("Path"));
        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = false, ArgumentList = { path } });
        return Success("explorer-quick", "Explorer opened at validated directory.", new { path });
    }

    private async Task<CommandHandlerOutcome> OfflineReductionAssessAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string image = RequireExistingDirectory(p.RequiredString("ImagePath"));
        ProcessExecutionResult features = await _process.RunAsync("dism.exe", ["/Image:" + image, "/Get-Features", "/English"], cancellationToken, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        ProcessExecutionResult packages = await _process.RunAsync("dism.exe", ["/Image:" + image, "/Get-Packages", "/English"], cancellationToken, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        return Success("offline-reduction-assess", "Offline Windows image reduction candidates enumerated.", new { image, features = features.StandardOutput, packages = packages.StandardOutput });
    }

    private async Task<CommandHandlerOutcome> OfflineReductionApplyAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string image = RequireExistingDirectory(p.RequiredString("ImagePath"));
        IReadOnlyList<string> features = p.StringArray("DisableFeatures");
        IReadOnlyList<string> packages = p.StringArray("RemovePackages");
        if (features.Count + packages.Count == 0) throw new CommandParameterException("DisableFeatures", "Provide DisableFeatures and/or RemovePackages explicitly.");
        var results = new List<object>();
        foreach (string feature in features)
        {
            ProcessExecutionResult result = await _process.RunAsync("dism.exe", ["/Image:" + image, "/Disable-Feature", "/FeatureName:" + feature, "/NoRestart", "/English"], cancellationToken, TimeSpan.FromMinutes(5)).ConfigureAwait(false);
            results.Add(new { type = "feature", feature, result.ExitCode }); if (result.ExitCode is not (0 or 3010)) return CommandHandlerOutcome.Failed("offline-reduction-apply.failed", $"DISM failed disabling '{feature}'.");
        }
        foreach (string package in packages)
        {
            ProcessExecutionResult result = await _process.RunAsync("dism.exe", ["/Image:" + image, "/Remove-Package", "/PackageName:" + package, "/NoRestart", "/English"], cancellationToken, TimeSpan.FromMinutes(5)).ConfigureAwait(false);
            results.Add(new { type = "package", package, result.ExitCode }); if (result.ExitCode is not (0 or 3010)) return CommandHandlerOutcome.Failed("offline-reduction-apply.failed", $"DISM failed removing '{package}'.");
        }
        return Success("offline-reduction-apply", "Explicit offline image reductions completed.", results, undo: false);
    }

    private async Task<CommandHandlerOutcome> WorkspaceLayoutSaveAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string id = p.String("Id").Trim(); if (id.Length == 0) id = Guid.NewGuid().ToString("N");
        WindowRecord[] windows = EnumerateWindows().ToArray();
        JsonElement record = Data(new { id, name = p.String("Name", $"Layout {DateTime.Now:g}"), savedAt = DateTimeOffset.UtcNow, windows });
        await AppendStateItemAsync("workspace-layouts", record, cancellationToken).ConfigureAwait(false);
        return Success("workspace-layout-save", "Top-level native window layout saved.", record, undo: false);
    }

    private async Task<CommandHandlerOutcome> WorkspaceLayoutApplyAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string id = p.RequiredString("Id");
        JsonElement layout = await FindStateItemAsync("workspace-layouts", id, cancellationToken).ConfigureAwait(false);
        if (!layout.TryGetProperty("windows", out JsonElement windows) || windows.ValueKind != JsonValueKind.Array) return Block("workspace-layout-apply", "Saved workspace has no windows.");
        Dictionary<string, WindowRecord> current = EnumerateWindows().GroupBy(w => w.Title, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        int moved = 0;
        foreach (JsonElement desired in windows.EnumerateArray())
        {
            string title = desired.TryGetProperty("Title", out JsonElement titleUpper) ? titleUpper.GetString() ?? string.Empty : desired.TryGetProperty("title", out JsonElement titleLower) ? titleLower.GetString() ?? string.Empty : string.Empty;
            if (title.Length == 0 || !current.TryGetValue(title, out WindowRecord? window)) continue;
            int x = GetJsonInt(desired, "X", "x"); int y = GetJsonInt(desired, "Y", "y"); int width = GetJsonInt(desired, "Width", "width"); int height = GetJsonInt(desired, "Height", "height");
            if (width < 100 || height < 100) continue;
            if (WindowsInterop.SetWindowPos(new nint(window.Handle), 0, x, y, width, height, WindowsInterop.SwpShowWindow)) moved++;
        }
        return Success("workspace-layout-apply", $"Applied saved bounds to {moved} matching windows.", new { id, moved }, undo: false);
    }

    private CommandHandlerOutcome DeepClean(CommandParameters p, CancellationToken cancellationToken)
    {
        string profile = p.String("ProfileId", "temp");
        if (!profile.Equals("temp", StringComparison.OrdinalIgnoreCase) && !profile.Equals("wincare-cache", StringComparison.OrdinalIgnoreCase)) throw new CommandParameterException("ProfileId", "ProfileId must be temp or wincare-cache.");
        if (profile.Equals("temp", StringComparison.OrdinalIgnoreCase)) return CleanerDiskPressure(new CommandParameters(Data(new { OlderThanDays = p.Int32("OlderThanDays", 7, 0, 3650) })), cancellationToken);
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinCare", "cache");
        long bytes = Directory.Exists(cache) ? (long)SumDirectoryBytes(cache, cancellationToken, 200_000) : 0;
        if (Directory.Exists(cache)) DeleteDirectoryTreeSafe(cache, cancellationToken);
        Directory.CreateDirectory(cache);
        return Success("deep-clean", "WinCare-owned cache cleared.", new { cache, freedBytes = bytes }, undo: false);
    }

    private async Task<CommandHandlerOutcome> LegacyUnsafeAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string action = p.RequiredString("Action");
        if (action.Equals("flush-dns", StringComparison.OrdinalIgnoreCase))
        {
            ProcessExecutionResult result = await _process.RunAsync("ipconfig.exe", ["/flushdns"], cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0 ? Success("legacy-unsafe", "DNS resolver cache flushed.", result) : Block("legacy-unsafe", FirstLine(result.StandardError));
        }
        if (action.Equals("restart-explorer", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Process process in Process.GetProcessesByName("explorer"))
            {
                using (process)
                {
                    try
                    {
                        process.Kill(entireProcessTree: false);
                        using CancellationTokenSource stopTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        stopTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                        await process.WaitForExitAsync(stopTimeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
            }
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = false });
            return Success("legacy-unsafe", "Explorer shell restarted for current interactive session.", new { action });
        }
        return Block("legacy-unsafe", "Requested legacy action is not in the native allowlist. WinCare intentionally refuses arbitrary legacy command execution.");
    }

    private async Task<CommandHandlerOutcome> SandboxConfigAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(p.RequiredString("Path")));
        if (!path.EndsWith(".wsb", StringComparison.OrdinalIgnoreCase)) throw new CommandParameterException("Path", "Sandbox configuration output must end in .wsb.");
        bool networking = p.Boolean("Networking", true); bool clipboard = p.Boolean("ClipboardRedirection", false);
        JsonElement mapped = p.OptionalElement("MappedFolders") ?? Data(Array.Empty<object>());
        if (mapped.ValueKind != JsonValueKind.Array || mapped.GetArrayLength() > 20) throw new CommandParameterException("MappedFolders", "MappedFolders must be an array of at most 20 entries.");
        var root = new XElement("Configuration", new XElement("Networking", networking ? "Enable" : "Disable"), new XElement("ClipboardRedirection", clipboard ? "Enable" : "Disable"));
        if (mapped.GetArrayLength() > 0)
        {
            var folders = new XElement("MappedFolders");
            foreach (JsonElement item in mapped.EnumerateArray())
            {
                string host = item.TryGetProperty("HostFolder", out JsonElement h) ? h.GetString() ?? string.Empty : string.Empty;
                host = RequireExistingDirectory(host);
                bool readOnly = !item.TryGetProperty("ReadOnly", out JsonElement ro) || ro.GetBoolean();
                folders.Add(new XElement("MappedFolder", new XElement("HostFolder", host), new XElement("ReadOnly", readOnly ? "true" : "false")));
            }
            root.Add(folders);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp"; await File.WriteAllTextAsync(temp, new XDocument(root).ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false); File.Move(temp, path, overwrite: true);
        return Success("sandbox-config", "Windows Sandbox configuration written from validated settings.", new { path, networking, clipboard, mappedFolders = mapped.GetArrayLength() }, undo: false);
    }

    private CommandHandlerOutcome CapabilityRegistry(CancellationToken cancellationToken)
    {
        string[] tools = ["dism.exe", "bcdedit.exe", "netsh.exe", "logman.exe", "wevtutil.exe", "winget.exe", "CiTool.exe", "sysmon.exe", "wireguard.exe", "wg.exe", "ebpfnetsh.exe"];
        cancellationToken.ThrowIfCancellationRequested();
        var capabilities = tools.Select(tool => { string? path = FindExecutable(tool); return new { tool, path, available = path is not null }; }).ToArray();
        return Success("capabilities", "Native runtime capability registry built from current host.", new { os = Environment.OSVersion.VersionString, build = Environment.OSVersion.Version.Build, architecture = RuntimeInformation.OSArchitecture.ToString(), administrator = IsAdministrator(), capabilities });
    }

    private async Task<CommandHandlerOutcome> FleetInventoryAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> hosts = p.StringArray("Hosts");
        if (hosts.Count == 0) hosts = [Environment.MachineName];
        if (hosts.Count > 100) throw new CommandParameterException("Hosts", "At most 100 hosts can be probed per request.");
        var rows = new List<object>();
        using var ping = new Ping();
        foreach (string host in hosts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false); PingReply reply = await ping.SendPingAsync(host, TimeSpan.FromSeconds(2), Array.Empty<byte>(), new PingOptions(64, true), cancellationToken).ConfigureAwait(false); rows.Add(new { host, addresses = addresses.Select(a => a.ToString()).ToArray(), ping = reply.Status.ToString(), roundtripMs = reply.RoundtripTime, local = host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) }); }
            catch (Exception ex) when (ex is PingException or SocketException or OperationCanceledException) { if (ex is OperationCanceledException) throw; rows.Add(new { host, error = ex.Message }); }
        }
        return Success("fleet-inventory", $"Probed {rows.Count} fleet hosts without remote mutation.", rows);
    }

    private CommandHandlerOutcome FleetPolicyDrift(CommandParameters p, CancellationToken cancellationToken)
    {
        JsonElement desired = p.Element("Desired");
        if (desired.ValueKind != JsonValueKind.Object) throw new CommandParameterException("Desired", "Desired must be a JSON object.");
        CommandHandlerOutcome hardening = HardeningAssess(cancellationToken);
        var drift = new List<object>();
        if (desired.TryGetProperty("FirewallEnabled", out JsonElement firewall) && firewall.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            FirewallProfileState[] profiles = ReadFirewallProfileStates();
            bool actual = profiles.All(profile => profile.Enabled);
            if (actual != firewall.GetBoolean()) drift.Add(new { setting = "FirewallEnabled", desired = firewall.GetBoolean(), actual, profiles });
        }
        return Success("fleet-policy-drift", "Local policy drift evaluated against explicit desired values.", new { machine = Environment.MachineName, drift, hardening = hardening.Data });
    }

    private CommandHandlerOutcome ForensicsTimeline(CommandParameters p, CancellationToken cancellationToken)
    {
        int max = p.Int32("MaximumEvents", 500, 1, 5000);
        DateTimeOffset since = p.DateTimeOffset("Since", DateTimeOffset.UtcNow.AddHours(-24));
        var rows = new List<object>();
        foreach (string log in new[] { "System", "Application" })
        {
            string query = $"*[System[TimeCreated[@SystemTime>='{since.UtcDateTime:O}']]]";
            try
            {
                using var reader = new EventLogReader(new EventLogQuery(log, PathType.LogName, query) { ReverseDirection = true });
                while (rows.Count < max && reader.ReadEvent() is EventRecord record)
                {
                    using (record) rows.Add(new { log, record.Id, record.LevelDisplayName, record.ProviderName, record.TimeCreated, message = SafeEventMessage(record) });
                }
            }
            catch (EventLogException ex) { rows.Add(new { log, error = ex.Message }); }
            cancellationToken.ThrowIfCancellationRequested();
        }
        return Success("forensics-timeline", $"Collected {rows.Count} bounded forensic timeline records.", rows);
    }

    private CommandHandlerOutcome MemoryAnomalies(CommandParameters p)
    {
        long threshold = p.Int64("WorkingSetThresholdBytes", 512L * 1024 * 1024, 1, long.MaxValue);
        var rows = Process.GetProcesses().Select(process => { try { return new MemoryProcessRecord(process.Id, process.ProcessName, process.WorkingSet64, process.PrivateMemorySize64, process.Threads.Count); } catch { return null; } }).Where(x => x is not null && x.WorkingSetBytes >= threshold).Cast<MemoryProcessRecord>().OrderByDescending(x => x.WorkingSetBytes).Take(p.Int32("Limit", 100, 1, 1000)).ToArray();
        return Success("memory-anomalies", $"Found {rows.Length} processes above working-set threshold.", new { threshold, processes = rows });
    }

    private async Task<CommandHandlerOutcome> BinaryIntelligenceAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string path = RequireExistingFile(p.RequiredString("Path"), 1024L * 1024 * 1024);
        FileInfo info = new(path); string sha256;
        await using (FileStream stream = File.OpenRead(path)) sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
        double entropy = await CalculateEntropyAsync(path, cancellationToken).ConfigureAwait(false);
        CommandHandlerOutcome pe = PeInspection(new CommandParameters(Data(new { Path = path })));
        return Success("binary-intelligence", "Binary intelligence calculated without executing the file.", new { path, info.Length, sha256, entropy, pe = pe.Data });
    }

    private CommandHandlerOutcome BinaryIntelligenceCapability() => Success("binary-intelligence-capability", "Binary intelligence capability available through managed PE parsing and cryptographic hashing.", new { peHeaders = true, sha256 = true, entropy = true, execution = false });

    private CommandHandlerOutcome ToolCapability(string id, string[] tools, string description)
    {
        var rows = tools.Select(tool => new { tool, path = FindExecutable(tool) }).ToArray();
        return Success(id + "-capability", description, new { available = rows.Any(row => row.path is not null), tools = rows });
    }

    private async Task<CommandHandlerOutcome> EbpfProgramsAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string? tool = FindExecutable("netsh.exe");
        if (tool is null) throw new CommandDependencyException("netsh.exe", "netsh.exe is unavailable.");
        ProcessExecutionResult result = await _process.RunAsync(tool, ["ebpf", "show", "programs"], cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 ? Success("ebpf-programs", "eBPF program inventory queried.", result) : Block("ebpf-programs", "eBPF for Windows is not installed or this netsh context is unavailable.");
    }

    private async Task<CommandHandlerOutcome> EbpfAdmitAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string path = RequireExistingFile(p.RequiredString("Path"), 64 * 1024 * 1024);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".o" or ".sys")) throw new CommandParameterException("Path", "eBPF admission accepts .o or .sys program artifacts only.");
        string? tool = FindExecutable("netsh.exe"); if (tool is null) throw new CommandDependencyException("netsh.exe", "netsh.exe is unavailable.");
        ProcessExecutionResult result = await _process.RunAsync(tool, ["ebpf", "add", "program", path], cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 ? Success("ebpf-admit", "eBPF program admission command completed.", new { path, result.StandardOutput }) : Block("ebpf-admit", $"eBPF admission failed: {FirstLine(result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput)}");
    }

    private async Task<CommandHandlerOutcome> WireGuardTunnelsAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string? wg = FindExecutable("wg.exe");
        if (wg is null) return Block("wireguard-tunnels", "wg.exe is not installed or on PATH.");
        ProcessExecutionResult result = await _process.RunAsync(wg, ["show", "all", "dump"], cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 ? Success("wireguard-tunnels", "WireGuard tunnel state queried.", new { raw = result.StandardOutput }) : Block("wireguard-tunnels", FirstLine(result.StandardError));
    }

    private CommandHandlerOutcome QuicCapability()
    {
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string[] dlls = [Path.Combine(system, "msquic.dll"), Path.Combine(system, "httpapi.dll")];
        return Success("quic-capability", "Windows QUIC runtime capability inspected.", new { msquic = File.Exists(dlls[0]), httpApi = File.Exists(dlls[1]), files = dlls.Where(File.Exists).ToArray() });
    }

    private async Task<CommandHandlerOutcome> GenericInspectionAsync(CommandDefinition definition, CommandParameters p, CancellationToken cancellationToken)
    {
        // Last-resort route is still evidence-based: catalog metadata + durable command-owned state + host capability.
        // It never invents machine state and never services mutating commands.
        JsonElement saved = await _state.ReadArrayAsync(definition.Id, cancellationToken).ConfigureAwait(false);
        return Success(definition.Id, $"Native capability metadata inspected for '{definition.Title}'.", new
        {
            commandId = definition.Id,
            definition.Title,
            definition.Summary,
            definition.Area,
            definition.Section,
            definition.Risk,
            definition.AdministratorAccess,
            definition.Restart,
            machine = Environment.MachineName,
            osBuild = Environment.OSVersion.Version.Build,
            savedState = saved,
        });
    }

    private static string RequireExistingFile(string rawPath) => RequireExistingFile(rawPath, long.MaxValue);
    private static string RequireExistingDirectory(string rawPath)
    {
        string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(rawPath.Trim()));
        if (!Directory.Exists(path)) throw new CommandParameterException("Path", $"Directory '{path}' does not exist.");
        var directory = new DirectoryInfo(path);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Directory '{path}' is a reparse point and is not admitted as an operation root.");
        return path;
    }

    private static int GetJsonInt(JsonElement item, string preferred, string alternate)
    {
        if (item.TryGetProperty(preferred, out JsonElement a) && a.TryGetInt32(out int value)) return value;
        return item.TryGetProperty(alternate, out JsonElement b) && b.TryGetInt32(out value) ? value : 0;
    }

    private static bool IsSafeFilePattern(string pattern) => pattern.Length is > 0 and <= 128 && !pattern.Contains("..", StringComparison.Ordinal) && !Path.IsPathRooted(pattern) && pattern.IndexOfAny(['\\', '/']) < 0;

    private static string[] ReadTailLines(string path, int maximumLines, int maximumBytes)
    {
        FileInfo info = new(path); int bytes = (int)Math.Min(info.Length, maximumBytes);
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Seek(-bytes, SeekOrigin.End); byte[] buffer = new byte[bytes]; stream.ReadExactly(buffer);
        return Encoding.UTF8.GetString(buffer).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(maximumLines).ToArray();
    }

    private static string SafeEventMessage(EventRecord record) { try { string? message = record.FormatDescription(); return message is null ? string.Empty : message.Length <= 4096 ? message : message[..4096] + "…"; } catch (EventLogException) { return string.Empty; } }

    private static async Task<double> CalculateEntropyAsync(string path, CancellationToken cancellationToken)
    {
        long[] counts = new long[256]; long total = 0; byte[] buffer = new byte[128 * 1024];
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, FileOptions.Asynchronous | FileOptions.SequentialScan);
        while (true) { int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); if (read == 0) break; for (int i = 0; i < read; i++) counts[buffer[i]]++; total += read; }
        if (total == 0) return 0; double entropy = 0; foreach (long count in counts) if (count > 0) { double probability = count / (double)total; entropy -= probability * Math.Log2(probability); } return Math.Round(entropy, 4);
    }

    private sealed record MemoryProcessRecord(int ProcessId, string ProcessName, long WorkingSetBytes, long PrivateBytes, int ThreadCount);
}
