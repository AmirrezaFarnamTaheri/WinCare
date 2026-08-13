using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Win32;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;

namespace WinCare.Infrastructure.Commands;

public sealed partial class WindowsCommandExecutor
{
    private async Task<CommandHandlerOutcome> SystemOverviewAsync(CancellationToken cancellationToken)
    {
        if (_nativeCore is not null)
        {
            string json = await _nativeCore.GetSystemInfoJsonAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(json);
            return CommandHandlerOutcome.Succeeded("system.ok", "System information collected.", document.RootElement.Clone());
        }

        WindowsInterop.MemoryStatusEx memory = new() { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WindowsInterop.MemoryStatusEx>() };
        bool memoryOk = WindowsInterop.GlobalMemoryStatusEx(ref memory);
        return Success("system", "System information collected.", new
        {
            machineName = Environment.MachineName,
            osVersion = Environment.OSVersion.VersionString,
            osBuild = Environment.OSVersion.Version.Build,
            architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            processorCount = Environment.ProcessorCount,
            userInteractive = Environment.UserInteractive,
            totalPhysicalMemoryBytes = memoryOk ? memory.TotalPhys : 0UL,
            availablePhysicalMemoryBytes = memoryOk ? memory.AvailPhys : 0UL,
            checkedAt = DateTimeOffset.UtcNow,
        });
    }

    private CommandHandlerOutcome Applications()
    {
        var apps = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach ((RegistryHive hive, RegistryView view) in new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Default),
        })
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (string name in uninstall.GetSubKeyNames().Take(5000))
            {
                using RegistryKey? item = uninstall.OpenSubKey(name);
                string? displayName = item?.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(displayName)) continue;
                string key = displayName.Trim();
                apps.TryAdd(key, new
                {
                    name = key,
                    version = item?.GetValue("DisplayVersion") as string ?? string.Empty,
                    publisher = item?.GetValue("Publisher") as string ?? string.Empty,
                    installDate = item?.GetValue("InstallDate") as string ?? string.Empty,
                    installLocation = item?.GetValue("InstallLocation") as string ?? string.Empty,
                    uninstallString = item?.GetValue("UninstallString") as string ?? string.Empty,
                });
            }
        }
        return Success("applications", $"Enumerated {apps.Count} installed applications.", apps.Values.OrderBy(value => value.ToString()).ToArray());
    }

    private async Task<CommandHandlerOutcome> CleanupTargetsAsync(CancellationToken cancellationToken)
    {
        string[] candidates =
        [
            Path.GetTempPath(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
        ];
        var targets = new List<object>();
        foreach (string raw in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.GetFullPath(raw);
            if (!Directory.Exists(path)) continue;
            ulong bytes = 0;
            try
            {
                bytes = _nativeCore is not null
                    ? await _nativeCore.GetDirectorySizeAsync(path, cancellationToken).ConfigureAwait(false)
                    : SumDirectoryBytes(path, cancellationToken, 200_000);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                targets.Add(new { path, available = false, error = ex.Message });
                continue;
            }
            targets.Add(new { path, available = true, bytes });
        }
        return Success("cleanup-targets", "Cleanup targets measured without changing files.", targets);
    }

    private CommandHandlerOutcome StorageOverview()
    {
        object[] drives = DriveInfo.GetDrives().Select(drive =>
        {
            try
            {
                return (object)new
                {
                    name = drive.Name,
                    type = drive.DriveType.ToString(),
                    format = drive.IsReady ? drive.DriveFormat : string.Empty,
                    label = drive.IsReady ? drive.VolumeLabel : string.Empty,
                    totalBytes = drive.IsReady ? drive.TotalSize : 0L,
                    freeBytes = drive.IsReady ? drive.AvailableFreeSpace : 0L,
                    ready = drive.IsReady,
                };
            }
            catch (IOException ex)
            {
                return (object)new { name = drive.Name, type = drive.DriveType.ToString(), ready = false, error = ex.Message };
            }
        }).ToArray();
        return Success("storage", $"Enumerated {drives.Length} drives.", drives);
    }

    private CommandHandlerOutcome StartupItems()
    {
        var items = new List<object>();
        foreach ((RegistryHive hive, string location) in new[]
        {
            (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        })
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using RegistryKey? run = baseKey.OpenSubKey(location);
            if (run is null) continue;
            foreach (string name in run.GetValueNames())
            {
                items.Add(new { name, command = Convert.ToString(run.GetValue(name), CultureInfo.InvariantCulture) ?? string.Empty, source = hive.ToString() });
            }
        }
        foreach (string folder in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
        })
        {
            if (!Directory.Exists(folder)) continue;
            foreach (string file in Directory.EnumerateFiles(folder).Take(500))
            {
                items.Add(new { name = Path.GetFileNameWithoutExtension(file), command = file, source = folder });
            }
        }
        return Success("startup", $"Enumerated {items.Count} startup entries.", items);
    }

    private CommandHandlerOutcome HealthOverview()
    {
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => new
        {
            d.Name,
            freePercent = d.TotalSize == 0 ? 0 : Math.Round(d.AvailableFreeSpace * 100d / d.TotalSize, 1),
        }).ToArray();
        WindowsInterop.MemoryStatusEx memory = new() { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WindowsInterop.MemoryStatusEx>() };
        WindowsInterop.GlobalMemoryStatusEx(ref memory);
        FirewallProfileState[] firewallProfiles = ReadFirewallProfileStates();
        bool firewall = firewallProfiles.All(profile => profile.Enabled);
        var findings = new List<object>();
        findings.AddRange(drives.Where(d => d.freePercent < 10).Select(d => (object)new { area = "Storage", item = d.Name, severity = "High", message = $"Only {d.freePercent}% free." }));
        if (memory.MemoryLoad >= 90) findings.Add(new { area = "Memory", item = "Physical memory", severity = "Moderate", message = $"Memory load is {memory.MemoryLoad}%." });
        if (!firewall) findings.Add(new { area = "Security", item = "Windows Firewall", severity = "High", message = "One or more firewall profiles appear disabled." });
        int score = Math.Max(0, 100 - findings.Count * 15);
        return Success("health", "Health assessment completed from current machine state.", new { score, findings, checkedAt = DateTimeOffset.UtcNow });
    }

    private async Task<CommandHandlerOutcome> SecurityOverviewAsync(CancellationToken cancellationToken)
    {
        ProcessExecutionResult defender = await _process.RunAsync("sc.exe", ["query", "WinDefend"], cancellationToken).ConfigureAwait(false);
        FirewallProfileState[] firewallProfiles = ReadFirewallProfileStates();
        using RegistryKey? policies = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
        int enableLua = Convert.ToInt32(policies?.GetValue("EnableLUA", 1), CultureInfo.InvariantCulture);
        return Success("security", "Security posture collected from Windows services, firewall and policy state.", new
        {
            defenderServiceRunning = defender.StandardOutput.Contains("RUNNING", StringComparison.OrdinalIgnoreCase),
            firewallEnabled = firewallProfiles.All(profile => profile.Enabled),
            firewallProfiles,
            uacEnabled = enableLua != 0,
            checkedAt = DateTimeOffset.UtcNow,
        });
    }

    private sealed record FirewallProfileState(string Profile, bool Enabled, bool ExplicitValue);

    private static FirewallProfileState[] ReadFirewallProfileStates()
    {
        (string Name, string Key)[] profiles =
        [
            ("Domain", @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\DomainProfile"),
            ("Private", @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile"),
            ("Public", @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\PublicProfile"),
        ];

        return profiles.Select(profile =>
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(profile.Key);
            object? raw = key?.GetValue("EnableFirewall", null);
            bool explicitValue = raw is not null;
            bool enabled = raw is null || Convert.ToInt32(raw, CultureInfo.InvariantCulture) != 0;
            return new FirewallProfileState(profile.Name, enabled, explicitValue);
        }).ToArray();
    }

    private CommandHandlerOutcome NetworkOverview()
    {
        object[] adapters = NetworkInterface.GetAllNetworkInterfaces().Select(adapter => new
        {
            id = adapter.Id,
            name = adapter.Name,
            description = adapter.Description,
            type = adapter.NetworkInterfaceType.ToString(),
            status = adapter.OperationalStatus.ToString(),
            speedBitsPerSecond = adapter.Speed,
            addresses = adapter.GetIPProperties().UnicastAddresses.Select(x => x.Address.ToString()).ToArray(),
            gateways = adapter.GetIPProperties().GatewayAddresses.Select(x => x.Address.ToString()).ToArray(),
            dns = adapter.GetIPProperties().DnsAddresses.Select(x => x.ToString()).ToArray(),
        }).ToArray();
        return Success("network", $"Enumerated {adapters.Length} network interfaces.", adapters);
    }

    private CommandHandlerOutcome PagefileState()
    {
        using RegistryKey? memory = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
        string[] pagingFiles = memory?.GetValue("PagingFiles") as string[] ?? Array.Empty<string>();
        int tempPageFile = Convert.ToInt32(memory?.GetValue("TempPageFile", 0), CultureInfo.InvariantCulture);
        return Success("pagefile", "Virtual memory configuration read from Windows registry.", new { pagingFiles, temporaryPageFile = tempPageFile != 0 });
    }

    private CommandHandlerOutcome PagefileRecommendation()
    {
        WindowsInterop.MemoryStatusEx memory = new() { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WindowsInterop.MemoryStatusEx>() };
        if (!WindowsInterop.GlobalMemoryStatusEx(ref memory))
        {
            return CommandHandlerOutcome.Failed("pagefile-recommendation.memory_query_failed", "Windows could not report physical memory.");
        }
        long ramMb = checked((long)(memory.TotalPhys / 1024 / 1024));
        long min = Math.Clamp(ramMb / 4, 1024, 16_384);
        long max = Math.Clamp(ramMb, min, 32_768);
        return Success("pagefile-recommendation", "Pagefile recommendation calculated from installed physical memory.", new { physicalMemoryMB = ramMb, recommendedInitialMB = min, recommendedMaximumMB = max });
    }

    private async Task<CommandHandlerOutcome> SecurityControlsAsync(CancellationToken cancellationToken)
    {
        ProcessExecutionResult defender = await _process.RunAsync("sc.exe", ["query", "WinDefend"], cancellationToken).ConfigureAwait(false);
        FirewallProfileState[] firewallProfiles = ReadFirewallProfileStates();
        using RegistryKey? policies = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
        var controls = new[]
        {
            new { id = "defender-service", state = defender.StandardOutput.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) ? "Enabled" : "Disabled" },
            new { id = "firewall", state = firewallProfiles.All(profile => profile.Enabled) ? "Enabled" : "Review" },
            new { id = "uac", state = Convert.ToInt32(policies?.GetValue("EnableLUA", 1), CultureInfo.InvariantCulture) != 0 ? "Enabled" : "Disabled" },
        };
        return Success("security-controls", "Security control state inspected.", controls);
    }

    private async Task<CommandHandlerOutcome> NetworkMeasureAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> hosts = p.StringArray("TargetHosts");
        if (hosts.Count == 0) hosts = ["1.1.1.1", "8.8.8.8"];
        int samples = p.Int32("SampleCount", 3, 1, 20);
        int timeout = p.Int32("TimeoutMilliseconds", 1500, 100, 10_000);
        int port = p.Int32("Port", 443, 1, 65535);
        var results = new List<object>();
        using var ping = new Ping();
        foreach (string host in hosts.Take(32))
        {
            var latency = new List<long>();
            for (int i = 0; i < samples; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    PingReply reply = await ping.SendPingAsync(host, TimeSpan.FromMilliseconds(timeout)).WaitAsync(cancellationToken).ConfigureAwait(false);
                    if (reply.Status == IPStatus.Success) latency.Add(reply.RoundtripTime);
                }
                catch (PingException) { }
            }
            bool tcp = false;
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(host, port, cancellationToken).AsTask().WaitAsync(TimeSpan.FromMilliseconds(timeout), cancellationToken).ConfigureAwait(false);
                tcp = client.Connected;
            }
            catch (Exception ex) when (ex is SocketException or TimeoutException) { }
            results.Add(new
            {
                host,
                port,
                successfulPings = latency.Count,
                averageLatencyMs = latency.Count == 0 ? (double?)null : Math.Round(latency.Average(), 1),
                tcpReachable = tcp,
            });
        }
        return Success("network-measure", "Network endpoints measured.", results);
    }

    private CommandHandlerOutcome ProcessInjectionSurface(CommandParameters p)
    {
        int limit = p.Int32("Limit", 100, 1, 1000);
        var rows = new List<object>();
        foreach (Process process in Process.GetProcesses().OrderByDescending(x => SafeWorkingSet(x)).Take(limit))
        {
            using (process)
            {
                rows.Add(new
                {
                    process.Id,
                    name = SafeProcessName(process),
                    workingSetBytes = SafeWorkingSet(process),
                    path = SafeProcessPath(process),
                    moduleCount = SafeModuleCount(process),
                });
            }
        }
        return Success("injection-surfaces", "Process surfaces enumerated from live processes.", rows);
    }

    private CommandHandlerOutcome ProcessModules(CommandParameters p)
    {
        int pid = p.Int32("ProcessId", 0, 1, int.MaxValue);
        if (pid <= 0) throw new CommandParameterException("ProcessId", "Parameter 'ProcessId' is required and must be positive.");
        int limit = p.Int32("Limit", 1000, 1, 5000);
        using Process process = Process.GetProcessById(pid);
        var modules = new List<object>();
        try
        {
            foreach (ProcessModule module in process.Modules.Cast<ProcessModule>().Take(limit))
            {
                modules.Add(new { module.ModuleName, module.FileName, baseAddress = module.BaseAddress.ToInt64(), module.ModuleMemorySize });
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Block("process-modules", $"Could not enumerate modules for PID {pid}: {ex.Message}");
        }
        return Success("process-modules", $"Enumerated {modules.Count} modules for PID {pid}.", modules);
    }

    private async Task<CommandHandlerOutcome> NativeToolQueryAsync(
        string id,
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool administratorOptional = false)
    {
        ProcessExecutionResult result = await _process.RunAsync(fileName, arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 && !administratorOptional)
        {
            return Block(id, $"{fileName} exited with code {result.ExitCode}: {FirstLine(result.StandardError)}");
        }
        return Success(id, $"{id} queried through {fileName}.", new
        {
            exitCode = result.ExitCode,
            stdout = result.StandardOutput,
            stderr = result.StandardError,
            outputTruncated = result.OutputTruncated,
            durationMs = result.Duration.TotalMilliseconds,
        });
    }

    private async Task<CommandHandlerOutcome> EventLogQueryAsync(
        string id,
        string logName,
        int maxEvents,
        CancellationToken cancellationToken,
        string? providerContains = null)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = new List<object>();
            var query = new EventLogQuery(logName, PathType.LogName) { ReverseDirection = true, TolerateQueryErrors = true };
            using var reader = new EventLogReader(query);
            for (int i = 0; i < maxEvents; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using EventRecord? record = reader.ReadEvent();
                if (record is null) break;
                if (!string.IsNullOrEmpty(providerContains) && !(record.ProviderName?.Contains(providerContains, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    continue;
                }
                string message;
                try { message = record.FormatDescription() ?? string.Empty; }
                catch (EventLogException) { message = string.Empty; }
                rows.Add(new
                {
                    record.Id,
                    record.ProviderName,
                    record.LevelDisplayName,
                    record.TimeCreated,
                    message = message.Length <= 2048 ? message : message[..2048],
                });
            }
            return Success(id, $"Read {rows.Count} events from {logName}.", rows);
        }, cancellationToken).ConfigureAwait(false);
    }

    private CommandHandlerOutcome InternalsProcesses(CommandParameters p)
    {
        int top = p.Int32("Top", 50, 1, 500);
        object[] rows = Process.GetProcesses().Select(process =>
        {
            using (process)
            {
                return new
                {
                    process.Id,
                    name = SafeProcessName(process),
                    workingSetBytes = SafeWorkingSet(process),
                    privateMemoryBytes = SafePrivateMemory(process),
                    threadCount = SafeThreadCount(process),
                    path = SafeProcessPath(process),
                };
            }
        }).OrderByDescending(row => row.workingSetBytes).Take(top).Cast<object>().ToArray();
        return Success("internals-processes", $"Collected process internals for {rows.Length} processes.", rows);
    }

    private CommandHandlerOutcome InternalsMemory()
    {
        WindowsInterop.MemoryStatusEx memory = new() { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WindowsInterop.MemoryStatusEx>() };
        if (!WindowsInterop.GlobalMemoryStatusEx(ref memory)) return CommandHandlerOutcome.Failed("internals-memory.query_failed", "GlobalMemoryStatusEx failed.");
        return Success("internals-memory", "Memory status collected.", new
        {
            loadPercent = memory.MemoryLoad,
            totalPhysicalBytes = memory.TotalPhys,
            availablePhysicalBytes = memory.AvailPhys,
            totalPageFileBytes = memory.TotalPageFile,
            availablePageFileBytes = memory.AvailPageFile,
            totalVirtualBytes = memory.TotalVirtual,
            availableVirtualBytes = memory.AvailVirtual,
        });
    }

    private CommandHandlerOutcome InternalsCpu()
    {
        return Success("internals-cpu", "Processor topology collected from runtime and registry.", new
        {
            logicalProcessors = Environment.ProcessorCount,
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            processorIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? string.Empty,
            processorLevel = Environment.GetEnvironmentVariable("PROCESSOR_LEVEL") ?? string.Empty,
            processorRevision = Environment.GetEnvironmentVariable("PROCESSOR_REVISION") ?? string.Empty,
        });
    }

    private CommandHandlerOutcome CredentialProviders()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers");
        var rows = new List<object>();
        if (key is not null)
        {
            foreach (string id in key.GetSubKeyNames())
            {
                using RegistryKey? provider = key.OpenSubKey(id);
                rows.Add(new { id, name = Convert.ToString(provider?.GetValue(null), CultureInfo.InvariantCulture) ?? string.Empty });
            }
        }
        return Success("credential-providers", $"Enumerated {rows.Count} credential providers.", rows);
    }

    private CommandHandlerOutcome AppContainerState(CommandParameters p)
    {
        string packageName = p.String("PackageName");
        using RegistryKey? root = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings");
        var rows = new List<object>();
        if (root is not null)
        {
            foreach (string sid in root.GetSubKeyNames().Take(5000))
            {
                using RegistryKey? item = root.OpenSubKey(sid);
                string name = Convert.ToString(item?.GetValue("Moniker"), CultureInfo.InvariantCulture) ?? string.Empty;
                if (packageName.Length == 0 || name.Contains(packageName, StringComparison.OrdinalIgnoreCase)) rows.Add(new { sid, name });
            }
        }
        return Success("appcontainer", $"Enumerated {rows.Count} AppContainer mappings.", rows);
    }

    private CommandHandlerOutcome DesktopControls()
    {
        using RegistryKey? desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        return Success("desktop-controls", "Desktop settings read from current-user policy.", new
        {
            wallpaper = Convert.ToString(desktop?.GetValue("WallPaper"), CultureInfo.InvariantCulture) ?? string.Empty,
            screenSaveActive = Convert.ToString(desktop?.GetValue("ScreenSaveActive"), CultureInfo.InvariantCulture) ?? string.Empty,
            screenSaveTimeOut = Convert.ToString(desktop?.GetValue("ScreenSaveTimeOut"), CultureInfo.InvariantCulture) ?? string.Empty,
        });
    }

    private CommandHandlerOutcome ShellExtensions()
    {
        var rows = new List<object>();
        foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using RegistryKey? approved = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved");
            if (approved is null) continue;
            foreach (string name in approved.GetValueNames().Take(5000)) rows.Add(new { clsid = name, description = Convert.ToString(approved.GetValue(name), CultureInfo.InvariantCulture) ?? string.Empty, hive = hive.ToString() });
        }
        return Success("shell-extensions", $"Enumerated {rows.Count} approved shell extensions.", rows);
    }

    private CommandHandlerOutcome DesktopShortcuts()
    {
        var files = new List<object>();
        foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) })
        {
            if (!Directory.Exists(folder)) continue;
            files.AddRange(Directory.EnumerateFiles(folder, "*.lnk", SearchOption.TopDirectoryOnly).Take(1000).Select(path => (object)new { name = Path.GetFileNameWithoutExtension(path), path, scope = folder }));
        }
        return Success("desktop-shortcuts", $"Enumerated {files.Count} desktop shortcuts.", files);
    }

    private CommandHandlerOutcome UnattendAnalyze(CommandParameters p)
    {
        string path = RequireExistingFile(p.RequiredString("Path"), 8 * 1024 * 1024);
        XDocument document = XDocument.Load(path, LoadOptions.None);
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
        var passes = document.Descendants(ns + "settings").Select(x => (string?)x.Attribute("pass") ?? string.Empty).Where(x => x.Length > 0).Distinct().ToArray();
        int componentCount = document.Descendants(ns + "component").Count();
        var sensitive = document.Descendants().Where(x => x.Name.LocalName.Contains("Password", StringComparison.OrdinalIgnoreCase) || x.Name.LocalName.Contains("ProductKey", StringComparison.OrdinalIgnoreCase)).Select(x => x.Name.LocalName).Distinct().ToArray();
        return Success("unattend-analyze", "Unattended setup XML inspected without executing it.", new { path, passes, componentCount, sensitiveElements = sensitive });
    }

    private CommandHandlerOutcome Knowledge(CommandParameters p)
    {
        string topic = p.String("Topic").Trim();
        string[] roots = [AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "docs")];
        var hits = new List<object>();
        if (topic.Length > 0)
        {
            foreach (string root in roots.Where(Directory.Exists))
            {
                foreach (string file in Directory.EnumerateFiles(root, "*.md", SafeRecursiveEnumeration).Take(500))
                {
                    string text;
                    try { text = File.ReadAllText(file); } catch (IOException) { continue; }
                    int index = text.IndexOf(topic, StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                    {
                        int start = Math.Max(0, index - 120); int len = Math.Min(480, text.Length - start);
                        hits.Add(new { path = file, excerpt = text.Substring(start, len) });
                    }
                }
            }
        }
        return Success("knowledge", topic.Length == 0 ? "Knowledge search is ready; provide Topic to search installed documentation." : $"Found {hits.Count} documentation matches.", new { topic, hits });
    }

    private async Task<CommandHandlerOutcome> SystemReportAsync(CancellationToken cancellationToken)
    {
        var system = await SystemOverviewAsync(cancellationToken).ConfigureAwait(false);
        var network = NetworkOverview();
        var storage = StorageOverview();
        return Success("reports", "System report assembled from live native collectors.", new
        {
            generatedAt = DateTimeOffset.UtcNow,
            system = system.Data,
            network = network.Data,
            storage = storage.Data,
        });
    }

    private async Task<CommandHandlerOutcome> ReadStateAsync(string key, CancellationToken cancellationToken)
    {
        JsonElement data = await _state.ReadArrayAsync(key, cancellationToken).ConfigureAwait(false);
        return CommandHandlerOutcome.Succeeded(key + ".ok", $"Loaded WinCare state '{key}'.", data);
    }

    private static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "no diagnostic text";
        using var reader = new StringReader(text.Trim());
        return reader.ReadLine() ?? text.Trim();
    }

    private static ulong SumDirectoryBytes(string root, CancellationToken cancellationToken, int maxFiles)
    {
        ulong total = 0;
        int count = 0;
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0 && count < maxFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string current = stack.Pop();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(current); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (string file in files)
            {
                if (count++ >= maxFiles) break;
                try { total = checked(total + (ulong)new FileInfo(file).Length); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            try
            {
                foreach (string directory in Directory.EnumerateDirectories(current))
                {
                    try
                    {
                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0) stack.Push(directory);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return total;
    }

    private static long SafeWorkingSet(Process process) { try { return process.WorkingSet64; } catch { return 0; } }
    private static long SafePrivateMemory(Process process) { try { return process.PrivateMemorySize64; } catch { return 0; } }
    private static int SafeThreadCount(Process process) { try { return process.Threads.Count; } catch { return 0; } }
    private static int SafeModuleCount(Process process) { try { return process.Modules.Count; } catch { return 0; } }
    private static string SafeProcessName(Process process) { try { return process.ProcessName; } catch { return string.Empty; } }
    private static string SafeProcessPath(Process process) { try { return process.MainModule?.FileName ?? string.Empty; } catch { return string.Empty; } }

    private static string RequireExistingFile(string rawPath, long maxBytes)
    {
        string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(rawPath));
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("File was not found.", path);
        if ((file.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse-point input files are not admitted.");
        if (file.Length > maxBytes) throw new InvalidDataException($"File exceeds the {maxBytes} byte limit.");
        return path;
    }
}
