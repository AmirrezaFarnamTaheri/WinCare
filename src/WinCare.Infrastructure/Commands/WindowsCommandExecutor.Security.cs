using System.Text.RegularExpressions;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;
namespace WinCare.Infrastructure.Commands;

public sealed partial class WindowsCommandExecutor
{
    private async Task<CommandHandlerOutcome> WindowsUpdateSearchAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string criteria = p.String("Criteria", "IsInstalled=0 and IsHidden=0");
        int limit = p.Int32("Limit", 250, 1, 1000);
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            dynamic session = CreateCom("Microsoft.Update.Session");
            dynamic searcher = session.CreateUpdateSearcher();
            dynamic result = searcher.Search(criteria);
            var updates = new List<object>();
            int count = Math.Min((int)result.Updates.Count, limit);
            for (int index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                dynamic update = result.Updates.Item(index);
                updates.Add(new
                {
                    updateId = (string)update.Identity.UpdateID,
                    revision = (int)update.Identity.RevisionNumber,
                    title = (string)update.Title,
                    description = ((string)update.Description ?? string.Empty).Length > 2048 ? ((string)update.Description)[..2048] : (string)update.Description,
                    isDownloaded = (bool)update.IsDownloaded,
                    isInstalled = (bool)update.IsInstalled,
                    isHidden = (bool)update.IsHidden,
                    rebootRequired = (bool)update.RebootRequired,
                });
            }
            return Success("wua-search", $"Windows Update search returned {updates.Count} updates.", updates);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandHandlerOutcome> WindowsUpdateHistoryAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        int count = p.Int32("Count", 100, 1, 1000);
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            dynamic session = CreateCom("Microsoft.Update.Session");
            dynamic searcher = session.CreateUpdateSearcher();
            int total = (int)searcher.GetTotalHistoryCount();
            int take = Math.Min(count, total);
            dynamic history = searcher.QueryHistory(0, take);
            var rows = new List<object>();
            for (int index = 0; index < (int)history.Count; index++)
            {
                dynamic item = history.Item(index);
                rows.Add(new
                {
                    title = (string)item.Title,
                    date = (DateTime)item.Date,
                    operation = item.Operation.ToString(),
                    resultCode = item.ResultCode.ToString(),
                    hResult = (int)item.HResult,
                    updateId = (string)item.UpdateIdentity.UpdateID,
                });
            }
            return Success("wua-history", $"Read {rows.Count} Windows Update history entries.", rows);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandHandlerOutcome> WindowsUpdateSetHiddenAsync(CommandParameters p, bool hidden, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> ids = p.StringArray("UpdateIds");
        string[] requested = ids.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            dynamic session = CreateCom("Microsoft.Update.Session");
            dynamic searcher = session.CreateUpdateSearcher();
            dynamic result = searcher.Search("IsInstalled=0");
            var candidates = new List<dynamic>();
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < (int)result.Updates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                dynamic update = result.Updates.Item(i);
                string id = (string)update.Identity.UpdateID;
                if (!requested.Contains(id, StringComparer.OrdinalIgnoreCase)) continue;
                candidates.Add(update);
                found.Add(id);
            }

            string[] missing = requested.Except(found, StringComparer.OrdinalIgnoreCase).ToArray();
            if (missing.Length > 0)
            {
                return Block(hidden ? "wua-hide" : "wua-unhide", $"Requested update IDs were not found; no visibility state was changed: {string.Join(", ", missing)}");
            }

            var changed = new List<string>(candidates.Count);
            foreach (dynamic update in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((bool)update.IsHidden != hidden) update.IsHidden = hidden;
                changed.Add((string)update.Identity.UpdateID);
            }
            string command = hidden ? "wua-hide" : "wua-unhide";
            return Success(command, hidden ? "Requested updates were hidden." : "Requested updates were unhidden.", new { updateIds = changed, hidden });
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandHandlerOutcome> WindowsUpdateDownloadAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> ids = p.StringArray("UpdateIds");
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            dynamic collection = SelectUpdates(ids, installed: false, cancellationToken);
            dynamic session = CreateCom("Microsoft.Update.Session");
            dynamic downloader = session.CreateUpdateDownloader();
            downloader.Updates = collection;
            dynamic result = downloader.Download();
            JsonElement data = Data(new { resultCode = result.ResultCode.ToString(), resultCodeValue = (int)result.ResultCode, updateCount = (int)collection.Count });
            return WindowsUpdateOperationOutcome("wua-download", "download", (int)result.ResultCode, data);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandHandlerOutcome> WindowsUpdateInstallAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> ids = p.StringArray("UpdateIds");
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            dynamic collection = SelectUpdates(ids, installed: false, cancellationToken);
            dynamic session = CreateCom("Microsoft.Update.Session");
            dynamic installer = session.CreateUpdateInstaller();
            installer.Updates = collection;
            dynamic result = installer.Install();
            var perUpdate = new List<object>();
            for (int i = 0; i < (int)collection.Count; i++)
            {
                dynamic update = collection.Item(i);
                dynamic itemResult = result.GetUpdateResult(i);
                perUpdate.Add(new { updateId = (string)update.Identity.UpdateID, title = (string)update.Title, resultCode = itemResult.ResultCode.ToString(), hResult = (int)itemResult.HResult });
            }
            JsonElement data = Data(new { resultCode = result.ResultCode.ToString(), resultCodeValue = (int)result.ResultCode, rebootRequired = (bool)result.RebootRequired, updates = perUpdate });
            return WindowsUpdateOperationOutcome("wua-install", "installation", (int)result.ResultCode, data);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandHandlerOutcome> WindowsUpdateUninstallAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> ids = p.StringArray("UpdateIds");
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            dynamic collection = SelectUpdates(ids, installed: true, cancellationToken);
            dynamic session = CreateCom("Microsoft.Update.Session");
            dynamic installer = session.CreateUpdateInstaller();
            installer.Updates = collection;
            dynamic result = installer.Uninstall();
            JsonElement data = Data(new { resultCode = result.ResultCode.ToString(), resultCodeValue = (int)result.ResultCode, rebootRequired = (bool)result.RebootRequired, updateCount = (int)collection.Count });
            return WindowsUpdateOperationOutcome("wua-uninstall", "removal", (int)result.ResultCode, data);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static CommandHandlerOutcome WindowsUpdateOperationOutcome(string commandId, string operation, int resultCode, JsonElement data)
    {
        // WUA OperationResultCode: 2 = succeeded, 3 = succeeded with errors, 4 = failed, 5 = aborted.
        return resultCode == 2
            ? CommandHandlerOutcome.Succeeded(commandId + ".ok", $"Windows Update {operation} succeeded.", data)
            : new CommandHandlerOutcome(
                CommandResultStatus.Failed,
                commandId + ".operation_failed",
                $"Windows Update {operation} did not fully succeed (result code {resultCode}).",
                data,
                UndoAvailable: false);
    }

    private static dynamic CreateCom(string progId)
    {
        Type? type = Type.GetTypeFromProgID(progId, throwOnError: false);
        if (type is null) throw new CommandDependencyException(progId, $"Windows COM component '{progId}' is unavailable.");
        return Activator.CreateInstance(type) ?? throw new CommandDependencyException(progId, $"Windows COM component '{progId}' could not be created.");
    }

    private static dynamic SelectUpdates(IReadOnlyList<string> ids, bool installed, CancellationToken cancellationToken)
    {
        string[] requested = ids.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        dynamic session = CreateCom("Microsoft.Update.Session");
        dynamic searcher = session.CreateUpdateSearcher();
        dynamic result = searcher.Search(installed ? "IsInstalled=1" : "IsInstalled=0");
        var candidates = new List<dynamic>();
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < (int)result.Updates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            dynamic update = result.Updates.Item(i);
            string id = (string)update.Identity.UpdateID;
            if (!requested.Contains(id, StringComparer.OrdinalIgnoreCase)) continue;
            candidates.Add(update);
            found.Add(id);
        }

        string[] missing = requested.Except(found, StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length > 0)
        {
            throw new CommandParameterException("UpdateIds", $"Update IDs were not found in the requested state: {string.Join(", ", missing)}");
        }

        dynamic collection = CreateCom("Microsoft.Update.UpdateColl");
        foreach (dynamic update in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!(bool)update.EulaAccepted) update.AcceptEula();
            collection.Add(update);
        }
        return collection;
    }

    private async Task<CommandHandlerOutcome> WdacPoliciesAsync(CancellationToken cancellationToken)
    {
        string? ciTool = FindExecutable("CiTool.exe");
        if (ciTool is null)
        {
            return Block("wdac-policies", "CiTool.exe is unavailable on this Windows build.");
        }
        ProcessExecutionResult result = await _process.RunAsync(ciTool, ["--list-policies", "--json"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) return Block("wdac-policies", $"CiTool failed: {FirstLine(result.StandardError)}");
        object data;
        try { data = JsonNode.Parse(result.StandardOutput) ?? new JsonObject(); }
        catch (JsonException) { data = new { raw = result.StandardOutput }; }
        return Success("wdac-policies", "WDAC policies queried with CiTool.", data);
    }

    private async Task<CommandHandlerOutcome> WdacDeployAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string policy = RequireExistingFile(p.RequiredString("Path"), 64 * 1024 * 1024);
        string? ciTool = FindExecutable("CiTool.exe");
        if (ciTool is null) return Block("wdac-deploy", "CiTool.exe is unavailable on this Windows build.");
        ProcessExecutionResult result = await _process.RunAsync(ciTool, ["--update-policy", policy, "--json"], cancellationToken, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        if (result.ExitCode != 0) return Block("wdac-deploy", $"CiTool rejected the policy: {FirstLine(result.StandardError)}");
        return Success("wdac-deploy", "WDAC policy deployment completed.", new { policy, stdout = result.StandardOutput });
    }

    private CommandHandlerOutcome PagefileSet(CommandParameters p, CancellationToken cancellationToken)
    {
        string mode = p.RequiredString("Mode").Trim();
        using RegistryKey memory = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", writable: true)
            ?? throw new UnauthorizedAccessException("Virtual-memory registry key could not be opened for writing.");
        if (mode.Equals("Automatic", StringComparison.OrdinalIgnoreCase) || mode.Equals("SystemManaged", StringComparison.OrdinalIgnoreCase))
        {
            memory.SetValue("PagingFiles", new[] { "?:\\pagefile.sys" }, RegistryValueKind.MultiString);
            return Success("pagefile-set", "Virtual memory changed to system-managed mode. Restart may be required.", new { mode = "SystemManaged" }, undo: false);
        }

        JsonElement settings = p.Element("Settings");
        if (settings.ValueKind != JsonValueKind.Array) throw new CommandParameterException("Settings", "Settings must be an array.");
        var entries = new List<string>();
        foreach (JsonElement item in settings.EnumerateArray())
        {
            string drive = item.TryGetProperty("Drive", out JsonElement driveElement) ? driveElement.GetString() ?? string.Empty : string.Empty;
            int initial = item.TryGetProperty("InitialMB", out JsonElement initialElement) && initialElement.TryGetInt32(out int i) ? i : 0;
            int maximum = item.TryGetProperty("MaximumMB", out JsonElement maxElement) && maxElement.TryGetInt32(out int m) ? m : 0;
            if (!Regex.IsMatch(drive, "^[A-Za-z]:$", RegexOptions.CultureInvariant) || initial < 16 || maximum < initial)
            {
                throw new CommandParameterException("Settings", "Each pagefile setting requires Drive (for example C:), InitialMB >= 16, and MaximumMB >= InitialMB.");
            }
            entries.Add($"{drive}\\pagefile.sys {initial} {maximum}");
        }
        if (entries.Count == 0) throw new CommandParameterException("Settings", "At least one pagefile setting is required.");
        memory.SetValue("PagingFiles", entries.ToArray(), RegistryValueKind.MultiString);
        return Success("pagefile-set", "Virtual memory settings updated. Restart may be required.", new { mode = "Custom", entries }, undo: false);
    }

    private Task<CommandHandlerOutcome> SecurityControlReduceAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string control = p.RequiredString("Control").Trim();
        _ = p.Int32("DurationMinutes", 15, 5, 1440);
        string reason = p.RequiredString("Reason").Trim();
        string environmentClass = p.String("EnvironmentClass", "Workstation").Trim();

        string[] admitted = ["DefenderRealtime", "SmartScreenShell", "Uac", "WindowsUpdateStack"];
        if (!admitted.Contains(control, StringComparer.OrdinalIgnoreCase))
        {
            throw new CommandParameterException("Control", $"Control must be one of: {string.Join(", ", admitted)}.");
        }
        if (reason.Length > 500) throw new CommandParameterException("Reason", "Reason must not exceed 500 characters.");
        if (!new[] { "Workstation", "Test", "DisposableLab" }.Contains(environmentClass, StringComparer.OrdinalIgnoreCase))
        {
            throw new CommandParameterException("EnvironmentClass", "EnvironmentClass must be Workstation, Test, or DisposableLab.");
        }
        if (control.Equals("Uac", StringComparison.OrdinalIgnoreCase) && !environmentClass.Equals("DisposableLab", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Block("security-control-reduce", "UAC reduction is limited to DisposableLab environments. No host state was changed."));
        }

        // Legacy behavior guarantees authenticated before/after snapshots plus automatic timed restoration.
        // The native app does not yet have a separately launchable recovery host that can honor that timer after
        // the UI process exits. Performing the reduction without that guarantee would weaken the safety contract.
        return Task.FromResult(Block(
            "security-control-reduce",
            $"Temporary {control} reduction is not admitted until the native recovery host can prove authenticated automatic restoration. No host state was changed."));
    }

    private Task<CommandHandlerOutcome> SecurityControlRestoreAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = p.RequiredString("RecordId");
        return Task.FromResult(Block(
            "security-control-restore",
            "No native temporary-security record can be restored because unsafe reductions are not created. No host state was changed."));
    }

    private async Task<CommandHandlerOutcome> NetworkExperimentAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string setting = p.RequiredString("Setting");
        string value = p.RequiredString("Value");
        string normalized = setting.ToLowerInvariant() switch
        {
            "autotuninglevel" or "autotuning" => "autotuninglevel",
            "ecncapability" or "ecn" => "ecncapability",
            "timestamps" => "timestamps",
            _ => throw new CommandParameterException("Setting", "Setting must be AutoTuningLevel, EcnCapability, or Timestamps."),
        };
        ProcessExecutionResult before = await _process.RunAsync("netsh.exe", ["interface", "tcp", "show", "global"], cancellationToken).ConfigureAwait(false);
        ProcessExecutionResult apply = await _process.RunAsync("netsh.exe", ["interface", "tcp", "set", "global", $"{normalized}={value}"], cancellationToken).ConfigureAwait(false);
        if (apply.ExitCode != 0) return Block("network-experiment", $"netsh rejected the experiment: {FirstLine(apply.StandardError)}");
        CommandHandlerOutcome measure = await NetworkMeasureAsync(p, cancellationToken).ConfigureAwait(false);
        JsonElement record = Data(new { id = Guid.NewGuid().ToString("N"), setting = normalized, value, appliedAt = DateTimeOffset.UtcNow, before = before.StandardOutput, measurement = measure.Data });
        await AppendStateItemAsync("network-experiments", record, cancellationToken).ConfigureAwait(false);
        return Success("network-experiment", "TCP experiment applied and measured. Review measurement before keeping the setting.", record, undo: false);
    }

    private async Task<CommandHandlerOutcome> EtwCaptureAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string profile = p.String("Profile", "GeneralProfile");
        int seconds = p.Int32("DurationSeconds", 15, 1, 300);
        string destination = p.String("Destination");
        if (destination.Length == 0) destination = Path.Combine(_state.Root, $"capture-{DateTime.UtcNow:yyyyMMdd-HHmmss}.etl");
        destination = Path.GetFullPath(Environment.ExpandEnvironmentVariables(destination));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string session = "WinCare-" + Guid.NewGuid().ToString("N")[..12];
        ProcessExecutionResult start = await _process.RunAsync("logman.exe", ["start", session, "-ets", "-o", destination, "-p", "Microsoft-Windows-Kernel-Process", "0x10", "5"], cancellationToken).ConfigureAwait(false);
        if (start.ExitCode != 0) return Block("etw-capture", $"ETW session could not start: {FirstLine(start.StandardError)}");
        ProcessExecutionResult? stopResult = null;
        Exception? stopError = null;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                stopResult = await _process.RunAsync(
                    "logman.exe",
                    ["stop", session, "-ets"],
                    CancellationToken.None,
                    TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is CommandDependencyException or TimeoutException)
            {
                stopError = ex;
            }
        }
        if (stopError is not null)
        {
            return CommandHandlerOutcome.Failed(
                "etw-capture.stop_failed",
                $"ETW capture started but WinCare could not confirm that session '{session}' stopped: {stopError.Message}");
        }
        if (stopResult is null || stopResult.ExitCode != 0)
        {
            string detail = stopResult is null ? "No stop result was returned." : FirstLine(stopResult.StandardError.Length > 0 ? stopResult.StandardError : stopResult.StandardOutput);
            return CommandHandlerOutcome.Failed(
                "etw-capture.stop_failed",
                $"ETW capture started but session '{session}' did not stop cleanly. {detail}");
        }
        return Success("etw-capture", $"ETW capture '{profile}' completed and the trace session stopped cleanly.", new { destination, durationSeconds = seconds, session });
    }

    private async Task<CommandHandlerOutcome> InjectionSurfaceQuarantineAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        int pid = p.Int32("ProcessId", 0, 1, int.MaxValue);
        if (pid <= 0)
        {
            string surfaceId = p.RequiredString("SurfaceId");
            Match match = Regex.Match(surfaceId, "(?:pid[:=-]?)(\\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out pid)) throw new CommandParameterException("SurfaceId", "SurfaceId must identify a process as pid:<number>, or provide ProcessId.");
        }
        using Process process = Process.GetProcessById(pid);
        string name = SafeProcessName(process);
        if (pid <= 4 || name.Equals("csrss", StringComparison.OrdinalIgnoreCase) || name.Equals("wininit", StringComparison.OrdinalIgnoreCase) || name.Equals("services", StringComparison.OrdinalIgnoreCase))
        {
            return Block("injection-surface-quarantine", "Critical Windows processes cannot be quarantined by WinCare.");
        }
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return Success("injection-surface-quarantine", $"Process {pid} ({name}) was terminated to quarantine the selected surface.", new { processId = pid, name, terminated = true });
    }

    private async Task<CommandHandlerOutcome> BcdExportAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string destination = Path.GetFullPath(Environment.ExpandEnvironmentVariables(p.RequiredString("Destination")));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        ProcessExecutionResult result = await _process.RunAsync("bcdedit.exe", ["/export", destination], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) return Block("bcd-export", $"BCD export failed: {FirstLine(result.StandardError)}");
        return Success("bcd-export", "Boot Configuration Data exported.", new { destination, exists = File.Exists(destination) });
    }

    private async Task<CommandHandlerOutcome> ProvisioningPlanAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string path = RequireExistingFile(p.RequiredString("Path"), 64 * 1024 * 1024);
        string extension = Path.GetExtension(path);
        if (extension.Equals(".ppkg", StringComparison.OrdinalIgnoreCase))
        {
            ProcessExecutionResult result = await _process.RunAsync("dism.exe", ["/Online", "/Add-ProvisioningPackage", "/PackagePath:" + path, "/QuietInstall"], cancellationToken, TimeSpan.FromMinutes(5)).ConfigureAwait(false);
            if (result.ExitCode != 0) return Block("provisioning-plan", $"DISM rejected the provisioning package: {FirstLine(result.StandardError)}");
            return Success("provisioning-plan", "Provisioning package applied.", new { path, exitCode = result.ExitCode }, undo: false);
        }
        return Block("provisioning-plan", "Native provisioning apply currently admits signed .ppkg packages only; JSON/XML blueprints remain inspect-only until converted to a provisioning package.");
    }

    private async Task<CommandHandlerOutcome> GroupPolicyImportAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(p.RequiredString("Path")));
        if (!Directory.Exists(path) && !File.Exists(path)) throw new CommandParameterException("Path", "Group Policy import path does not exist.");
        string? lgpo = FindExecutable("LGPO.exe");
        if (lgpo is null) return Block("group-policy-import", "LGPO.exe is required for native local Group Policy import and was not found.");
        ProcessExecutionResult result = await _process.RunAsync(lgpo, ["/g", path], cancellationToken, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        if (result.ExitCode != 0) return Block("group-policy-import", $"LGPO import failed: {FirstLine(result.StandardError)}");
        return Success("group-policy-import", "Local Group Policy backup imported with LGPO.", new { path });
    }

    private async Task<CommandHandlerOutcome> SysmonStateAsync(CancellationToken cancellationToken)
    {
        ProcessExecutionResult service = await _process.RunAsync("sc.exe", ["query", "Sysmon64"], cancellationToken).ConfigureAwait(false);
        if (service.ExitCode != 0) service = await _process.RunAsync("sc.exe", ["query", "Sysmon"], cancellationToken).ConfigureAwait(false);
        return Success("sysmon", "Sysmon service state inspected.", new { installed = service.ExitCode == 0, service = service.StandardOutput });
    }

    private async Task<CommandHandlerOutcome> SysmonConfigureAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string config = RequireExistingFile(p.RequiredString("Path"), 16 * 1024 * 1024);
        string? sysmon = FindExecutable("Sysmon64.exe") ?? FindExecutable("Sysmon.exe");
        if (sysmon is null) return Block("sysmon-configure", "Sysmon executable was not found. Install Sysmon from Microsoft Sysinternals first.");
        ProcessExecutionResult result = await _process.RunAsync(sysmon, ["-accepteula", "-c", config], cancellationToken, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        if (result.ExitCode != 0) return Block("sysmon-configure", $"Sysmon rejected the configuration: {FirstLine(result.StandardError)}");
        return Success("sysmon-configure", "Sysmon configuration applied.", new { config });
    }

    private async Task<CommandHandlerOutcome> SysmonUninstallAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string? sysmon = FindExecutable("Sysmon64.exe") ?? FindExecutable("Sysmon.exe");
        if (sysmon is null) return Block("sysmon-uninstall", "Sysmon executable was not found.");
        ProcessExecutionResult result = await _process.RunAsync(sysmon, ["-u", "force"], cancellationToken, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        if (result.ExitCode != 0) return Block("sysmon-uninstall", $"Sysmon uninstall failed: {FirstLine(result.StandardError)}");
        return Success("sysmon-uninstall", "Sysmon was removed.", new { removed = true });
    }

    private async Task<CommandHandlerOutcome> DismOfflineQueryAsync(string id, CommandParameters p, string operation, CancellationToken cancellationToken)
    {
        string image = p.String("ImagePath");
        var args = new List<string> { "/English" };
        args.Add(image.Length == 0 ? "/Online" : "/Image:" + Path.GetFullPath(Environment.ExpandEnvironmentVariables(image)));
        args.Add(operation);
        return await NativeToolQueryAsync(id, "dism.exe", args, cancellationToken, administratorOptional: true).ConfigureAwait(false);
    }

    private async Task<CommandHandlerOutcome> DismOfflineMutationAsync(string id, CommandParameters p, IReadOnlyList<string> operationArgs, CancellationToken cancellationToken)
    {
        string image = Path.GetFullPath(Environment.ExpandEnvironmentVariables(p.RequiredString("ImagePath")));
        if (!Directory.Exists(image)) throw new CommandParameterException("ImagePath", "Offline image path must identify a mounted Windows image directory.");
        var args = new List<string> { "/English", "/Image:" + image };
        args.AddRange(operationArgs);
        ProcessExecutionResult result = await _process.RunAsync("dism.exe", args, cancellationToken, TimeSpan.FromMinutes(10), 2 * 1024 * 1024).ConfigureAwait(false);
        if (result.ExitCode != 0) return Block(id, $"DISM failed: {FirstLine(result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput)}");
        return Success(id, "DISM operation completed.", new { imagePath = image, stdout = result.StandardOutput, exitCode = result.ExitCode });
    }

    private async Task<CommandHandlerOutcome> DismOfflineFeatureSetAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string feature = p.RequiredString("FeatureName");
        bool enable = p.Boolean("Enabled", true);
        return await DismOfflineMutationAsync("offline-feature-set", p, enable
            ? ["/Enable-Feature", "/FeatureName:" + feature, "/All"]
            : ["/Disable-Feature", "/FeatureName:" + feature], cancellationToken).ConfigureAwait(false);
    }

    private CommandHandlerOutcome HardeningAssess(CancellationToken cancellationToken)
    {
        using RegistryKey? system = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
        using RegistryKey? lsa = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa");
        cancellationToken.ThrowIfCancellationRequested();
        FirewallProfileState[] firewallProfiles = ReadFirewallProfileStates();
        var checks = new[]
        {
            new { id = "uac", passed = Convert.ToInt32(system?.GetValue("EnableLUA", 1), CultureInfo.InvariantCulture) != 0 },
            new { id = "lsa-protection", passed = Convert.ToInt32(lsa?.GetValue("RunAsPPL", 0), CultureInfo.InvariantCulture) != 0 },
            new { id = "firewall", passed = firewallProfiles.All(profile => profile.Enabled) },
        };
        return Success("hardening-assess", "Balanced hardening assessment completed.", new { checks, passed = checks.Count(x => x.passed), total = checks.Length });
    }

    private async Task<CommandHandlerOutcome> HardeningApplyAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string profile = p.String("ProfileId", "balanced");
        if (!profile.Equals("balanced", StringComparison.OrdinalIgnoreCase) && !profile.Equals("hail-mary", StringComparison.OrdinalIgnoreCase))
            throw new CommandParameterException("ProfileId", "ProfileId must be 'balanced' or 'hail-mary'.");
        using RegistryKey system = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", writable: true);
        system.SetValue("EnableLUA", 1, RegistryValueKind.DWord);
        system.SetValue("ConsentPromptBehaviorAdmin", 5, RegistryValueKind.DWord);
        using RegistryKey lsa = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa", writable: true);
        lsa.SetValue("RunAsPPL", 1, RegistryValueKind.DWord);
        ProcessExecutionResult firewall = await _process.RunAsync("netsh.exe", ["advfirewall", "set", "allprofiles", "state", "on"], cancellationToken).ConfigureAwait(false);
        if (firewall.ExitCode != 0)
        {
            return new CommandHandlerOutcome(
                CommandResultStatus.Failed,
                "hardening-apply.partial_failure",
                $"Registry hardening was applied, but Windows Firewall could not be enabled: {FirstLine(firewall.StandardError)} Manual review is required.",
                Data(new { profile, registryPolicyApplied = true, firewallEnabled = false }),
                UndoAvailable: false);
        }
        if (profile.Equals("hail-mary", StringComparison.OrdinalIgnoreCase))
        {
            using RegistryKey defender = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender", writable: true);
            defender.SetValue("DisableAntiSpyware", 0, RegistryValueKind.DWord);
        }
        return Success("hardening-apply", "Hardening profile applied using supported Windows policy surfaces.", new { profile, restartMayBeRequired = true }, undo: false);
    }

    private CommandHandlerOutcome VbsAssurance()
    {
        using RegistryKey? dg = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard");
        using RegistryKey? lsa = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa");
        return Success("vbs-assurance", "VBS policy state read from Windows registry.", new
        {
            enableVirtualizationBasedSecurity = Convert.ToInt32(dg?.GetValue("EnableVirtualizationBasedSecurity", 0), CultureInfo.InvariantCulture),
            requirePlatformSecurityFeatures = Convert.ToInt32(dg?.GetValue("RequirePlatformSecurityFeatures", 0), CultureInfo.InvariantCulture),
            lsaCfgFlags = Convert.ToInt32(lsa?.GetValue("LsaCfgFlags", 0), CultureInfo.InvariantCulture),
        });
    }

    private async Task<CommandHandlerOutcome> VbsHardenAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        bool credentialGuard = p.Boolean("IncludeCredentialGuard", true);
        bool hvci = p.Boolean("IncludeHvci", true);
        using RegistryKey dg = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard", writable: true);
        dg.SetValue("EnableVirtualizationBasedSecurity", 1, RegistryValueKind.DWord);
        dg.SetValue("RequirePlatformSecurityFeatures", 1, RegistryValueKind.DWord);
        if (hvci)
        {
            using RegistryKey hvciKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", writable: true);
            hvciKey.SetValue("Enabled", 1, RegistryValueKind.DWord);
        }
        if (credentialGuard)
        {
            using RegistryKey lsa = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa", writable: true);
            lsa.SetValue("LsaCfgFlags", 1, RegistryValueKind.DWord);
        }
        ProcessExecutionResult hypervisor = await _process.RunAsync("bcdedit.exe", ["/set", "hypervisorlaunchtype", "auto"], cancellationToken).ConfigureAwait(false);
        if (hypervisor.ExitCode != 0)
        {
            return new CommandHandlerOutcome(
                CommandResultStatus.Failed,
                "vbs-harden.partial_failure",
                $"VBS registry policy was applied, but hypervisor boot configuration failed: {FirstLine(hypervisor.StandardError)} Manual review is required before restart.",
                Data(new { registryPolicyApplied = true, hypervisorBootConfigured = false, credentialGuard, hvci }),
                UndoAvailable: false);
        }
        return Success("vbs-harden", "Virtualization-based security hardening configured. Restart required to validate final state.", new { credentialGuard, hvci, restartRequired = true }, undo: false);
    }

    private async Task<CommandHandlerOutcome> HypervisorIsolationAsync(CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await _process.RunAsync("systeminfo.exe", Array.Empty<string>(), cancellationToken, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        return Success("hypervisor-isolation", "Hypervisor isolation state inspected.", new { systemInfo = result.StandardOutput, vbs = VbsAssurance().Data });
    }

    private static string? FindExecutable(string fileName)
    {
        if (Path.IsPathRooted(fileName)) return File.Exists(fileName) ? fileName : null;
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string systemCandidate = Path.Combine(system, fileName);
        if (File.Exists(systemCandidate)) return systemCandidate;
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is null) return null;
        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                string candidate = Path.Combine(directory.Trim('"'), fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }
}
