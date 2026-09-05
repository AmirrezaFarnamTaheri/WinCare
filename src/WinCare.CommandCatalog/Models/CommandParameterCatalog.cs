namespace WinCare.CommandCatalog.Models;

/// <summary>Input kind rendered by native command surfaces.</summary>
public enum CommandParameterKind { Text, Integer, Long, Number, Boolean, StringList, Json, DateTime }

/// <summary>Declarative input contract derived from the native executor parameter boundary.</summary>
public sealed record CommandParameterDefinition(
    string Name,
    CommandParameterKind Kind,
    bool Required = false,
    string? DefaultValue = null,
    string? Minimum = null,
    string? Maximum = null,
    IReadOnlyList<string>? Options = null);

/// <summary>
/// Typed parameter metadata for native commands. The compact data block is generated from the
/// <c>CommandParameters</c> calls used by <c>WindowsCommandExecutor</c>; conditional relationships
/// such as "one of these two fields" remain enforced by the executor itself.
/// </summary>
public static class CommandParameterCatalog
{
    private const string SchemaData = """
appcontainer|PackageName:s;ProcessId:i=0[1,]
appx-launch|AppUserModelId:!s
appx-launch-targets|Query:s
appx-selection|PackageNames:!a
appx-selection-assess|PackageNames:a
archive-inspect|Path:!s;Limit:i=5000[1,100000]
bcd-export|Destination:!s
binary-intelligence|Path:!s
bluetooth|Limit:i=500[1,5000]
bluetooth-events|MaxEvents:i=100[1,1000]
calculator|Expression:!s
cancel-operation|OperationId:!s
cleaner-disk-pressure|OlderThanDays:i=7[0,3650]
cleaner-disk-pressure-schedule|Id:s;Record:j;Name:j;Path:j;Value:j;Data:j
cleaner-relocation|Source:!s;Destination:!s;Move:b=true;Overwrite:b
cleaner-relocation-assess|Source:!s;Destination:!s
cleaner-winapp2|Path:!s
cleaner-winapp2-run|Path:!s
color-add|Color:!s;Id:s;Name:s
color-capture|X:i;Y:i
color-remove|Id:!s
context-menu-set|Scope:s=File;Id:!s;Enabled:b=true;Label:s;Command:!s
deep-clean|ProfileId:s=temp{temp,wincare-cache};OlderThanDays:i=7[0,3650]
display-calibrate|PhysicalIndex:i=0[0,64];Brightness:i=0[0,100];Contrast:i=0[0,100]
download-batch|Ids:!a
download-cancel|Id:!s
download-create|Id:s;FileName:s;Destination:s;DueAt:d;Sha256:s;Url:!s
download-reconcile|Id:!s
download-remove|Id:!s
download-resume|Id:!s
download-start|Id:!s
download-suspend|Id:!s
ebpf-admit|Path:!s
etw-capture|Profile:s=GeneralProfile;DurationSeconds:i=15[1,300];Destination:s
experience-location-set|GeoId:!i[0,]
experience-power-apply|ProfileId:s=balanced{balanced,high-performance,power-saver};Scheme:s
experience-privacy-apply|ProfileId:s=privacy;IncludeTelemetry:b=true
experience-remote-set|Id:s;Record:j;Name:j;Path:j;Value:j;Data:j
experience-sprite-layout|FrameWidth:i=0[1,16384];FrameHeight:i=0[1,16384];SheetWidth:i=0[1,65536];SheetHeight:i=0[1,65536]
experience-visual-asset|Path:!s
experience-visual-manifest|Path:!s;Entries:!j
explorer-quick|Path:!s
explorer-session-restore|Id:!s
explorer-session-save|Id:s;Name:s
file-preview|Path:!s;MaximumBytes:i=262144[1024,1048576]
file-preview-export|OutputPath:s;Path:!s
fleet-inventory|Hosts:a
fleet-policy-drift|Desired:!j
forensics-timeline|MaximumEvents:i=500[1,5000];Since:d
game-integrity|Path:!s;Limit:i=20000[1,100000]
group-policy-import|Path:!s
hardening-apply|ProfileId:s=balanced
hardware-report|Path:s
image-metadata|Path:!s
injection-surface-quarantine|ProcessId:i=0[1,];SurfaceId:s
injection-surfaces|Limit:i=100[1,1000]
internals-processes|Top:i=50[1,500]
knowledge|Topic:s
launcher-open|Target:!s
launcher-search|Query:!s;Limit:i=50[1,500]
legacy-unsafe|Action:!s
maintenance-create|Id:s;Name:!s;StartAt:d;EndAt:d;Description:s;PlaybookId:s;Tags:a;RequiresRestart:b
maintenance-export|Path:s
maintenance-template-create|Id:s;Record:j;Name:j;Path:j;Value:j;Data:j
maintenance-transition|Id:!s;State:!s
memory-anomalies|WorkingSetThresholdBytes:l[1,];Limit:i=100[1,1000]
monitor-control-set|PhysicalIndex:i=0[0,64];Brightness:i=0[0,100];Contrast:i=0[0,100]
network-experiment|Setting:!s;Value:!s
network-measure|TargetHosts:a;SampleCount:i=3[1,20];TimeoutMilliseconds:i=1500[100,10000];Port:i=443[1,65535]
note-remove|Id:!s
note-save|Id:s;Text:!s;Title:s
offline-appx-selection|PackageNames:!a;ImagePath:!s
offline-driver-add|ImagePath:!s;Path:!s
offline-driver-remove|ImagePath:!s;Driver:!s
offline-drivers|ImagePath:s
offline-feature-set|FeatureName:!s;Enabled:b=true;ImagePath:!s
offline-features|ImagePath:s
offline-package-add|ImagePath:!s;Path:!s
offline-packages|ImagePath:s
offline-reduction-apply|ImagePath:!s;DisableFeatures:a;RemovePackages:a
offline-reduction-assess|ImagePath:!s
pagefile-set|Mode:!s=SystemManaged{Automatic,SystemManaged,Custom};Settings:j
peer-container-log-config|Id:s;Record:j;Name:j;Path:j;Value:j;Data:j
peer-log-tail|Path:!s;Lines:i=200[1,5000]
peer-pe|Path:!s
peer-task-save|Id:s;Record:j;Name:j;Path:j;Value:j;Data:j
playbook|Steps:j;Id:s
power-start|Name:s=Power session
power-stop|Id:!s
preset|PresetId:!s
process-modules|ProcessId:i=0[1,];Limit:i=1000[1,5000]
provisioning-plan|Path:!s
remote-consent-create|DurationMinutes:i=30[1,1440];Subject:!s;Scope:s=diagnostics
remote-consent-expire|Id:!s
remote-consent-state|Id:!s;State:!s
remote-thread-events|MaxEvents:i=100[1,1000]
run-automation|Steps:!j
sandbox-config|Path:!s;Networking:b=true;ClipboardRedirection:b=false;MappedFolders:j
security-control-reduce|Control:!s=DefenderRealtime{DefenderRealtime,SmartScreenShell,Uac,WindowsUpdateStack};DurationMinutes:i=15[5,1440];Reason:!s;EnvironmentClass:s=Workstation{Workstation,Test,DisposableLab}
security-control-restore|RecordId:!s
steam-backup|Source:!s;Destination:!s;Overwrite:b
steam-cloud-files|UserId:s;AppId:s
steam-restore|Source:!s;Destination:!s;Overwrite:b=true
studio-adb-inventory|AdbPath:!s;Sha256:!s
studio-brightness-apply|PhysicalIndex:i=0[0,64];Brightness:i=0[0,100];Contrast:i=0[0,100]
studio-brightness-save|Id:s;Record:j;Name:j;Path:j;Value:j;Data:j
studio-file-workspace-save|Id:s;Record:j;Name:j;Path:j;Value:j;Data:j
studio-folder-appearance|Path:!s;IconPath:s
studio-layout-save|Id:s;Record:j;Name:j;Path:j;Value:j;Data:j
studio-monitoring-export|Path:s
studio-wezterm|Name:s=wincare-status.lua
studio-xbox-fse|ViVeToolPath:!s;Sha256:!s;Enabled:b=true;BasicFeatureSet:b=false
sysmon-configure|Path:!s
system-shortcuts-export|Path:s
telemetry-export|Path:s
telemetry-ingest|Record:!j;Name:!s;Timestamp:d
telemetry-lake-records|Since:d;Until:d;MaximumRecords:i=10000[1,100000];Name:s
telemetry-retention|RetentionDays:i=30[1,3650]
terminal-export|Path:s
toolkit-msi|Path:!s
toolkit-win32-error|Code:i
torrent-metadata|Path:!s
ui-automation-snapshot|Query:s;Limit:i=200[1,1000]
unattend-analyze|Path:!s
vbs-harden|IncludeCredentialGuard:b=true;IncludeHvci:b=true
wdac-deploy|Path:!s
wdac-events|MaxEvents:i=100[1,1000]
widget-catalog|Limit:i=200[1,1000]
widget-export|Path:s
widgets|Limit:i=100[1,500]
window-activate|Handle:l=0[1,]
window-search|Query:s;Limit:i=100[1,1000]
window-topmost|Enabled:b=true;Handle:l=0[1,]
window-zone-set|X:i=0[-32768,32768];Y:i=0[-32768,32768];Width:i=800[100,16384];Height:i=600[100,16384];Handle:l=0[1,]
windows|Limit:i=500[1,5000]
workspace-layout-apply|Id:!s
workspace-layout-remove|Id:!s
workspace-layout-save|Id:s;Name:s
wua-download|UpdateIds:!a
wua-hide|UpdateIds:!a
wua-history|Count:i=100[1,1000]
wua-install|UpdateIds:!a
wua-search|Criteria:s=IsInstalled=0 and IsHidden=0;Limit:i=250[1,1000]
wua-unhide|UpdateIds:!a
wua-uninstall|UpdateIds:!a
""";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<CommandParameterDefinition>> Schemas = BuildSchemas();

    public static IReadOnlyList<CommandParameterDefinition> For(string commandId) =>
        !string.IsNullOrWhiteSpace(commandId) && Schemas.TryGetValue(commandId, out IReadOnlyList<CommandParameterDefinition>? schema)
            ? schema
            : Array.Empty<CommandParameterDefinition>();

    public static bool HasSchema(string commandId) =>
        !string.IsNullOrWhiteSpace(commandId) && Schemas.ContainsKey(commandId);

    public static IReadOnlyCollection<string> ParameterizedCommandIds => Schemas.Keys.ToArray();

    private static IReadOnlyDictionary<string, IReadOnlyList<CommandParameterDefinition>> BuildSchemas()
    {
        var result = new Dictionary<string, IReadOnlyList<CommandParameterDefinition>>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in SchemaData.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = rawLine.IndexOf('|');
            if (separator <= 0 || separator == rawLine.Length - 1)
                throw new InvalidOperationException($"Invalid command parameter schema line: '{rawLine}'.");

            string commandId = rawLine[..separator];
            string fieldsText = rawLine[(separator + 1)..];
            var fields = new List<CommandParameterDefinition>();
            foreach (string fieldText in fieldsText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                fields.Add(ParseField(commandId, fieldText));
            result.Add(commandId, fields);
        }
        return result;
    }

    private static CommandParameterDefinition ParseField(string commandId, string fieldText)
    {
        int separator = fieldText.IndexOf(':');
        if (separator <= 0 || separator == fieldText.Length - 1)
            throw new InvalidOperationException($"Invalid parameter schema field '{fieldText}' for '{commandId}'.");

        string name = fieldText[..separator];
        string spec = fieldText[(separator + 1)..];
        bool required = spec.StartsWith('!');
        if (required) spec = spec[1..];
        if (spec.Length == 0) throw new InvalidOperationException($"Missing parameter kind for '{commandId}.{name}'.");

        CommandParameterKind kind = spec[0] switch
        {
            's' => CommandParameterKind.Text,
            'i' => CommandParameterKind.Integer,
            'l' => CommandParameterKind.Long,
            'n' => CommandParameterKind.Number,
            'b' => CommandParameterKind.Boolean,
            'a' => CommandParameterKind.StringList,
            'j' => CommandParameterKind.Json,
            'd' => CommandParameterKind.DateTime,
            _ => throw new InvalidOperationException($"Unknown parameter kind '{spec[0]}' for '{commandId}.{name}'."),
        };
        spec = spec[1..];

        IReadOnlyList<string>? options = null;
        int optionStart = spec.IndexOf('{');
        if (optionStart >= 0)
        {
            int optionEnd = spec.LastIndexOf('}');
            if (optionEnd <= optionStart) throw new InvalidOperationException($"Invalid options for '{commandId}.{name}'.");
            options = spec[(optionStart + 1)..optionEnd].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            spec = spec.Remove(optionStart, optionEnd - optionStart + 1);
        }

        string? minimum = null;
        string? maximum = null;
        int rangeStart = spec.IndexOf('[');
        if (rangeStart >= 0)
        {
            int rangeEnd = spec.IndexOf(']', rangeStart + 1);
            if (rangeEnd <= rangeStart) throw new InvalidOperationException($"Invalid range for '{commandId}.{name}'.");
            string[] range = spec[(rangeStart + 1)..rangeEnd].Split(',', 2, StringSplitOptions.TrimEntries);
            minimum = range.Length > 0 && range[0].Length > 0 ? range[0] : null;
            maximum = range.Length > 1 && range[1].Length > 0 ? range[1] : null;
            spec = spec.Remove(rangeStart, rangeEnd - rangeStart + 1);
        }

        string? defaultValue = spec.StartsWith('=') ? spec[1..] : null;
        if (spec.Length > 0 && !spec.StartsWith('='))
            throw new InvalidOperationException($"Invalid trailing schema text '{spec}' for '{commandId}.{name}'.");

        return new CommandParameterDefinition(name, kind, required, defaultValue, minimum, maximum, options);
    }
}
