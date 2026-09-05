using System.Collections.ObjectModel;

namespace WinCare.CommandCatalog.Models;

/// <summary>Input kind rendered by native command surfaces.</summary>
public enum CommandParameterKind { Text, Integer, Long, Number, Boolean, StringList, Json, DateTime }

/// <summary>Declarative input contract derived from the native executor parameter boundary.</summary>
public sealed record CommandParameterDefinition(
    string Name, CommandParameterKind Kind, bool Required = false, string? DefaultValue = null,
    string? Minimum = null, string? Maximum = null, IReadOnlyList<string>? Options = null);

public static class CommandParameterCatalog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<CommandParameterDefinition>> Schemas =
        new ReadOnlyDictionary<string, IReadOnlyList<CommandParameterDefinition>>(
            new Dictionary<string, IReadOnlyList<CommandParameterDefinition>>(StringComparer.OrdinalIgnoreCase)
            {
                ["appcontainer"] = [
                    new("PackageName", CommandParameterKind.Text, false, null, null, null, null),
                    new("ProcessId", CommandParameterKind.Integer, false, "0", "1", null, null),
                ],
                ["appx-launch"] = [
                    new("AppUserModelId", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["appx-launch-targets"] = [
                    new("Query", CommandParameterKind.Text, false, null, null, null, null),
                ],
                ["appx-selection"] = [
                    new("PackageNames", CommandParameterKind.StringList, true, null, null, null, null),
                ],
                ["appx-selection-assess"] = [
                    new("PackageNames", CommandParameterKind.StringList, false, null, null, null, null),
                ],
                ["archive-inspect"] = [
                    new("Limit", CommandParameterKind.Integer, false, "5000", "1", "100000", null),
                    new("Path", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["bcd-export"] = [
                    new("Destination", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["binary-intelligence"] = [
                    new("Path", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["bluetooth"] = [
                    new("Limit", CommandParameterKind.Integer, false, "100", "1", "1000", null),
                ],
                ["bluetooth-events"] = [
                    new("MaxEvents", CommandParameterKind.Integer, false, "100", "1", "1000", null),
                ],
                ["calculator"] = [
                    new("Expression", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["cancel-operation"] = [
                    new("OperationId", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["cleaner-disk-pressure"] = [
                    new("OlderThanDays", CommandParameterKind.Integer, false, "7", "0", "3650", null),
                ],
                ["cleaner-relocation"] = [
                    new("Source", CommandParameterKind.Text, true, null, null, null, null),
                    new("Destination", CommandParameterKind.Text, true, null, null, null, null),
                    new("Move", CommandParameterKind.Boolean, false, "false", null, null, null),
                ],
                ["cleaner-relocation-assess"] = [
                    new("Source", CommandParameterKind.Text, true, null, null, null, null),
                    new("Destination", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["cleaner-winapp2"] = [
                    new("Path", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["cleaner-winapp2-run"] = [
                    new("Path", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["color-add"] = [
                    new("Color", CommandParameterKind.Text, true, null, null, null, null),
                    new("Name", CommandParameterKind.Text, false, null, null, null, null),
                ],
                ["color-capture"] = [
                    new("X", CommandParameterKind.Integer, false, "0", null, null, null),
                    new("Y", CommandParameterKind.Integer, false, "0", null, null, null),
                ],
                ["color-remove"] = [
                    new("Id", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["context-menu-set"] = [
                    new("Id", CommandParameterKind.Text, true, null, null, null, null),
                    new("Command", CommandParameterKind.Text, true, null, null, null, null),
                    new("Label", CommandParameterKind.Text, false, null, null, null, null),
                    new("IconPath", CommandParameterKind.Text, false, null, null, null, null),
                ],
                ["deep-clean"] = [
                    new("ProfileId", CommandParameterKind.Text, false, "temp", null, null, ["temp", "wincare-cache"]),
                    new("OlderThanDays", CommandParameterKind.Integer, false, "7", "0", "3650", null),
                ],
                ["display-calibrate"] = [
                    new("Brightness", CommandParameterKind.Integer, false, "0", "0", "100", null),
                    new("Contrast", CommandParameterKind.Integer, false, "0", "0", "100", null),
                    new("PhysicalIndex", CommandParameterKind.Integer, false, "0", "0", "64", null),
                ],
                ["download-batch"] = [
                    new("Ids", CommandParameterKind.StringList, true, null, null, null, null),
                ],
                ["download-cancel"] = [new("Id", CommandParameterKind.Text, true, null, null, null, null)],
                ["download-create"] = [
                    new("Id", CommandParameterKind.Text, false, null, null, null, null),
                    new("FileName", CommandParameterKind.Text, false, null, null, null, null),
                    new("Destination", CommandParameterKind.Text, false, null, null, null, null),
                    new("DueAt", CommandParameterKind.DateTime, false, null, null, null, null),
                    new("Sha256", CommandParameterKind.Text, false, null, null, null, null),
                    new("Url", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["download-reconcile"] = [new("Id", CommandParameterKind.Text, true, null, null, null, null)],
                ["download-remove"] = [new("Id", CommandParameterKind.Text, true, null, null, null, null)],
                ["download-resume"] = [new("Id", CommandParameterKind.Text, true, null, null, null, null)],
                ["download-start"] = [new("Id", CommandParameterKind.Text, true, null, null, null, null)],
                ["download-suspend"] = [new("Id", CommandParameterKind.Text, true, null, null, null, null)],
                ["ebpf-admit"] = [new("Path", CommandParameterKind.Text, true, null, null, null, null)],
                ["etw-capture"] = [new("DurationSeconds", CommandParameterKind.Integer, false, "15", "1", "300", null)],
                ["experience-location-set"] = [new("GeoId", CommandParameterKind.Integer, true, null, "0", null, null)],
                ["experience-power-apply"] = [
                    new("ProfileId", CommandParameterKind.Text, false, "balanced", null, null, ["balanced", "high-performance", "power-saver"]),
                    new("Scheme", CommandParameterKind.Text, false, null, null, null, null),
                ],
                ["experience-privacy-apply"] = [new("IncludeTelemetry", CommandParameterKind.Boolean, false, "true", null, null, null)],
                ["experience-sprite-layout"] = [
                    new("FrameWidth", CommandParameterKind.Integer, false, "0", "1", "16384", null),
                    new("FrameHeight", CommandParameterKind.Integer, false, "0", "1", "16384", null),
                    new("SheetWidth", CommandParameterKind.Integer, false, "0", "1", "65536", null),
                    new("SheetHeight", CommandParameterKind.Integer, false, "0", "1", "65536", null),
                ],
                ["experience-visual-asset"] = [new("Path", CommandParameterKind.Text, true, null, null, null, null)],
                ["experience-visual-manifest"] = [
                    new("Path", CommandParameterKind.Text, true, null, null, null, null),
                    new("Entries", CommandParameterKind.Json, true, null, null, null, null),
                ],
                ["explorer-quick"] = [new("Path", CommandParameterKind.Text, true, null, null, null, null)],
                ["explorer-session-restore"] = [new("Id", CommandParameterKind.Text, true, null, null, null, null)],
                ["file-preview"] = [
                    new("Path", CommandParameterKind.Text, true, null, null, null, null),
                    new("MaximumBytes", CommandParameterKind.Integer, false, "262144", "1024", "1048576", null),
                ],
                ["file-preview-export"] = [new("Path", CommandParameterKind.Text, true, null, null, null, null)],
                ["game-integrity"] = [
                    new("Path", CommandParameterKind.Text, true, null, null, null, null),
                    new("Sha256", CommandParameterKind.Text, false, null, null, null, null),
                ],
                ["group-policy-import"] = [new("Path", CommandParameterKind.Text, true, null, null, null, null)],
                ["image-metadata"] = [new("Path", CommandParameterKind.Text, true, null, null, null, null)],
                ["injection-surface-quarantine"] = [
                    new("ProcessId", CommandParameterKind.Integer, false, null, "1", null, null),
                    new("SurfaceId", CommandParameterKind.Text, false, null, null, null, null),
                ],
                ["internals-processes"] = [new("Limit", CommandParameterKind.Integer, false, "200", "1", "5000", null)],
                ["knowledge"] = [
                    new("Topic", CommandParameterKind.Text, false, null, null, null, null),
                    new("Query", CommandParameterKind.Text, false, null, null, null, null),
                ],
                ["launcher-open"] = [new("Target", CommandParameterKind.Text, true, null, null, null, null)],
                ["launcher-search"] = [
                    new("Query", CommandParameterKind.Text, false, null, null, null, null),
                    new("Limit", CommandParameterKind.Integer, false, "50", "1", "500", null),
                ],
                ["legacy-unsafe"] = [new("Action", CommandParameterKind.Text, true, null, null, null, null)],
                ["maintenance-create"] = [
                    new("Name", CommandParameterKind.Text, true, null, null, null, null),
                    new("StartAt", CommandParameterKind.DateTime, false, null, null, null, null),
                    new("EndAt", CommandParameterKind.DateTime, false, null, null, null, null),
                ],
                ["maintenance-transition"] = [
                    new("Id", CommandParameterKind.Text, true, null, null, null, null),
                    new("State", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["monitor-control-set"] = [
                    new("Brightness", CommandParameterKind.Integer, false, null, "0", "100", null),
                    new("Contrast", CommandParameterKind.Integer, false, null, "0", "100", null),
                    new("PhysicalIndex", CommandParameterKind.Integer, false, "0", "0", "64", null),
                ],
                ["network-experiment"] = [
                    new("Setting", CommandParameterKind.Text, true, null, null, null, null),
                    new("Value", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["network-measure"] = [
                    new("TargetHosts", CommandParameterKind.StringList, false, null, null, null, null),
                    new("SampleCount", CommandParameterKind.Integer, false, "3", "1", "20", null),
                    new("TimeoutMilliseconds", CommandParameterKind.Integer, false, "1500", "100", "10000", null),
                    new("Port", CommandParameterKind.Integer, false, "443", "1", "65535", null),
                ],
                ["note-remove"] = [new("Id", CommandParameterKind.Text, true, null, null, null, null)],
                ["note-save"] = [
                    new("Text", CommandParameterKind.Text, true, null, null, null, null),
                    new("Title", CommandParameterKind.Text, false, null, null, null, null),
                    new("Tags", CommandParameterKind.StringList, false, null, null, null, null),
                    new("Id", CommandParameterKind.Text, false, null, null, null, null),
                ],
                ["offline-appx-selection"] = [
                    new("ImagePath", CommandParameterKind.Text, true, null, null, null, null),
                    new("PackageNames", CommandParameterKind.StringList, true, null, null, null, null),
                ],
                ["offline-driver-add"] = [
                    new("ImagePath", CommandParameterKind.Text, true, null, null, null, null),
                    new("Path", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["offline-driver-remove"] = [
                    new("ImagePath", CommandParameterKind.Text, true, null, null, null, null),
                    new("Driver", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["offline-feature-set"] = [
                    new("ImagePath", CommandParameterKind.Text, true, null, null, null, null),
                    new("FeatureName", CommandParameterKind.Text, true, null, null, null, null),
                    new("State", CommandParameterKind.Text, true, null, null, null, ["Enable", "Disable"]),
                ],
                ["offline-package-add"] = [
                    new("ImagePath", CommandParameterKind.Text, true, null, null, null, null),
                    new("Path", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["offline-reduction-apply"] = [
                    new("ImagePath", CommandParameterKind.Text, true, null, null, null, null),
                    new("DisableFeatures", CommandParameterKind.StringList, false, null, null, null, null),
                    new("RemovePackages", CommandParameterKind.StringList, false, null, null, null, null),
                ],
                ["pagefile-set"] = [
                    new("Mode", CommandParameterKind.Text, true, null, null, null, ["Automatic", "SystemManaged", "Custom"]),
                    new("Settings", CommandParameterKind.Json, false, null, null, null, null),
                ],
                ["peer-log-tail"] = [
                    new("Path", CommandParameterKind.Text, true, null, null, null, null),
                    new("Lines", CommandParameterKind.Integer, false, "200", "1", "5000", null),
                ],
                ["peer-pe"] = [new("Path", CommandParameterKind.Text, true, null, null, null, null)],
                ["playbook"] = [new("Id", CommandParameterKind.Text, true, null, null, null, null)],
                ["power-stop"] = [new("Id", CommandParameterKind.Text, true, null, null, null, null)],
                ["preset"] = [new("PresetId", CommandParameterKind.Text, true, null, null, null, null)],
                ["process-modules"] = [
                    new("ProcessId", CommandParameterKind.Integer, false, null, "1", null, null),
                    new("Limit", CommandParameterKind.Integer, false, "1000", "1", "5000", null),
                ],
                ["provisioning-plan"] = [new("Path", CommandParameterKind.Text, true, null, null, null, null)],
                ["remote-consent-create"] = [
                    new("Subject", CommandParameterKind.Text, true, null, null, null, null),
                    new("Until", CommandParameterKind.DateTime, false, null, null, null, null),
                ],
                ["remote-consent-expire"] = [new("Id", CommandParameterKind.Text, true, null, null, null, null)],
                ["remote-consent-state"] = [
                    new("Id", CommandParameterKind.Text, true, null, null, null, null),
                    new("State", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["remote-thread-events"] = [new("MaxEvents", CommandParameterKind.Integer, false, "100", "1", "1000", null)],
                ["run-automation"] = [new("Steps", CommandParameterKind.Json, true, null, null, null, null)],
                ["sandbox-config"] = [new("Path", CommandParameterKind.Text, true, null, null, null, null)],
                ["security-control-reduce"] = [
                    new("Control", CommandParameterKind.Text, true, null, null, null, ["DefenderRealtime", "SmartScreenShell", "Uac", "WindowsUpdateStack"]),
                    new("Reason", CommandParameterKind.Text, true, null, null, null, null),
                    new("DurationMinutes", CommandParameterKind.Integer, false, "15", "5", "1440", null),
                    new("EnvironmentClass", CommandParameterKind.Text, false, "Workstation", null, null, ["Workstation", "Test", "DisposableLab"]),
                ],
                ["security-control-restore"] = [new("RecordId", CommandParameterKind.Text, true, null, null, null, null)],
                ["steam-backup"] = [
                    new("Source", CommandParameterKind.Text, true, null, null, null, null),
                    new("Destination", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["steam-cloud-files"] = [new("UserId", CommandParameterKind.Text, false, null, null, null, null)],
                ["steam-restore"] = [
                    new("Source", CommandParameterKind.Text, true, null, null, null, null),
                    new("Destination", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["studio-adb-inventory"] = [
                    new("AdbPath", CommandParameterKind.Text, true, null, null, null, null),
                    new("Sha256", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["studio-brightness-apply"] = [
                    new("Brightness", CommandParameterKind.Integer, false, null, "0", "100", null),
                    new("Contrast", CommandParameterKind.Integer, false, null, "0", "100", null),
                    new("PhysicalIndex", CommandParameterKind.Integer, false, "0", "0", "64", null),
                ],
                ["studio-folder-appearance"] = [new("Path", CommandParameterKind.Text, true, null, null, null, null)],
                ["studio-xbox-fse"] = [
                    new("ViVeToolPath", CommandParameterKind.Text, true, null, null, null, null),
                    new("Sha256", CommandParameterKind.Text, true, null, null, null, null),
                ],
                ["sysmon-configure"] = [new("Path", CommandParameterKind.Text, true, null, null, null, null)],
                ["telemetry-ingest"] = [
                    new("Name", CommandParameterKind.Text, true, null, null, null, null),
                    new("Record", CommandParameterKind.Json, true, null, null, null, null),
                ],
                ["telemetry-retention"] = [new("RetentionDays", CommandParameterKind.Integer, false, "30", "1", "3650", null)],
                ["toolkit-win32-error"] = [new("Code", CommandParameterKind.Integer, false, "0", null, null, null)],
                ["unattend-analyze"] = [new("Path", CommandParameterKind.Text, false, null, null, null, null)],
                ["wdac-deploy"] = [new("Path", CommandParameterKind.Text, true, null, null, null, null)],
                ["wdac-events"] = [new("MaxEvents", CommandParameterKind.Integer, false, "100", "1", "1000", null)],
                ["window-activate"] = [new("Handle", CommandParameterKind.Long, true, null, "1", null, null)],
                ["window-search"] = [new("Query", CommandParameterKind.Text, false, null, null, null, null)],
                ["window-topmost"] = [
                    new("Handle", CommandParameterKind.Long, true, null, "1", null, null),
                    new("Enabled", CommandParameterKind.Boolean, false, "true", null, null, null),
                ],
                ["window-zone-set"] = [
                    new("Handle", CommandParameterKind.Long, true, null, "1", null, null),
                    new("X", CommandParameterKind.Integer, false, "0", null, null, null),
                    new("Y", CommandParameterKind.Integer, false, "0", null, null, null),
                    new("Width", CommandParameterKind.Integer, false, "0", "1", null, null),
                    new("Height", CommandParameterKind.Integer, false, "0", "1", null, null),
                ],
                ["wua-download"] = [new("UpdateIds", CommandParameterKind.StringList, true, null, null, null, null)],
                ["wua-hide"] = [new("UpdateIds", CommandParameterKind.StringList, true, null, null, null, null)],
                ["wua-history"] = [new("Count", CommandParameterKind.Integer, false, "100", "1", "1000", null)],
                ["wua-install"] = [new("UpdateIds", CommandParameterKind.StringList, true, null, null, null, null)],
                ["wua-search"] = [new("Criteria", CommandParameterKind.Text, false, null, null, null, null)],
                ["wua-unhide"] = [new("UpdateIds", CommandParameterKind.StringList, true, null, null, null, null)],
                ["wua-uninstall"] = [new("UpdateIds", CommandParameterKind.StringList, true, null, null, null, null)],
                ["workspace-layout-apply"] = [new("Id", CommandParameterKind.Text, true, null, null, null, null)],
                ["workspace-layout-remove"] = [new("Id", CommandParameterKind.Text, true, null, null, null, null)],
            });

    public static IReadOnlyList<CommandParameterDefinition> For(string commandId) =>
        !string.IsNullOrWhiteSpace(commandId) && Schemas.TryGetValue(commandId, out var schema)
            ? schema : Array.Empty<CommandParameterDefinition>();
}
