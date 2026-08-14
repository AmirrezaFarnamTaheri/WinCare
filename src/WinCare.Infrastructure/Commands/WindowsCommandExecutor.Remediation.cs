using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;
using WinCare.Application.Commands;

namespace WinCare.Infrastructure.Commands;

internal sealed partial class WindowsCommandExecutor
{
    private async Task<CommandHandlerOutcome> ApplyPresetAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string presetId = p.RequiredString("PresetId");
        PresetDefinition? preset = WinCare.CommandCatalog.RemediationCatalog.LoadPresets().FirstOrDefault(x => x.Id.Equals(presetId, StringComparison.OrdinalIgnoreCase));
        if (preset is null) throw new CommandParameterException("PresetId", $"Preset '{presetId}' does not exist.");
        IReadOnlyDictionary<string, RemediationRule> rules = WinCare.CommandCatalog.RemediationCatalog.LoadRules().ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

        string executionId = Guid.NewGuid().ToString("N");
        var results = new List<object>();
        JsonElement intentRecord = Data(new { id = executionId, presetId = preset.Id, preset.Title, status = "Applying", startedAt = DateTimeOffset.UtcNow, rules = results });
        await AppendStateItemAsync("preset-history", intentRecord, CancellationToken.None).ConfigureAwait(false);
        if (OnIntentPersistedAsync is not null)
        {
            await OnIntentPersistedAsync("preset-history").ConfigureAwait(false);
        }

        string currentRuleId = "";
        try
        {
            foreach (string ruleId in preset.RuleIds)
            {
                currentRuleId = ruleId;
                cancellationToken.ThrowIfCancellationRequested();
                if (!rules.TryGetValue(ruleId, out RemediationRule? rule))
                {
                    JsonElement partialPreset = Data(new { id = executionId, presetId = preset.Id, preset.Title, status = "PartiallyApplied", stoppedAtRule = ruleId, error = $"Missing rule '{ruleId}'", rules = results });
                    await TransitionStateItemAsync("preset-history", executionId, partialPreset, cancellationToken).ConfigureAwait(false);
                    throw new InvalidDataException($"Preset '{preset.Id}' references missing rule '{ruleId}'.");
                }

                CommandHandlerOutcome outcome = await ApplyRemediationRuleAsync(rule, cancellationToken).ConfigureAwait(false);
                results.Add(new { ruleId, status = outcome.Status.ToString(), outcome.Code, outcome.Message, outcome.Data });
                if (outcome.Status != CommandResultStatus.Succeeded)
                {
                    JsonElement partialPreset = Data(new { id = executionId, presetId = preset.Id, preset.Title, status = "PartiallyApplied", stoppedAtRule = ruleId, message = outcome.Message, rules = results });
                    await TransitionStateItemAsync("preset-history", executionId, partialPreset, cancellationToken).ConfigureAwait(false);
                    return CommandHandlerOutcome.Failed("preset.partial_failure", $"Preset '{preset.Title}' stopped at rule '{ruleId}': {outcome.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            using CancellationTokenSource cleanupCts = new(TimeSpan.FromSeconds(5));
            string terminalStatus = ex is OperationCanceledException ? "Cancelled" : "PartiallyApplied";
            JsonElement partialPreset = Data(new { id = executionId, presetId = preset.Id, preset.Title, status = terminalStatus, stoppedAtRule = currentRuleId, error = ex.Message, rules = results });
            await TransitionStateItemAsync("preset-history", executionId, partialPreset, cleanupCts.Token).ConfigureAwait(false);
            throw;
        }
        JsonElement finalPreset = Data(new { id = executionId, presetId = preset.Id, preset.Title, status = "Applied", completedAt = DateTimeOffset.UtcNow, rules = results });
        await TransitionStateItemAsync("preset-history", executionId, finalPreset, cancellationToken).ConfigureAwait(false);
        return Success("preset", $"Preset '{preset.Title}' applied through native remediation primitives.", finalPreset, undo: false);
    }

    private async Task<CommandHandlerOutcome> ApplyRemediationRuleAsync(RemediationRule rule, CancellationToken cancellationToken)
    {
        int build = Environment.OSVersion.Version.Build;
        if (build < rule.Compatibility.MinBuild) return Block("preset", $"Rule '{rule.Id}' requires Windows build {rule.Compatibility.MinBuild} or newer.");
        if (rule.RequiresAdmin && !IsAdministrator()) return Block("preset", $"Rule '{rule.Id}' requires administrator access.");

        string executionId = Guid.NewGuid().ToString("N");
        var changes = new List<object>();
        JsonElement intentRecord = Data(new { id = executionId, ruleId = rule.Id, rule.Title, status = "Applying", startedAt = DateTimeOffset.UtcNow, changes });
        await AppendStateItemAsync("remediation-history", intentRecord, cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (RemediationChange change in rule.Changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object detail = await ApplyRemediationChangeAsync(rule.Id, change, cancellationToken).ConfigureAwait(false);
                changes.Add(new { change.Type, detail, appliedAt = DateTimeOffset.UtcNow });
            }
            JsonElement history = Data(new { id = executionId, ruleId = rule.Id, rule.Title, status = "Applied", completedAt = DateTimeOffset.UtcNow, changes });
            await TransitionStateItemAsync("remediation-history", executionId, history, cancellationToken).ConfigureAwait(false);
            return Success("preset", $"Rule '{rule.Title}' applied.", history, undo: rule.Reversible);
        }
        catch (Exception ex)
        {
            using CancellationTokenSource cleanupCts = new(TimeSpan.FromSeconds(5));
            string terminalStatus = ex is OperationCanceledException ? "Cancelled" : "PartiallyApplied";
            JsonElement partialRecord = Data(new { id = executionId, ruleId = rule.Id, rule.Title, status = terminalStatus, failedAt = DateTimeOffset.UtcNow, error = ex.Message, appliedChanges = changes });
            await TransitionStateItemAsync("remediation-history", executionId, partialRecord, cleanupCts.Token).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<object> ApplyRemediationChangeAsync(string ruleId, RemediationChange change, CancellationToken cancellationToken)
    {
        JsonElement p = change.Parameters;
        switch (change.Type)
        {
            case "SetRegistryValue":
            {
                string rawPath = RequiredJsonString(p, "Path");
                string name = RequiredJsonString(p, "Name");
                string valueType = JsonString(p, "ValueType", "String");
                JsonElement value = p.GetProperty("Value");
                (RegistryHive hive, string subKey) = ParseRegistryPath(rawPath);
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using RegistryKey key = baseKey.CreateSubKey(subKey, writable: true);
                object? previous = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                RegistryValueKind? previousKind = previous is null ? null : key.GetValueKind(name);
                object converted = ConvertRegistryValue(value, valueType);
                key.SetValue(name, converted, ParseRegistryKind(valueType));
                return new { path = rawPath, name, previous, previousKind = previousKind?.ToString(), value = converted, valueType };
            }
            case "SetOptionalFeatureState":
            {
                string name = RequiredJsonString(p, "Name");
                string state = RequiredJsonString(p, "State");
                IReadOnlyList<string> args = state.Equals("Enabled", StringComparison.OrdinalIgnoreCase)
                    ? ["/Online", "/English", "/Enable-Feature", "/FeatureName:" + name, "/All", "/NoRestart"]
                    : ["/Online", "/English", "/Disable-Feature", "/FeatureName:" + name, "/NoRestart"];
                ProcessExecutionResult result = await _process.RunAsync("dism.exe", args, cancellationToken, TimeSpan.FromMinutes(5)).ConfigureAwait(false);
                if (result.ExitCode is not (0 or 3010)) throw new InvalidDataException($"DISM failed for '{name}': {FirstLine(result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput)}");
                return new { feature = name, state, result.ExitCode, restartRequired = result.ExitCode == 3010 };
            }
            case "SetPowerScheme":
            {
                string scheme = RequiredJsonString(p, "Scheme");
                if (!Guid.TryParse(scheme, out _)) throw new InvalidDataException($"Remediation rule '{ruleId}' contains invalid power scheme GUID.");
                ProcessExecutionResult result = await _process.RunAsync("powercfg.exe", ["/setactive", scheme], cancellationToken).ConfigureAwait(false);
                if (result.ExitCode != 0) throw new InvalidDataException($"powercfg failed: {FirstLine(result.StandardError)}");
                return new { scheme };
            }
            case "SetFirewallProfileState":
            {
                bool enabled = JsonBoolean(p, "Enabled");
                string[] profiles = p.GetProperty("Profiles").EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToArray();
                foreach (string profile in profiles)
                {
                    string normalized = profile.Equals("Domain", StringComparison.OrdinalIgnoreCase) ? "domainprofile" : profile.Equals("Private", StringComparison.OrdinalIgnoreCase) ? "privateprofile" : profile.Equals("Public", StringComparison.OrdinalIgnoreCase) ? "publicprofile" : throw new InvalidDataException($"Unknown firewall profile '{profile}'.");
                    ProcessExecutionResult result = await _process.RunAsync("netsh.exe", ["advfirewall", "set", normalized, "state", enabled ? "on" : "off"], cancellationToken).ConfigureAwait(false);
                    if (result.ExitCode != 0) throw new InvalidDataException($"netsh failed for firewall profile '{profile}': {FirstLine(result.StandardError)}");
                }
                return new { profiles, enabled };
            }
            case "SetLocalUserState":
            {
                string name = RequiredJsonString(p, "Name");
                bool enabled = JsonBoolean(p, "Enabled");
                ProcessExecutionResult result = await _process.RunAsync("net.exe", ["user", name, enabled ? "/active:yes" : "/active:no"], cancellationToken).ConfigureAwait(false);
                if (result.ExitCode != 0) throw new InvalidDataException($"net user failed for '{name}': {FirstLine(result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput)}");
                return new { name, enabled };
            }
            case "SetServiceStartMode":
            {
                string name = RequiredJsonString(p, "Name");
                string mode = RequiredJsonString(p, "StartMode").ToLowerInvariant() switch { "disabled" => "disabled", "automatic" or "auto" => "auto", "manual" or "demand" => "demand", _ => throw new InvalidDataException("Unsupported service start mode.") };
                ProcessExecutionResult result = await _process.RunAsync("sc.exe", ["config", name, "start=", mode], cancellationToken).ConfigureAwait(false);
                if (result.ExitCode != 0) throw new InvalidDataException($"sc.exe failed for '{name}': {FirstLine(result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput)}");
                return new { name, startMode = mode };
            }
            case "SetDefenderPreference":
                return ApplyDefenderPolicyPreference(p);
            default:
                throw new InvalidDataException($"Remediation rule '{ruleId}' uses unsupported native change type '{change.Type}'.");
        }
    }

    private static object ApplyDefenderPolicyPreference(JsonElement p)
    {
        string name = RequiredJsonString(p, "Name");
        JsonElement value = p.GetProperty("Value");
        (string subKey, string valueName) = name switch
        {
            "PUAProtection" => (@"SOFTWARE\Policies\Microsoft\Windows Defender", "PUAProtection"),
            "MAPSReporting" => (@"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet", "SpynetReporting"),
            "SubmitSamplesConsent" => (@"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet", "SubmitSamplesConsent"),
            "DisableBlockAtFirstSeen" => (@"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet", "DisableBlockAtFirstSeen"),
            "EnableNetworkProtection" => (@"SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\Network Protection", "EnableNetworkProtection"),
            "EnableControlledFolderAccess" => (@"SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access", "EnableControlledFolderAccess"),
            "CloudBlockLevel" => (@"SOFTWARE\Policies\Microsoft\Windows Defender\MpEngine", "MpCloudBlockLevel"),
            "SignatureUpdateInterval" => (@"SOFTWARE\Policies\Microsoft\Windows Defender\Signature Updates", "SignatureUpdateInterval"),
            "DisableArchiveScanning" => (@"SOFTWARE\Policies\Microsoft\Windows Defender\Scan", "DisableArchiveScanning"),
            _ => throw new InvalidDataException($"Defender preference '{name}' is not admitted by the native policy mapper."),
        };
        int dword = value.ValueKind is JsonValueKind.True or JsonValueKind.False ? (value.GetBoolean() ? 1 : 0) : value.GetInt32();
        using RegistryKey key = Registry.LocalMachine.CreateSubKey(subKey, writable: true);
        object? previous = key.GetValue(valueName);
        key.SetValue(valueName, dword, RegistryValueKind.DWord);
        return new { name, policyPath = "HKLM\\" + subKey, valueName, previous, value = dword };
    }

    private async Task<CommandHandlerOutcome> RunAutomationAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        JsonElement steps = p.Element("Steps");
        if (steps.ValueKind != JsonValueKind.Array) throw new CommandParameterException("Steps", "Steps must be an array of command objects.");
        return await ExecuteCommandPlanAsync("run-automation", steps, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandHandlerOutcome> RunPlaybookAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        JsonElement steps;
        if (p.OptionalElement("Steps") is JsonElement inline)
        {
            steps = inline;
        }
        else
        {
            string id = p.RequiredString("Id");
            JsonElement record = await FindStateItemAsync("playbooks", id, cancellationToken).ConfigureAwait(false);
            if (!record.TryGetProperty("steps", out steps)) throw new CommandParameterException("Id", $"Playbook '{id}' does not contain steps.");
        }
        if (steps.ValueKind != JsonValueKind.Array) throw new CommandParameterException("Steps", "Playbook steps must be an array.");
        return await ExecuteCommandPlanAsync("playbook", steps, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandHandlerOutcome> ExecuteCommandPlanAsync(string commandId, JsonElement steps, CancellationToken cancellationToken)
    {
        WindowsCommandExecutor.ValidateCommandPlanSteps(steps);
        var results = new List<object>();
        foreach (JsonElement step in steps.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string id = step.GetProperty("commandId").GetString()!;
            CommandDefinition definition = WinCare.CommandCatalog.CommandCatalog.Find(id)!;
            JsonElement parameters = step.TryGetProperty("parameters", out JsonElement parameterElement) && parameterElement.ValueKind == JsonValueKind.Object ? parameterElement.Clone() : Data(new { });
            CommandRequest nested = definition.ReadOnly ? CommandRequest.Preview(id, parameters) : CommandRequest.Execute(id, parameters);
            CommandHandlerOutcome outcome = await ExecuteAsync(definition, nested, cancellationToken).ConfigureAwait(false);
            results.Add(new { commandId = id, status = outcome.Status.ToString(), outcome.Code, outcome.Message, outcome.Data });
            if (outcome.Status != CommandResultStatus.Succeeded)
            {
                return CommandHandlerOutcome.Failed(commandId + ".step_failed", $"Command plan stopped at '{id}': {outcome.Message}", Data(results));
            }
        }
        return Success(commandId, "Command plan completed through the native command dispatcher.", results);
    }

    private CommandHandlerOutcome CancelOperation(CommandParameters p)
    {
        string id = p.RequiredString("OperationId");
        if (!_operations.TryGetValue(id, out CancellationTokenSource? source)) return Block("cancel-operation", $"Operation '{id}' is not active.");
        source.Cancel();
        return Success("cancel-operation", "Cancellation requested for active operation.", new { operationId = id });
    }

    private static (RegistryHive Hive, string SubKey) ParseRegistryPath(string raw)
    {
        string path = raw.Replace('/', '\\').Trim();
        int colon = path.IndexOf(':');
        string hive = colon >= 0 ? path[..colon] : path.Split('\\', 2)[0];
        string rest = colon >= 0 ? path[(colon + 1)..].TrimStart('\\') : path[(hive.Length)..].TrimStart('\\');
        RegistryHive registryHive = hive.ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKCR" or "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
            "HKU" or "HKEY_USERS" => RegistryHive.Users,
            _ => throw new InvalidDataException($"Unsupported registry hive '{hive}'."),
        };
        if (rest.Length == 0) throw new InvalidDataException("Registry path must include a subkey.");
        return (registryHive, rest);
    }

    private static RegistryValueKind ParseRegistryKind(string valueType) => valueType.ToLowerInvariant() switch
    {
        "dword" => RegistryValueKind.DWord,
        "qword" => RegistryValueKind.QWord,
        "multistring" => RegistryValueKind.MultiString,
        "expandstring" => RegistryValueKind.ExpandString,
        "binary" => RegistryValueKind.Binary,
        _ => RegistryValueKind.String,
    };

    private static object ConvertRegistryValue(JsonElement value, string valueType) => ParseRegistryKind(valueType) switch
    {
        RegistryValueKind.DWord => value.ValueKind is JsonValueKind.True or JsonValueKind.False ? (value.GetBoolean() ? 1 : 0) : value.GetInt32(),
        RegistryValueKind.QWord => value.GetInt64(),
        RegistryValueKind.MultiString => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray() : new[] { value.ToString() },
        RegistryValueKind.Binary => value.ValueKind == JsonValueKind.String ? Convert.FromBase64String(value.GetString() ?? string.Empty) : throw new InvalidDataException("Binary registry values must be base64 strings."),
        _ => value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString(),
    };

    private static string RequiredJsonString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) throw new InvalidDataException($"Remediation change requires string parameter '{name}'.");
        return value.GetString()!.Trim();
    }

    private static string JsonString(JsonElement element, string name, string fallback) => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static bool JsonBoolean(JsonElement element, string name) => element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw new InvalidDataException($"Remediation change requires boolean parameter '{name}'.");
}
