using System.Text.Json;
using Microsoft.Win32;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Commands;

namespace WinCare.Infrastructure.Plugins;

/// <summary>
/// Executes built-in manifest tools whose behavior is narrower than any public core command.
/// The normal dispatcher still owns preview receipts and apply admission for these handlers.
/// </summary>
public sealed class BuiltInPluginCommandHandler : ICommandHandler
{
    private const int MaximumCandidates = 20_000;
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "cleaner.ai_models_huggingface", "cleaner.ai_models_ollama", "cleaner.ai_models_pytorch", "cleaner.ai_models_lmstudio",
        "cleaner.adb_wsa_cache", "cleaner.adb_device_apk_residues",
        "cleaner.redis_dump_aof", "cleaner.redis_temp_logs",
        "cleaner.downloads_incomplete_streams", "cleaner.downloads_torrent_debris", "cleaner.downloads_multipart_chunks",
        "privacy.media_player", "privacy.vlc_history", "privacy.office_mru",
        "gpu.amd_ulps", "gpu.nvidia_pstate", "gpu.intel_async_flip",
        "eventlog.diagnostic", "eventlog.powershell", "eventlog.bits",
    };

    private readonly CommandDefinition _definition;

    private BuiltInPluginCommandHandler(CommandDefinition definition) => _definition = definition;

    public string CommandId => _definition.Id;

    public static ICommandHandler? TryCreate(CommandDefinition definition) =>
        Supported.Contains(definition.Id) ? new BuiltInPluginCommandHandler(definition) : null;

    public async Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            return CommandHandlerOutcome.Blocked("plugin.windows_required", "This built-in tool requires Windows.");

        if (CommandId.StartsWith("gpu.", StringComparison.OrdinalIgnoreCase))
            return ExecuteGpu(request.Apply);
        if (CommandId.StartsWith("privacy.", StringComparison.OrdinalIgnoreCase))
            return ExecutePrivacy(request.Apply);
        if (CommandId.StartsWith("eventlog.", StringComparison.OrdinalIgnoreCase))
            return await ExecuteEventLogAsync(request.Apply, cancellationToken).ConfigureAwait(false);
        return ExecuteScopedCleanup(request.Apply, cancellationToken);
    }

    private CommandHandlerOutcome ExecuteScopedCleanup(bool apply, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> candidates = FindFileCandidates(cancellationToken);
        ulong bytes = 0;
        foreach (string path in candidates)
        {
            try { bytes = checked(bytes + (ulong)new FileInfo(path).Length); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException) { }
        }

        if (!apply)
            return Success("preview", $"Found {candidates.Count:N0} scoped item(s) eligible for this tool.", new { candidates, bytes });

        int removed = 0;
        var failures = new List<object>();
        foreach (string path in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Delete(path);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new { path, error = ex.GetType().Name });
            }
        }
        RemoveEmptyCandidateDirectories(candidates);
        string message = failures.Count == 0
            ? $"Removed {removed:N0} scoped item(s)."
            : $"Removed {removed:N0} scoped item(s); {failures.Count:N0} could not be removed.";
        return failures.Count == candidates.Count && candidates.Count > 0
            ? CommandHandlerOutcome.Failed(Code("failed"), message, Data(new { removed, failures }))
            : Success("applied", message, new { removed, failures });
    }

    private IReadOnlyList<string> FindFileCandidates(CancellationToken cancellationToken)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string temp = Path.GetTempPath();
        string downloads = Path.Combine(profile, "Downloads");
        var specs = CommandId.ToLowerInvariant() switch
        {
            "cleaner.ai_models_huggingface" => new[] { AllFiles(Path.Combine(profile, ".cache", "huggingface", "hub")) },
            "cleaner.ai_models_ollama" => new[] { AllFiles(Path.Combine(profile, ".ollama", "models")) },
            "cleaner.ai_models_pytorch" => new[] { AllFiles(Path.Combine(profile, ".cache", "torch", "hub", "checkpoints")) },
            "cleaner.ai_models_lmstudio" => new[]
            {
                AllFiles(Path.Combine(roaming, "LM Studio", "Cache")), AllFiles(Path.Combine(roaming, "LM Studio", "Code Cache")),
                AllFiles(Path.Combine(roaming, "CherryStudio", "Cache")), AllFiles(Path.Combine(roaming, "Jan", "Cache")),
            },
            "cleaner.adb_wsa_cache" => FindWsaCacheSpecs(local),
            "cleaner.adb_device_apk_residues" => new[]
            {
                new FileSpec(Path.Combine(profile, ".android"), new[] { "*.apk", "*.log", "*.dmp" }, true, null),
                new FileSpec(temp, new[] { "*.apk", "adb*.log", "emulator*.dmp" }, false, DateTime.UtcNow.AddDays(-7)),
            },
            "cleaner.redis_dump_aof" => CandidateProjectRoots().Select(root =>
                new FileSpec(root, new[] { "dump.rdb", "appendonly.aof", "*.rdb", "*.aof" }, false, DateTime.UtcNow.AddDays(-7))).ToArray(),
            "cleaner.redis_temp_logs" => new[]
            {
                new FileSpec(Path.Combine(local, "Redis"), new[] { "*.log", "*.tmp" }, true, DateTime.UtcNow.AddDays(-7)),
                new FileSpec(temp, new[] { "redis*.log", "redis*.tmp" }, false, DateTime.UtcNow.AddDays(-7)),
            },
            "cleaner.downloads_incomplete_streams" => new[] { new FileSpec(downloads, new[] { "*.crdownload", "*.part", "*.aria2", "*.dap" }, false, DateTime.UtcNow.AddDays(-7)) },
            "cleaner.downloads_torrent_debris" => new[] { new FileSpec(downloads, new[] { "*.torrent.part", "*.fastresume" }, false, DateTime.UtcNow.AddDays(-7)) },
            "cleaner.downloads_multipart_chunks" => new[] { new FileSpec(downloads, new[] { "*.abdm.part", "*.gopeed", "*.qdm", "*.dlman.part", "*.cpdm.part", "*.chunk", "*.motrix" }, false, DateTime.UtcNow.AddDays(-7)) },
            _ => Array.Empty<FileSpec>(),
        };

        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FileSpec spec in specs)
        {
            if (!Directory.Exists(spec.Root)) continue;
            string root = Path.GetFullPath(spec.Root);
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) continue;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = spec.Recursive,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
            };
            foreach (string pattern in spec.Patterns)
            {
                foreach (string path in Directory.EnumerateFiles(root, pattern, options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (spec.OlderThanUtc is DateTime cutoff && File.GetLastWriteTimeUtc(path) >= cutoff) continue;
                    results.Add(Path.GetFullPath(path));
                    if (results.Count >= MaximumCandidates) return results.ToArray();
                }
            }
        }
        return results.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private CommandHandlerOutcome ExecuteGpu(bool apply)
    {
        IReadOnlyList<GpuRegistryTarget> targets = FindGpuTargets();
        if (!apply)
            return Success("preview", $"Found {targets.Count:N0} matching display-driver setting(s).", new { targets });

        int changed = 0;
        var failures = new List<object>();
        foreach (GpuRegistryTarget target in targets)
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(target.SubKey, writable: true);
                if (key is null) throw new IOException("Registry key is unavailable.");
                key.SetValue(target.ValueName, target.Value, RegistryValueKind.DWord);
                changed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                failures.Add(new { target.SubKey, target.ValueName, error = ex.GetType().Name });
            }
        }
        string message = $"Applied {changed:N0} display-driver setting(s); {failures.Count:N0} failed.";
        return failures.Count > 0 && changed == 0
            ? CommandHandlerOutcome.Failed(Code("failed"), message, Data(new { changed, failures }))
            : Success("applied", message, new { changed, failures });
    }

    private IReadOnlyList<GpuRegistryTarget> FindGpuTargets()
    {
        const string displayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        using RegistryKey? root = Registry.LocalMachine.OpenSubKey(displayClass);
        if (root is null) return [];
        var targets = new List<GpuRegistryTarget>();
        foreach (string childName in root.GetSubKeyNames().Take(128))
        {
            using RegistryKey? child = root.OpenSubKey(childName);
            if (child is null) continue;
            string identity = $"{child.GetValue("DriverDesc")} {child.GetValue("ProviderName")}";
            (string vendor, string valueName, int value)? desired = CommandId.ToLowerInvariant() switch
            {
                "gpu.amd_ulps" => ("amd", "EnableUlps", 0),
                "gpu.nvidia_pstate" => ("nvidia", "DisableDynamicPstate", 1),
                "gpu.intel_async_flip" => ("intel", "DisableAsyncFlip", 1),
                _ => null,
            };
            if (desired is { } setting && identity.Contains(setting.vendor, StringComparison.OrdinalIgnoreCase))
                targets.Add(new GpuRegistryTarget($"{displayClass}\\{childName}", setting.valueName, setting.value));
        }
        return targets;
    }

    private CommandHandlerOutcome ExecutePrivacy(bool apply)
    {
        string[] resources = CommandId.ToLowerInvariant() switch
        {
            "privacy.media_player" => [@"HKCU\Software\Microsoft\MediaPlayer\Player\RecentFileList", @"HKCU\Software\Microsoft\Zune\Shell\MRU"],
            "privacy.vlc_history" => [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vlc", "vlc-qt-interface.ini")],
            "privacy.office_mru" => [@"HKCU\Software\Microsoft\Office\*\*\File MRU", @"HKCU\Software\Microsoft\Office\*\*\Place MRU"],
            _ => [],
        };
        if (!apply) return Success("preview", $"Reviewed {resources.Length:N0} scoped history resource(s).", new { resources });

        int changed = CommandId.ToLowerInvariant() switch
        {
            "privacy.media_player" => ClearRegistryValues([@"Software\Microsoft\MediaPlayer\Player\RecentFileList", @"Software\Microsoft\Zune\Shell\MRU"]),
            "privacy.vlc_history" => ClearVlcHistory(),
            "privacy.office_mru" => ClearOfficeMru(),
            _ => 0,
        };
        return Success("applied", $"Cleared {changed:N0} scoped history value(s).", new { changed, resources });
    }

    private async Task<CommandHandlerOutcome> ExecuteEventLogAsync(bool apply, CancellationToken cancellationToken)
    {
        string channel = CommandId.ToLowerInvariant() switch
        {
            "eventlog.diagnostic" => "Microsoft-Windows-Diagnosis-PLA/Operational",
            "eventlog.powershell" => "Microsoft-Windows-PowerShell/Operational",
            "eventlog.bits" => "Microsoft-Windows-Bits-Client/Operational",
            _ => throw new InvalidOperationException("Unsupported event log tool."),
        };
        if (!apply) return Success("preview", $"Event log channel '{channel}' is ready for explicit clearing.", new { channel });
        ProcessExecutionResult result = await new BoundedProcessRunner().RunAsync("wevtutil.exe", ["clear-log", channel], cancellationToken, TimeSpan.FromSeconds(20)).ConfigureAwait(false);
        return result.ExitCode == 0
            ? Success("applied", $"Cleared event log channel '{channel}'.", new { channel })
            : CommandHandlerOutcome.Failed(Code("failed"), $"Event log channel '{channel}' could not be cleared.", Data(new { channel, result.ExitCode }));
    }

    private static int ClearRegistryValues(IEnumerable<string> subKeys)
    {
        int changed = 0;
        foreach (string subKey in subKeys)
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
            if (key is null) continue;
            foreach (string name in key.GetValueNames()) { key.DeleteValue(name, throwOnMissingValue: false); changed++; }
        }
        return changed;
    }

    private static int ClearOfficeMru()
    {
        using RegistryKey? office = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Office", writable: true);
        if (office is null) return 0;
        int changed = 0;
        foreach (string version in office.GetSubKeyNames().Take(32))
        using (RegistryKey? versionKey = office.OpenSubKey(version, writable: true))
        {
            if (versionKey is null) continue;
            foreach (string app in versionKey.GetSubKeyNames().Take(64))
            using (RegistryKey? appKey = versionKey.OpenSubKey(app, writable: true))
            {
                if (appKey is null) continue;
                foreach (string mruName in new[] { "File MRU", "Place MRU" })
                using (RegistryKey? mru = appKey.OpenSubKey(mruName, writable: true))
                {
                    if (mru is null) continue;
                    foreach (string value in mru.GetValueNames()) { mru.DeleteValue(value, false); changed++; }
                }
            }
        }
        return changed;
    }

    private static int ClearVlcHistory()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vlc", "vlc-qt-interface.ini");
        if (!File.Exists(path)) return 0;
        string[] lines = File.ReadAllLines(path);
        int changed = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("list=", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("times=", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = trimmed.StartsWith("list=", StringComparison.OrdinalIgnoreCase) ? "list=" : "times=";
                changed++;
            }
        }
        if (changed > 0) File.WriteAllLines(path, lines);
        return changed;
    }

    private static FileSpec AllFiles(string root) => new(root, ["*"], true, null);

    private static FileSpec[] FindWsaCacheSpecs(string localAppData)
    {
        string packages = Path.Combine(localAppData, "Packages");
        if (!Directory.Exists(packages)) return [];
        return Directory.EnumerateDirectories(packages, "*WindowsSubsystemForAndroid*", SearchOption.TopDirectoryOnly)
            .Take(16).Select(path => AllFiles(Path.Combine(path, "LocalCache"))).ToArray();
    }

    private static IEnumerable<string> CandidateProjectRoots()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[] { Path.Combine(profile, "source", "repos"), Path.Combine(profile, "repos"), Path.Combine(profile, "projects"), Path.Combine(profile, "dev"), Path.Combine(profile, "code"), @"C:\code", @"C:\projects", @"C:\dev" };
    }

    private static void RemoveEmptyCandidateDirectories(IEnumerable<string> files)
    {
        foreach (string directory in files.Select(Path.GetDirectoryName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(path => path.Length))
        {
            try { if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private CommandHandlerOutcome Success(string suffix, string message, object data) => CommandHandlerOutcome.Succeeded(Code(suffix), message, Data(data));
    private string Code(string suffix) => $"{CommandId}.{suffix}";
    private static JsonElement Data(object value) => JsonSerializer.SerializeToElement(value);

    private sealed record FileSpec(string Root, string[] Patterns, bool Recursive, DateTime? OlderThanUtc);
    private sealed record GpuRegistryTarget(string SubKey, string ValueName, int Value);
}
