using System.Text.RegularExpressions;
using System.Net;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using WinCare.Domain.Commands;
using WinCare.Application.Commands;

using System.Runtime.InteropServices;
namespace WinCare.Infrastructure.Commands;

public sealed partial class WindowsCommandExecutor
{
    private static readonly EnumerationOptions SafeRecursiveEnumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    private async Task<CommandHandlerOutcome> WidgetSnapshotAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string[] names = ["Widgets", "WidgetService", "msedgewebview2"];
        var processes = Process.GetProcesses()
            .Where(process => names.Any(name => process.ProcessName.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .Take(p.Int32("Limit", 100, 1, 500))
            .Select(process => new { process.Id, process.ProcessName, workingSetBytes = SafeWorkingSet(process) })
            .ToArray();
        JsonElement saved = await _state.ReadArrayAsync("widgets", cancellationToken).ConfigureAwait(false);
        return Success("widgets", "Windows widget runtime and saved WinCare widget state inspected.", new { processes, saved });
    }

    private CommandHandlerOutcome WidgetCatalog(CommandParameters p)
    {
        string packagesRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        string[] packages = Directory.Exists(packagesRoot)
            ? Directory.EnumerateDirectories(packagesRoot).Select(Path.GetFileName).Where(name => name is not null && name.Contains("Widget", StringComparison.OrdinalIgnoreCase)).Cast<string>().Take(p.Int32("Limit", 200, 1, 1000)).ToArray()
            : [];
        return Success("widget-catalog", $"Found {packages.Length} widget-related application packages.", packages);
    }

    private CommandHandlerOutcome BluetoothDevices(CommandParameters p, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var devices = new List<object>();
        using RegistryKey? root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices");
        if (root is not null)
        {
            foreach (string id in root.GetSubKeyNames().Take(p.Int32("Limit", 500, 1, 5000)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using RegistryKey? device = root.OpenSubKey(id);
                byte[]? nameBytes = device?.GetValue("Name") as byte[];
                string name = nameBytes is null ? string.Empty : Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');
                devices.Add(new { id, name, lastConnected = device?.GetValue("LastConnected")?.ToString() });
            }
        }
        return Success("bluetooth", $"Enumerated {devices.Count} Bluetooth device records from Windows.", devices);
    }

    private CommandHandlerOutcome ContextMenuEntries()
    {
        var rows = new List<object>();
        foreach ((RegistryHive hive, string hiveName) in new[] { (RegistryHive.CurrentUser, "HKCU"), (RegistryHive.LocalMachine, "HKLM") })
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            foreach (string rootPath in new[] { @"Software\Classes\*\shell", @"Software\Classes\Directory\shell", @"Software\Classes\Directory\Background\shell" })
            {
                using RegistryKey? root = baseKey.OpenSubKey(rootPath);
                if (root is null) continue;
                foreach (string name in root.GetSubKeyNames())
                {
                    using RegistryKey? item = root.OpenSubKey(name);
                    rows.Add(new { hive = hiveName, root = rootPath, id = name, label = Convert.ToString(item?.GetValue(null)), command = Convert.ToString(item?.OpenSubKey("command")?.GetValue(null)) });
                }
            }
        }
        return Success("context-menu", $"Enumerated {rows.Count} shell context-menu entries.", rows);
    }

    private CommandHandlerOutcome ContextMenuSet(CommandParameters p)
    {
        string scope = p.String("Scope", "File").Trim();
        string id = SanitizeRegistryLeaf(p.RequiredString("Id"));
        bool enabled = p.Boolean("Enabled", true);
        string rootPath = scope.ToLowerInvariant() switch
        {
            "file" => @"Software\Classes\*\shell",
            "directory" => @"Software\Classes\Directory\shell",
            "background" => @"Software\Classes\Directory\Background\shell",
            _ => throw new CommandParameterException("Scope", "Scope must be File, Directory, or Background."),
        };
        using RegistryKey root = Registry.CurrentUser.CreateSubKey(rootPath, writable: true);
        if (!enabled)
        {
            root.DeleteSubKeyTree(id, throwOnMissingSubKey: false);
            return Success("context-menu-set", "Per-user context-menu entry removed.", new { scope, id, enabled }, undo: false);
        }
        string label = p.String("Label", id).Trim();
        string command = p.RequiredString("Command").Trim();
        using RegistryKey item = root.CreateSubKey(id, writable: true);
        item.SetValue(null, label, RegistryValueKind.String);
        using RegistryKey commandKey = item.CreateSubKey("command", writable: true);
        commandKey.SetValue(null, command, RegistryValueKind.String);
        return Success("context-menu-set", "Per-user context-menu entry saved.", new { scope, id, label, command, enabled }, undo: false);
    }

    private CommandHandlerOutcome CustomizationHosts()
    {
        string[] processHints = ["Rainmeter", "windhawk", "ep_setup", "ExplorerPatcher", "StartAllBack", "TranslucentTB"];
        var processes = Process.GetProcesses().Where(process => processHints.Any(h => process.ProcessName.Contains(h, StringComparison.OrdinalIgnoreCase))).Select(process => new { process.Id, process.ProcessName }).ToArray();
        return Success("customization-hosts", $"Found {processes.Length} active customization host processes.", processes);
    }

    private CommandHandlerOutcome RainmeterSkins()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Rainmeter", "Skins");
        string[] skins = Directory.Exists(root) ? Directory.EnumerateDirectories(root).Select(Path.GetFileName).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray() : [];
        return Success("rainmeter-skins", $"Found {skins.Length} Rainmeter skin folders.", new { root, skins });
    }

    private CommandHandlerOutcome WindhawkMods()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windhawk", "Engine", "Mods");
        string[] mods = Directory.Exists(root) ? Directory.EnumerateFiles(root, "*.wh.cpp", SearchOption.TopDirectoryOnly).Select(Path.GetFileNameWithoutExtension).Where(x => x is not null).Cast<string>().ToArray() : [];
        return Success("windhawk-mods", $"Found {mods.Length} Windhawk mod sources.", new { root, mods, installed = Directory.Exists(Path.GetDirectoryName(root)) });
    }

    private CommandHandlerOutcome ExplorerPatcherState()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\ExplorerPatcher");
        string install = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ExplorerPatcher", "ep_setup.exe");
        return Success("explorerpatcher", "ExplorerPatcher presence and configuration inspected.", new { installed = File.Exists(install) || key is not null, executable = File.Exists(install) ? install : null, values = RegistryValues(key) });
    }

    private CommandHandlerOutcome ModernContextValidation()
    {
        using RegistryKey? currentVersion = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        int build = int.TryParse(Convert.ToString(currentVersion?.GetValue("CurrentBuildNumber")), out int parsed) ? parsed : Environment.OSVersion.Version.Build;
        using RegistryKey? classic = Registry.CurrentUser.OpenSubKey(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32");
        return Success("modern-context-validate", "Windows context-menu compatibility inspected.", new { build, supportsModernContextMenu = build >= 22000, classicOverridePresent = classic is not null });
    }

    private async Task<CommandHandlerOutcome> PowerSessionStartAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string id = Guid.NewGuid().ToString("N");
        string name = p.String("Name", "Power session");
        using Process process = Process.GetCurrentProcess();
        JsonElement record = Data(new { id, name, startedAt = DateTimeOffset.UtcNow, processId = process.Id, initialProcessorTimeMs = process.TotalProcessorTime.TotalMilliseconds, state = "Running" });
        await AppendStateItemAsync("power-sessions", record, cancellationToken).ConfigureAwait(false);
        return Success("power-start", "Local power/performance observation session started.", record, undo: false);
    }

    private async Task<CommandHandlerOutcome> PowerSessionStopAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string id = p.RequiredString("Id");
        JsonElement sessions = await _state.ReadArrayAsync("power-sessions", cancellationToken).ConfigureAwait(false);
        JsonElement item = sessions.ValueKind == JsonValueKind.Array
            ? sessions.EnumerateArray().FirstOrDefault(x => x.TryGetProperty("id", out JsonElement idEl) && string.Equals(idEl.GetString(), id, StringComparison.Ordinal)).Clone()
            : default;
        if (item.ValueKind == JsonValueKind.Undefined) return Block("power-stop", $"Power session '{id}' was not found.");
        DateTimeOffset startedAt = item.TryGetProperty("startedAt", out JsonElement start) && start.TryGetDateTimeOffset(out DateTimeOffset parsed) ? parsed : DateTimeOffset.UtcNow;
        await PatchStateItemAsync("power-sessions", id, node => { node["state"] = "Completed"; node["stoppedAt"] = DateTimeOffset.UtcNow; }, cancellationToken).ConfigureAwait(false);
        return Success("power-stop", "Power/performance observation session stopped.", new { id, durationSeconds = (DateTimeOffset.UtcNow - startedAt).TotalSeconds });
    }

    private async Task<CommandHandlerOutcome> DownloadsDueAsync(CancellationToken cancellationToken)
    {
        JsonElement downloads = await _state.ReadArrayAsync("downloads", cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        JsonElement[] due = downloads.ValueKind == JsonValueKind.Array
            ? downloads.EnumerateArray().Where(item => IsDownloadDue(item, now)).Select(item => item.Clone()).ToArray()
            : [];
        return Success("downloads-due", $"Found {due.Length} due download jobs.", due);
    }

    private async Task<CommandHandlerOutcome> DownloadCreateAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        Uri uri = RequiredHttpUri(p, "Url");
        string id = p.String("Id").Trim(); if (id.Length == 0) id = Guid.NewGuid().ToString("N");
        string fileName = p.String("FileName").Trim(); if (fileName.Length == 0) fileName = SafeFileNameFromUri(uri);
        string destination = p.String("Destination").Trim();
        if (destination.Length == 0) destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", fileName);
        destination = Path.GetFullPath(Environment.ExpandEnvironmentVariables(destination));
        DateTimeOffset? dueAt = p.String("DueAt").Length == 0 ? null : p.DateTimeOffset("DueAt", DateTimeOffset.UtcNow);
        JsonElement record = Data(new { id, url = uri.AbsoluteUri, destination, state = "Pending", createdAt = DateTimeOffset.UtcNow, dueAt, sha256 = p.String("Sha256").Trim().ToLowerInvariant() });
        await AppendStateItemAsync("downloads", record, cancellationToken).ConfigureAwait(false);
        return Success("download-create", "Download job created.", record, undo: false);
    }

    private async Task<CommandHandlerOutcome> DownloadStartAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        const long maximumBytes = 2L * 1024 * 1024 * 1024;
        string id = p.RequiredString("Id");
        JsonElement job = await FindStateItemAsync("downloads", id, cancellationToken).ConfigureAwait(false);
        Uri uri = new(job.GetProperty("url").GetString()!, UriKind.Absolute);
        string destination = job.GetProperty("destination").GetString()!;
        string expectedHash = job.TryGetProperty("sha256", out JsonElement hash) ? hash.GetString() ?? string.Empty : string.Empty;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string partial = destination + ".wincare.download";

        using CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        string operationKey = "download:" + id;
        if (!_operations.TryAdd(operationKey, operationCancellation)) return Block("download-start", $"Download '{id}' is already active.");

        try
        {
            long existingBytes = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            if (existingBytes > maximumBytes)
            {
                throw new InvalidDataException("Existing partial download exceeds the 2 GiB WinCare safety limit.");
            }

            await PatchStateItemAsync("downloads", id, node =>
            {
                node["state"] = "Running";
                node["startedAt"] ??= DateTimeOffset.UtcNow;
                if (existingBytes > 0) node["resumedAt"] = DateTimeOffset.UtcNow;
                else node.Remove("resumedAt");
                node["partialBytes"] = existingBytes;
                node["updatedAt"] = DateTimeOffset.UtcNow;
            }, cancellationToken).ConfigureAwait(false);

            using HttpRequestMessage request = new(HttpMethod.Get, uri);
            if (existingBytes > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);
            }

            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                operationCancellation.Token).ConfigureAwait(false);

            bool serverResumed = existingBytes > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
            if (existingBytes > 0 && !serverResumed)
            {
                existingBytes = 0;
            }
            response.EnsureSuccessStatusCode();

            long? responseBytes = response.Content.Headers.ContentLength;
            if (responseBytes.HasValue && existingBytes + responseBytes.Value > maximumBytes)
            {
                throw new InvalidDataException("Download exceeds the 2 GiB WinCare safety limit.");
            }

            await using Stream source = await response.Content.ReadAsStreamAsync(operationCancellation.Token).ConfigureAwait(false);
            FileMode mode = serverResumed ? FileMode.Append : FileMode.Create;
            await using FileStream target = new(
                partial,
                mode,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            byte[] buffer = new byte[128 * 1024];
            long total = existingBytes;
            long nextProgressCheckpoint = total + 4L * 1024 * 1024;
            while (true)
            {
                int read = await source.ReadAsync(buffer, operationCancellation.Token).ConfigureAwait(false);
                if (read == 0) break;
                total += read;
                if (total > maximumBytes) throw new InvalidDataException("Download exceeded the 2 GiB WinCare safety limit.");
                await target.WriteAsync(buffer.AsMemory(0, read), operationCancellation.Token).ConfigureAwait(false);
                if (total >= nextProgressCheckpoint)
                {
                    long progress = total;
                    await PatchStateItemAsync("downloads", id, node =>
                    {
                        node["partialBytes"] = progress;
                        node["updatedAt"] = DateTimeOffset.UtcNow;
                    }, operationCancellation.Token).ConfigureAwait(false);
                    nextProgressCheckpoint = total + 4L * 1024 * 1024;
                }
            }
            await target.FlushAsync(operationCancellation.Token).ConfigureAwait(false);

            await using FileStream hashStream = File.OpenRead(partial);
            string actualHash = Convert.ToHexString(
                await SHA256.HashDataAsync(hashStream, operationCancellation.Token).ConfigureAwait(false)).ToLowerInvariant();
            if (expectedHash.Length > 0 && !CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(actualHash), Encoding.ASCII.GetBytes(expectedHash)))
            {
                throw new InvalidDataException("Downloaded file SHA-256 does not match the requested digest.");
            }

            File.Move(partial, destination, overwrite: true);
            await PatchStateItemAsync("downloads", id, node =>
            {
                node["state"] = "Completed";
                node["completedAt"] = DateTimeOffset.UtcNow;
                node["updatedAt"] = DateTimeOffset.UtcNow;
                node["bytes"] = total;
                node["partialBytes"] = 0;
                node["sha256Actual"] = actualHash;
            }, cancellationToken).ConfigureAwait(false);
            return Success("download-start", "Download completed and verified.", new { id, destination, bytes = total, sha256 = actualHash, resumed = serverResumed });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            string requestedState = await GetDownloadStateAsync(id, CancellationToken.None).ConfigureAwait(false);
            bool suspended = requestedState.Equals("SuspendRequested", StringComparison.OrdinalIgnoreCase);
            bool cancelled = requestedState.Equals("CancelRequested", StringComparison.OrdinalIgnoreCase);
            long partialBytes = File.Exists(partial) ? new FileInfo(partial).Length : 0;

            if (cancelled)
            {
                TryDeleteFile(partial);
                partialBytes = 0;
            }

            string finalState = suspended ? "Suspended" : "Cancelled";
            await PatchStateItemAsync("downloads", id, node =>
            {
                node["state"] = finalState;
                node["partialBytes"] = partialBytes;
                node["updatedAt"] = DateTimeOffset.UtcNow;
            }, CancellationToken.None).ConfigureAwait(false);
            return Block("download-start", suspended ? $"Download '{id}' was suspended with {partialBytes} partial byte(s) retained." : $"Download '{id}' was cancelled.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            long partialBytes = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            await PatchStateItemAsync("downloads", id, node =>
            {
                node["state"] = partialBytes > 0 ? "Suspended" : "Pending";
                node["partialBytes"] = partialBytes;
                node["updatedAt"] = DateTimeOffset.UtcNow;
            }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch
        {
            long partialBytes = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            await PatchStateItemAsync("downloads", id, node =>
            {
                node["state"] = "Failed";
                node["partialBytes"] = partialBytes;
                node["updatedAt"] = DateTimeOffset.UtcNow;
            }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _operations.TryRemove(operationKey, out _);
        }
    }

    private async Task<CommandHandlerOutcome> DownloadStartDueAsync(CancellationToken cancellationToken)
    {
        JsonElement downloads = await _state.ReadArrayAsync("downloads", cancellationToken).ConfigureAwait(false);
        string[] ids = downloads.ValueKind == JsonValueKind.Array
            ? downloads.EnumerateArray()
                .Where(item => IsDownloadDue(item, DateTimeOffset.UtcNow))
                .Select(item => item.GetProperty("id").GetString())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToArray()
            : [];
        var completed = new List<string>();
        var failed = new List<object>();
        foreach (string id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommandHandlerOutcome result = await DownloadStartAsync(new CommandParameters(Data(new { Id = id })), cancellationToken).ConfigureAwait(false);
            if (result.Status == CommandResultStatus.Succeeded) completed.Add(id);
            else failed.Add(new { id, result.Code, result.Message });
        }
        JsonElement data = Data(new { attempted = ids.Length, completed, failed });
        return failed.Count == 0
            ? CommandHandlerOutcome.Succeeded("download-start-due.ok", "Due download jobs processed successfully.", data)
            : new CommandHandlerOutcome(CommandResultStatus.Failed, "download-start-due.partial_failure", $"{failed.Count} due download job(s) failed or were blocked.", data, UndoAvailable: false);
    }

    private async Task<CommandHandlerOutcome> DownloadSuspendAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string id = p.RequiredString("Id");
        _ = await FindStateItemAsync("downloads", id, cancellationToken).ConfigureAwait(false);
        if (!_operations.TryGetValue("download:" + id, out CancellationTokenSource? source))
        {
            string state = await GetDownloadStateAsync(id, cancellationToken).ConfigureAwait(false);
            return state.Equals("Suspended", StringComparison.OrdinalIgnoreCase)
                ? Success("download-suspend", $"Download '{id}' is already suspended.", new { id, state })
                : Block("download-suspend", $"Download '{id}' is not currently active and cannot be suspended.");
        }
        await PatchStateItemAsync("downloads", id, node =>
        {
            node["state"] = "SuspendRequested";
            node["updatedAt"] = DateTimeOffset.UtcNow;
        }, cancellationToken).ConfigureAwait(false);
        source.Cancel();
        return Success("download-suspend", $"Suspend requested for download '{id}'. Partial data will be retained for resume.", new { id, state = "SuspendRequested" });
    }

    private async Task<CommandHandlerOutcome> DownloadReconcileAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string id = p.RequiredString("Id");
        JsonElement job = await FindStateItemAsync("downloads", id, cancellationToken).ConfigureAwait(false);
        string destination = job.GetProperty("destination").GetString()!;
        string partial = destination + ".wincare.download";
        bool completeExists = File.Exists(destination);
        bool partialExists = File.Exists(partial);
        long bytes = completeExists ? new FileInfo(destination).Length : partialExists ? new FileInfo(partial).Length : 0;
        string state = completeExists ? "Completed" : partialExists ? "Suspended" : "Pending";
        await PatchStateItemAsync("downloads", id, node =>
        {
            node["state"] = state;
            node["bytes"] = completeExists ? bytes : 0;
            node["partialBytes"] = partialExists ? bytes : 0;
            node["updatedAt"] = DateTimeOffset.UtcNow;
        }, cancellationToken).ConfigureAwait(false);
        return Success("download-reconcile", "Download job reconciled with complete and partial filesystem state.", new { id, destination, partial, state, bytes });
    }

    private async Task<CommandHandlerOutcome> DownloadCancelAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string id = p.RequiredString("Id");
        JsonElement job = await FindStateItemAsync("downloads", id, cancellationToken).ConfigureAwait(false);
        string destination = job.GetProperty("destination").GetString()!;
        string partial = destination + ".wincare.download";
        if (_operations.TryGetValue("download:" + id, out CancellationTokenSource? source))
        {
            await PatchStateItemAsync("downloads", id, node =>
            {
                node["state"] = "CancelRequested";
                node["updatedAt"] = DateTimeOffset.UtcNow;
            }, cancellationToken).ConfigureAwait(false);
            source.Cancel();
            return Success("download-cancel", "Download cancellation requested. Partial data will be removed by the active worker.", new { id, state = "CancelRequested" });
        }
        TryDeleteFile(partial);
        await PatchStateItemAsync("downloads", id, node =>
        {
            node["state"] = "Cancelled";
            node["partialBytes"] = 0;
            node["updatedAt"] = DateTimeOffset.UtcNow;
        }, cancellationToken).ConfigureAwait(false);
        return Success("download-cancel", "Download cancelled and retained partial data removed.", new { id, state = "Cancelled" });
    }

    private async Task<string> GetDownloadStateAsync(string id, CancellationToken cancellationToken)
    {
        JsonElement job = await FindStateItemAsync("downloads", id, cancellationToken).ConfigureAwait(false);
        return job.TryGetProperty("state", out JsonElement state) ? state.GetString() ?? string.Empty : string.Empty;
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<CommandHandlerOutcome> DownloadRemoveAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string id = p.RequiredString("Id");
        if (_operations.ContainsKey("download:" + id)) return Block("download-remove", "Stop the active download before removing its record.");
        JsonElement job = await FindStateItemAsync("downloads", id, cancellationToken).ConfigureAwait(false);
        string destination = job.GetProperty("destination").GetString()!;
        TryDeleteFile(destination + ".wincare.download");
        return await RemoveStateItemAsync("downloads", id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandHandlerOutcome> DownloadBatchAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> ids = p.StringArray("Ids");
        if (ids.Count == 0) throw new CommandParameterException("Ids", "Provide at least one download Id.");
        var results = new List<object>();
        int failures = 0;
        foreach (string id in ids)
        {
            CommandHandlerOutcome result = await DownloadStartAsync(new CommandParameters(Data(new { Id = id })), cancellationToken).ConfigureAwait(false);
            if (result.Status != CommandResultStatus.Succeeded) failures++;
            results.Add(new { id, status = result.Status.ToString(), result.Code, result.Message });
        }
        JsonElement data = Data(results);
        return failures == 0
            ? CommandHandlerOutcome.Succeeded("download-batch.ok", "Download batch completed.", data)
            : new CommandHandlerOutcome(CommandResultStatus.Failed, "download-batch.partial_failure", $"Download batch completed with {failures} failed job(s). Inspect individual results before retrying.", data, UndoAvailable: false);
    }

    private CommandHandlerOutcome TelemetrySnapshot()
    {
        using Process current = Process.GetCurrentProcess();
        return Success("telemetry-snapshot", "Local WinCare process telemetry captured.", new
        {
            timestamp = DateTimeOffset.UtcNow,
            processId = current.Id,
            workingSetBytes = current.WorkingSet64,
            privateBytes = current.PrivateMemorySize64,
            threads = current.Threads.Count,
            handles = SafeHandleCount(current),
            totalProcessorTimeMs = current.TotalProcessorTime.TotalMilliseconds,
            gcMemoryBytes = GC.GetTotalMemory(forceFullCollection: false),
        });
    }

    private CommandHandlerOutcome LauncherSearch(CommandParameters p)
    {
        string query = p.RequiredString("Query");
        JsonElement applicationData = Applications().Data ?? Data(Array.Empty<object>());
        JsonElement[] rows = applicationData.ValueKind == JsonValueKind.Array
            ? applicationData.EnumerateArray()
                .Where(item => (item.TryGetProperty("displayName", out JsonElement name) ? name.GetString() ?? string.Empty : string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(p.Int32("Limit", 50, 1, 500)).Select(item => item.Clone()).ToArray()
            : [];
        return Success("launcher-search", $"Found {rows.Length} installed-application matches.", rows);
    }

    private CommandHandlerOutcome LauncherOpen(CommandParameters p)
    {
        string target = p.RequiredString("Target").Trim();
        if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" or "ms-settings" or "shell")
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return Success("launcher-open", "Target opened through Windows shell association.", new { target = uri.AbsoluteUri });
        }
        string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(target));
        if (!File.Exists(full) && !Directory.Exists(full)) throw new CommandParameterException("Target", "Target must be an existing path or an allowed URI (http, https, ms-settings, shell).");
        Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
        return Success("launcher-open", "Target opened through Windows shell association.", new { target = full });
    }

    private CommandHandlerOutcome Calculator(CommandParameters p)
    {
        string expression = p.RequiredString("Expression");
        if (expression.Length > 1024) throw new CommandParameterException("Expression", "Expression exceeds 1024 characters.");
        double result = new ArithmeticParser(expression).Parse();
        return Success("calculator", "Expression evaluated with WinCare's local arithmetic parser.", new { expression, result });
    }

    private CommandHandlerOutcome SteamGames()
    {
        string? root = FindSteamRoot();
        if (root is null) return Success("steam-games", "Steam installation was not found.", Array.Empty<object>());
        string libraryFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        if (File.Exists(libraryFile))
        {
            foreach (string line in File.ReadLines(libraryFile))
            {
                string? path = ParseQuotedValue(line, "path");
                if (path is not null) libraries.Add(path.Replace("\\\\", "\\"));
            }
        }
        var games = new List<object>();
        foreach (string library in libraries)
        {
            string steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps)) continue;
            foreach (string manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                string[] lines = File.ReadAllLines(manifest);
                games.Add(new { appId = FindQuoted(lines, "appid"), name = FindQuoted(lines, "name"), installDir = FindQuoted(lines, "installdir"), manifest, library });
            }
        }
        return Success("steam-games", $"Found {games.Count} Steam application manifests.", games);
    }

    private CommandHandlerOutcome SteamUsers()
    {
        string? root = FindSteamRoot();
        if (root is null) return Success("steam-users", "Steam installation was not found.", Array.Empty<object>());
        string userData = Path.Combine(root, "userdata");
        string[] users = Directory.Exists(userData) ? Directory.EnumerateDirectories(userData).Select(Path.GetFileName).Where(name => ulong.TryParse(name, out _)).Cast<string>().ToArray() : [];
        return Success("steam-users", $"Found {users.Length} Steam userdata profiles.", users);
    }

    private CommandHandlerOutcome SteamCloudFiles(CommandParameters p)
    {
        string? root = FindSteamRoot();
        if (root is null) return Success("steam-cloud-files", "Steam installation was not found.", Array.Empty<object>());
        string userId = p.String("UserId").Trim();
        string appId = p.String("AppId").Trim();
        IEnumerable<string> roots = Directory.Exists(Path.Combine(root, "userdata")) ? Directory.EnumerateDirectories(Path.Combine(root, "userdata")) : [];
        if (userId.Length > 0) roots = roots.Where(path => string.Equals(Path.GetFileName(path), userId, StringComparison.Ordinal));
        var files = new List<object>();
        foreach (string userRoot in roots)
        {
            IEnumerable<string> appRoots = Directory.EnumerateDirectories(userRoot);
            if (appId.Length > 0) appRoots = appRoots.Where(path => string.Equals(Path.GetFileName(path), appId, StringComparison.Ordinal));
            foreach (string appRoot in appRoots)
            {
                string remote = Path.Combine(appRoot, "remote");
                if (!Directory.Exists(remote)) continue;
                foreach (string file in Directory.EnumerateFiles(remote, "*", SafeRecursiveEnumeration).Take(5000))
                {
                    var info = new FileInfo(file);
                    files.Add(new { userId = Path.GetFileName(userRoot), appId = Path.GetFileName(appRoot), path = file, info.Length, info.LastWriteTimeUtc });
                }
            }
        }
        return Success("steam-cloud-files", $"Found {files.Count} Steam cloud/local remote files.", files);
    }

    private async Task<CommandHandlerOutcome> SteamBackupAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string source = RequireExistingDirectory(p.RequiredString("Source"));
        string destination = Path.GetFullPath(Environment.ExpandEnvironmentVariables(p.RequiredString("Destination")));
        if (IsSubPath(source, destination) || IsSubPath(destination, source)) throw new CommandParameterException("Destination", "Backup source and destination must not contain one another.");
        long bytes = await CopyDirectoryAsync(source, destination, overwrite: p.Boolean("Overwrite"), cancellationToken).ConfigureAwait(false);
        return Success("steam-backup", "Steam data backup completed.", new { source, destination, bytes }, undo: false);
    }

    private async Task<CommandHandlerOutcome> SteamRestoreAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string source = RequireExistingDirectory(p.RequiredString("Source"));
        string destination = Path.GetFullPath(Environment.ExpandEnvironmentVariables(p.RequiredString("Destination")));
        if (IsSubPath(source, destination) || IsSubPath(destination, source)) throw new CommandParameterException("Destination", "Restore source and destination must not contain one another.");
        long bytes = await CopyDirectoryAsync(source, destination, overwrite: p.Boolean("Overwrite", true), cancellationToken).ConfigureAwait(false);
        return Success("steam-restore", "Steam data restore completed.", new { source, destination, bytes }, undo: false);
    }

    private async Task<CommandHandlerOutcome> GameIntegrityAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string root = RequireExistingDirectory(p.RequiredString("Path"));
        int limit = p.Int32("Limit", 20000, 1, 100000);
        long totalBytes = 0; int files = 0; var hashes = new List<object>();
        foreach (string path in Directory.EnumerateFiles(root, "*", SafeRecursiveEnumeration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++files > limit) break;
            var info = new FileInfo(path); totalBytes += info.Length;
            if (info.Length <= 64L * 1024 * 1024 && hashes.Count < 500)
            {
                await using FileStream stream = File.OpenRead(path);
                string digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
                hashes.Add(new { path = Path.GetRelativePath(root, path), info.Length, sha256 = digest });
            }
        }
        return Success("game-integrity", "Game directory integrity inventory calculated.", new { root, filesScanned = Math.Min(files, limit), totalBytes, sampledHashes = hashes });
    }

    private CommandHandlerOutcome ImageMetadata(CommandParameters p)
    {
        string path = RequireExistingFile(p.RequiredString("Path"));
        var info = new FileInfo(path);
        byte[] header = new byte[Math.Min(64, (int)Math.Min(info.Length, 64))];
        using (FileStream stream = File.OpenRead(path)) stream.ReadExactly(header);
        string kind = DetectImageKind(header);
        return Success("image-metadata", "Image file metadata inspected without loading untrusted codecs.", new { path, info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc, extension = info.Extension, detectedKind = kind });
    }

    private CommandHandlerOutcome BrowserInventory()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mozilla Firefox", "firefox.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Opera", "opera.exe"),
        ];
        var rows = candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).Select(path => new { path, product = FileVersionInfo.GetVersionInfo(path).ProductName, version = FileVersionInfo.GetVersionInfo(path).FileVersion }).ToArray();
        return Success("browsers", $"Found {rows.Length} supported browser installations.", rows);
    }

    private CommandHandlerOutcome BrowserExtensions()
    {
        var rows = new List<object>();
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach ((string browser, string root) in new[]
        {
            ("Chrome", Path.Combine(local, "Google", "Chrome", "User Data")),
            ("Edge", Path.Combine(local, "Microsoft", "Edge", "User Data")),
        })
        {
            if (!Directory.Exists(root)) continue;
            foreach (string profile in Directory.EnumerateDirectories(root).Where(path => Path.GetFileName(path).Equals("Default", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).StartsWith("Profile ", StringComparison.OrdinalIgnoreCase)))
            {
                string extRoot = Path.Combine(profile, "Extensions");
                if (!Directory.Exists(extRoot)) continue;
                foreach (string extension in Directory.EnumerateDirectories(extRoot)) rows.Add(new { browser, profile = Path.GetFileName(profile), id = Path.GetFileName(extension), path = extension });
            }
        }
        return Success("browser-extensions", $"Found {rows.Count} Chromium extension directories.", rows);
    }

    private async Task<CommandHandlerOutcome> RemoteSupportStateAsync(CancellationToken cancellationToken)
    {
        string[] services = ["TermService", "RemoteRegistry", "WinRM"];
        var rows = new List<object>(services.Length);
        foreach (string name in services)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessExecutionResult result = await _process.RunAsync("sc.exe", ["query", name], cancellationToken, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            string state = result.ExitCode == 0
                ? result.StandardOutput.Split('\n').FirstOrDefault(value => value.Contains("STATE", StringComparison.OrdinalIgnoreCase))?.Trim() ?? "Unknown"
                : "Unavailable";
            rows.Add(new { name, state, exitCode = result.ExitCode });
        }
        return Success("remote-support", "Windows remote-support service state inspected.", rows);
    }

    private async Task<CommandHandlerOutcome> StudioSnapshotAsync(CancellationToken cancellationToken)
    {
        JsonElement workspaces = await _state.ReadArrayAsync("studio-file-workspaces", cancellationToken).ConfigureAwait(false);
        JsonElement layouts = await _state.ReadArrayAsync("studio-layout-profiles", cancellationToken).ConfigureAwait(false);
        JsonElement brightness = await _state.ReadArrayAsync("studio-brightness-schedules", cancellationToken).ConfigureAwait(false);
        return Success("studio-snapshot", "WinCare Studio state and native tool capabilities captured.", new
        {
            workspaces,
            layouts,
            brightness,
            tools = new[] { StudioToolValidation("kanata", ["kanata.exe"]).Data, StudioToolValidation("syncthing", ["syncthing.exe"]).Data, StudioToolValidation("wezterm", ["wezterm.exe"]).Data, StudioToolValidation("adb", ["adb.exe"]).Data },
        });
    }

    private CommandHandlerOutcome StudioToolValidation(string id, string[] executables)
    {
        string? executable = executables.Select(FindExecutable).FirstOrDefault(path => path is not null);
        return Success("studio-" + id, executable is null ? $"{id} is not installed or not on PATH." : $"{id} executable located.", new { id, available = executable is not null, executable });
    }

    private async Task<CommandHandlerOutcome> StudioWezTermAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string name = p.String("Name", "wincare-status.lua").Trim();
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9._-]+\.lua$", RegexOptions.CultureInvariant) || name.Length is < 3 or > 80)
        {
            throw new CommandParameterException("Name", "Name must be a 3-80 character local .lua module name.");
        }

        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinCare", "Studio", "WezTerm");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, name);
        const string lua = """
local wezterm = require 'wezterm'
local M = {}
function M.apply(config)
  wezterm.on('update-status', function(window, pane)
    local cwd = pane:get_current_working_dir()
    local cwd_text = cwd and cwd.file_path or ''
    local workspace = window:active_workspace()
    local host = wezterm.hostname()
    window:set_right_status(wezterm.format({
      { Foreground = { AnsiColor = 'Aqua' } }, { Text = ' ' .. workspace .. ' ' },
      { Foreground = { AnsiColor = 'Fuchsia' } }, { Text = host .. ' ' },
      { Foreground = { AnsiColor = 'Grey' } }, { Text = cwd_text .. ' ' },
      { Text = wezterm.strftime('%Y-%m-%d %H:%M') .. ' ' },
    }))
  end)
  return config
end
return M
""";
        string temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, lua, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
        return Success("studio-wezterm", "Local WezTerm status module written inside the WinCare Studio boundary.", new { path, sha256 = HashFile(path) });
    }

    private async Task<CommandHandlerOutcome> StudioAdbAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string adb = RequireExistingFile(p.RequiredString("AdbPath"), 256L * 1024 * 1024);
        string expected = p.RequiredString("Sha256").Trim().ToLowerInvariant();
        if (!Regex.IsMatch(expected, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant))
        {
            throw new CommandParameterException("Sha256", "Sha256 must contain exactly 64 hexadecimal characters.");
        }
        string actual = HashFile(adb);
        if (!actual.Equals(expected, StringComparison.Ordinal))
        {
            return Block("studio-adb-inventory", "ADB executable hash does not match the reviewed SHA-256 value.");
        }

        ProcessExecutionResult result = await _process.RunAsync(adb, ["devices", "-l"], cancellationToken, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        return result.ExitCode == 0
            ? Success("studio-adb-inventory", "Hash-verified ADB device inventory completed with the fixed read-only argument set.", new { adb, sha256 = actual, output = result.StandardOutput })
            : Block("studio-adb-inventory", $"ADB inventory failed with exit code {result.ExitCode}: {FirstLine(result.StandardError)}");
    }

    private async Task<CommandHandlerOutcome> StudioXboxFseAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string viveTool = RequireExistingFile(p.RequiredString("ViVeToolPath"), 256L * 1024 * 1024);
        string expected = p.RequiredString("Sha256").Trim().ToLowerInvariant();
        if (!Regex.IsMatch(expected, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant))
        {
            throw new CommandParameterException("Sha256", "Sha256 must contain exactly 64 hexadecimal characters.");
        }
        string actual = HashFile(viveTool);
        if (!actual.Equals(expected, StringComparison.Ordinal))
        {
            return Block("studio-xbox-fse", "ViVeTool hash does not match the reviewed SHA-256 value.");
        }

        bool enabled = p.Boolean("Enabled", true);
        bool basic = p.Boolean("BasicFeatureSet", false);
        string ids = basic ? "/id:52580392,50902630" : "/id:52580392,50902630,59765208";
        string verb = enabled ? "/enable" : "/disable";
        string opposite = enabled ? "/disable" : "/enable";

        using RegistryKey oem = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\OEM", writable: true)
            ?? throw new UnauthorizedAccessException("Xbox Full Screen Experience OEM registry state could not be opened for writing.");
        const string valueName = "DeviceForm";
        bool existed = oem.GetValueNames().Contains(valueName, StringComparer.OrdinalIgnoreCase);
        object? beforeValue = existed ? oem.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) : null;
        RegistryValueKind? beforeKind = existed ? oem.GetValueKind(valueName) : null;

        oem.SetValue(valueName, enabled ? 46 : 0, RegistryValueKind.DWord);
        ProcessExecutionResult result = await _process.RunAsync(viveTool, [verb, ids], cancellationToken, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            RestoreRegistryValue(oem, valueName, existed, beforeValue, beforeKind);
            ProcessExecutionResult? rollbackResult = null;
            Exception? rollbackError = null;
            try
            {
                rollbackResult = await _process.RunAsync(
                    viveTool,
                    [opposite, ids],
                    CancellationToken.None,
                    TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is CommandDependencyException or TimeoutException)
            {
                rollbackError = ex;
            }

            string rollbackStatus = rollbackError is not null
                ? $"Feature rollback could not be confirmed: {rollbackError.Message}"
                : rollbackResult?.ExitCode == 0
                    ? "Feature rollback completed."
                    : $"Feature rollback returned exit code {rollbackResult?.ExitCode}; manual review is required.";
            return CommandHandlerOutcome.Failed(
                "studio-xbox-fse.failed",
                $"ViVeTool apply failed with exit code {result.ExitCode}; registry state was rolled back. {rollbackStatus} {FirstLine(result.StandardError)}");
        }

        return Success("studio-xbox-fse", "Xbox Full Screen Experience feature IDs and DeviceForm state were updated with a hash-verified ViVeTool binary.", new { viveTool, sha256 = actual, enabled, ids, restartRequired = true }, undo: false);
    }

    private static void RestoreRegistryValue(RegistryKey key, string name, bool existed, object? value, RegistryValueKind? kind)
    {
        if (!existed)
        {
            key.DeleteValue(name, throwOnMissingValue: false);
            return;
        }
        if (kind is null || value is null) throw new InvalidDataException($"Cannot restore registry value '{name}' without its original type and value.");
        key.SetValue(name, value, kind.Value);
    }

    private CommandHandlerOutcome FolderAppearance(CommandParameters p)
    {
        string path = RequireExistingDirectory(p.RequiredString("Path"));
        string desktopIni = Path.Combine(path, "desktop.ini");
        string icon = p.String("IconPath").Trim();
        if (icon.Length == 0)
        {
            if (File.Exists(desktopIni)) File.Delete(desktopIni);
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            return Success("studio-folder-appearance", "Folder appearance override removed.", new { path }, undo: false);
        }
        string iconPath = RequireExistingFile(icon);
        string content = $"[.ShellClassInfo]{Environment.NewLine}IconResource={iconPath},0{Environment.NewLine}";
        File.WriteAllText(desktopIni, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.SetAttributes(desktopIni, FileAttributes.Hidden | FileAttributes.System);
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        return Success("studio-folder-appearance", "Folder icon metadata updated through desktop.ini.", new { path, iconPath }, undo: false);
    }

    private CommandHandlerOutcome ToolkitDiagnostics(CancellationToken cancellationToken)
    {
        var checks = new List<object>();
        foreach (string tool in new[] { "dism.exe", "sfc.exe", "bcdedit.exe", "netsh.exe", "wevtutil.exe", "logman.exe", "gpresult.exe" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? path = FindExecutable(tool);
            checks.Add(new { tool, available = path is not null, path });
        }
        return Success("toolkit-diagnostics", "Native Windows tooling capability audit completed.", checks);
    }

    private CommandHandlerOutcome Win32ErrorLookup(CommandParameters p)
    {
        int code = p.Int32("Code");
        string message = new System.ComponentModel.Win32Exception(code).Message;
        return Success("toolkit-win32-error", "Win32 error decoded.", new { code, hex = $"0x{code:X8}", message });
    }

    private CommandHandlerOutcome ToolkitMsi(CommandParameters p)
    {
        string path = RequireExistingFile(p.RequiredString("Path"));
        if (!path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) throw new CommandParameterException("Path", "Path must reference an .msi file.");
        var info = new FileInfo(path);
        return Success("toolkit-msi", "MSI package file inspected safely without executing it.", new { path, info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc, sha256 = HashFile(path) });
    }

    private CommandHandlerOutcome SystemShortcuts()
    {
        var rows = new[]
        {
            new { id = "settings", target = "ms-settings:" },
            new { id = "windows-update", target = "ms-settings:windowsupdate" },
            new { id = "network", target = "ms-settings:network-status" },
            new { id = "storage", target = "ms-settings:storagesense" },
            new { id = "security", target = "windowsdefender:" },
            new { id = "task-manager", target = "taskmgr.exe" },
        };
        return Success("system-shortcuts", "Native Windows shortcut catalog generated.", rows);
    }

    private async Task<CommandHandlerOutcome> SystemShortcutsExportAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        JsonElement data = SystemShortcuts().Data ?? Data(Array.Empty<object>());
        string path = _state.ResolveExportPath(p.String("Path"), "wincare-system-shortcuts.json");
        await WriteJsonExportAsync(path, data, cancellationToken).ConfigureAwait(false);
        return Success("system-shortcuts-export", "System shortcut catalog exported.", new { path, count = data.GetArrayLength() });
    }

    private CommandHandlerOutcome TorrentMetadata(CommandParameters p)
    {
        string path = RequireExistingFile(p.RequiredString("Path"));
        if (new FileInfo(path).Length > 16L * 1024 * 1024) throw new CommandParameterException("Path", "Torrent metadata exceeds the 16 MiB parser limit.");
        byte[] bytes = File.ReadAllBytes(path);
        object? parsed = new BencodeParser(bytes).Parse();
        return Success("torrent-metadata", "Torrent bencode metadata parsed locally.", new { path, length = bytes.Length, sha1 = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant(), metadata = parsed });
    }

    private async Task<JsonElement> FindStateItemAsync(string key, string id, CancellationToken cancellationToken)
    {
        JsonElement current = await _state.ReadArrayAsync(key, cancellationToken).ConfigureAwait(false);
        if (current.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in current.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out JsonElement idElement) && string.Equals(idElement.GetString(), id, StringComparison.Ordinal)) return item.Clone();
            }
        }
        throw new CommandParameterException("Id", $"State item '{id}' was not found.");
    }

    private static bool IsDownloadDue(JsonElement item, DateTimeOffset now)
    {
        string state = item.TryGetProperty("state", out JsonElement s) ? s.GetString() ?? string.Empty : string.Empty;
        if (!state.Equals("Pending", StringComparison.OrdinalIgnoreCase) && !state.Equals("Suspended", StringComparison.OrdinalIgnoreCase)) return false;
        return !item.TryGetProperty("dueAt", out JsonElement due) || due.ValueKind == JsonValueKind.Null || !due.TryGetDateTimeOffset(out DateTimeOffset dueAt) || dueAt <= now;
    }

    private static Uri RequiredHttpUri(CommandParameters p, string name)
    {
        string raw = p.RequiredString(name);
        if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")) throw new CommandParameterException(name, $"Parameter '{name}' must be an absolute HTTP or HTTPS URL.");
        return uri;
    }

    private static string SafeFileNameFromUri(Uri uri)
    {
        string fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "download.bin";
        foreach (char invalid in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(invalid, '_');
        return fileName;
    }

    private static string SanitizeRegistryLeaf(string value)
    {
        string result = value.Trim();
        if (result.Length == 0 || result.Length > 128 || result.Contains('\\') || result.Contains('/')) throw new CommandParameterException("Id", "Registry entry Id must be a single key name up to 128 characters.");
        return result;
    }

    private static Dictionary<string, object?> RegistryValues(RegistryKey? key)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (key is null) return values;
        foreach (string name in key.GetValueNames()) values[name.Length == 0 ? "(default)" : name] = key.GetValue(name)?.ToString();
        return values;
    }

    private static string? FindSteamRoot()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        string? root = key?.GetValue("SteamPath")?.ToString();
        if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root)) return Path.GetFullPath(root);
        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return Directory.Exists(fallback) ? fallback : null;
    }

    private static string? ParseQuotedValue(string line, string key)
    {
        string trimmed = line.Trim();
        string prefix = '"' + key + '"';
        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        string rest = trimmed[prefix.Length..].Trim();
        return rest.Length >= 2 && rest[0] == '"' && rest[^1] == '"' ? rest[1..^1] : null;
    }

    private static string FindQuoted(IEnumerable<string> lines, string key) => lines.Select(line => ParseQuotedValue(line, key)).FirstOrDefault(value => value is not null) ?? string.Empty;

    private static int SafeHandleCount(Process process) { try { return process.HandleCount; } catch { return 0; } }


    private static string DetectImageKind(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith([0x89, 0x50, 0x4E, 0x47])) return "PNG";
        if (header.StartsWith([0xFF, 0xD8, 0xFF])) return "JPEG";
        if (header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8)) return "GIF";
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)) return "WebP";
        if (header.Length >= 2 && header[..2].SequenceEqual("BM"u8)) return "BMP";
        return "Unknown";
    }

    private static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool IsSubPath(string parent, string candidate)
    {
        string normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar;
        string normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<long> CopyDirectoryAsync(string source, string destination, bool overwrite, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        long total = 0;
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SafeRecursiveEnumeration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (string file in Directory.EnumerateFiles(source, "*", SafeRecursiveEnumeration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using FileStream input = new(file, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using FileStream output = new(target, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false); total += input.Length;
        }
        return total;
    }

    private static void DeleteDirectoryTreeSafe(string root, CancellationToken cancellationToken)
    {
        string normalizedRoot = Path.GetFullPath(root);
        if (!Directory.Exists(normalizedRoot)) return;

        var pending = new Stack<string>();
        var postOrder = new List<string>();
        pending.Push(normalizedRoot);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string current = pending.Pop();
            FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(current, recursive: false);
                continue;
            }

            postOrder.Add(current);
            foreach (string entry in Directory.EnumerateFileSystemEntries(current, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes entryAttributes = File.GetAttributes(entry);
                if ((entryAttributes & FileAttributes.Directory) != 0)
                {
                    if ((entryAttributes & FileAttributes.ReparsePoint) != 0)
                    {
                        Directory.Delete(entry, recursive: false);
                    }
                    else
                    {
                        pending.Push(entry);
                    }
                }
                else
                {
                    File.Delete(entry);
                }
            }
        }

        for (int index = postOrder.Count - 1; index >= 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Delete(postOrder[index], recursive: false);
        }
    }

    private sealed class ArithmeticParser
    {
        private readonly string _text; private int _index;
        public ArithmeticParser(string text) => _text = text;
        public double Parse() { double value = ParseExpression(); Skip(); if (_index != _text.Length) throw new CommandParameterException("Expression", $"Unexpected token at position {_index}."); return value; }
        private double ParseExpression() { double value = ParseTerm(); while (true) { Skip(); if (Take('+')) value += ParseTerm(); else if (Take('-')) value -= ParseTerm(); else return value; } }
        private double ParseTerm() { double value = ParseFactor(); while (true) { Skip(); if (Take('*')) value *= ParseFactor(); else if (Take('/')) { double divisor = ParseFactor(); if (divisor == 0) throw new CommandParameterException("Expression", "Division by zero."); value /= divisor; } else return value; } }
        private double ParseFactor() { Skip(); if (Take('+')) return ParseFactor(); if (Take('-')) return -ParseFactor(); if (Take('(')) { double v = ParseExpression(); Skip(); if (!Take(')')) throw new CommandParameterException("Expression", "Missing closing parenthesis."); return v; } int start = _index; while (_index < _text.Length && (char.IsDigit(_text[_index]) || _text[_index] == '.')) _index++; if (start == _index || !double.TryParse(_text[start.._index], NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) throw new CommandParameterException("Expression", $"Expected number at position {start}."); return n; }
        private void Skip() { while (_index < _text.Length && char.IsWhiteSpace(_text[_index])) _index++; }
        private bool Take(char ch) { if (_index < _text.Length && _text[_index] == ch) { _index++; return true; } return false; }
    }

    private sealed class BencodeParser
    {
        private readonly byte[] _data; private int _index; private const int MaxDepth = 64;
        public BencodeParser(byte[] data) => _data = data;
        public object? Parse() { object? value = ParseValue(0); if (_index != _data.Length) throw new InvalidDataException("Trailing data after bencode value."); return value; }
        private object? ParseValue(int depth)
        {
            if (depth > MaxDepth || _index >= _data.Length) throw new InvalidDataException("Invalid or excessively nested bencode data.");
            return _data[_index] switch
            {
                (byte)'i' => ParseInteger(),
                (byte)'l' => ParseList(depth + 1),
                (byte)'d' => ParseDictionary(depth + 1),
                >= (byte)'0' and <= (byte)'9' => ParseBytes(),
                _ => throw new InvalidDataException($"Invalid bencode token at byte {_index}."),
            };
        }
        private long ParseInteger() { _index++; int start = _index; while (_index < _data.Length && _data[_index] != (byte)'e') _index++; if (_index >= _data.Length || !long.TryParse(Encoding.ASCII.GetString(_data, start, _index - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)) throw new InvalidDataException("Invalid bencode integer."); _index++; return value; }
        private object ParseBytes() { int start = _index; while (_index < _data.Length && char.IsDigit((char)_data[_index])) _index++; if (_index >= _data.Length || _data[_index] != (byte)':' || !int.TryParse(Encoding.ASCII.GetString(_data, start, _index - start), out int len) || len < 0 || len > _data.Length - _index - 1) throw new InvalidDataException("Invalid bencode byte string."); _index++; byte[] bytes = _data.AsSpan(_index, len).ToArray(); _index += len; return IsMostlyText(bytes) ? Encoding.UTF8.GetString(bytes) : Convert.ToBase64String(bytes); }
        private List<object?> ParseList(int depth) { _index++; var list = new List<object?>(); while (_index < _data.Length && _data[_index] != (byte)'e') { if (list.Count >= 100_000) throw new InvalidDataException("Bencode list exceeds safety limit."); list.Add(ParseValue(depth)); } if (_index >= _data.Length) throw new InvalidDataException("Unterminated bencode list."); _index++; return list; }
        private Dictionary<string, object?> ParseDictionary(int depth) { _index++; var dict = new Dictionary<string, object?>(StringComparer.Ordinal); while (_index < _data.Length && _data[_index] != (byte)'e') { object keyObject = ParseBytes(); string key = keyObject as string ?? Convert.ToString(keyObject, CultureInfo.InvariantCulture) ?? string.Empty; if (dict.Count >= 100_000) throw new InvalidDataException("Bencode dictionary exceeds safety limit."); dict[key] = ParseValue(depth); } if (_index >= _data.Length) throw new InvalidDataException("Unterminated bencode dictionary."); _index++; return dict; }
        private static bool IsMostlyText(byte[] bytes) { if (bytes.Length == 0) return true; int printable = bytes.Count(b => b is 9 or 10 or 13 || b >= 32 && b < 127 || b >= 0xC2); return printable >= bytes.Length * 9 / 10; }
    }
    private CommandHandlerOutcome StudioPackageInventory()
    {
        string appRoot = AppContext.BaseDirectory;
        string? manifest = Directory.EnumerateFiles(appRoot, "AppxManifest.xml", SearchOption.TopDirectoryOnly).FirstOrDefault();
        string? packageFamily = Environment.GetEnvironmentVariable("PACKAGE_FAMILY_NAME");
        return Success("studio-package", "WinCare package runtime inspected.", new
        {
            appRoot,
            packaged = manifest is not null || !string.IsNullOrWhiteSpace(packageFamily),
            manifest,
            packageFamily = packageFamily ?? string.Empty,
            version = typeof(WindowsCommandExecutor).Assembly.GetName().Version?.ToString() ?? string.Empty,
            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
        });
    }

}
