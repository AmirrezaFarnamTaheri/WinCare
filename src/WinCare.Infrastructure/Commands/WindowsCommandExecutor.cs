using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Win32;
using WinCare.Application.Commands;
using WinCare.Application.Native;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Infrastructure.Commands;

/// <summary>
/// Native Windows implementation for the complete stable WinCare command catalog.
/// Uses BCL/Win32 APIs and bounded native child processes only; PowerShell is never invoked.
/// </summary>
public sealed partial class WindowsCommandExecutor : ICommandOperationExecutor, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly INativeCoreService? _nativeCore;
    private readonly BoundedProcessRunner _process;
    private readonly CommandStateStore _state;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _operations = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="WindowsCommandExecutor"/>.
    /// </summary>
    public WindowsCommandExecutor(
        INativeCoreService? nativeCore = null,
        BoundedProcessRunner? process = null,
        CommandStateStore? state = null,
        HttpClient? httpClient = null)
    {
        _nativeCore = nativeCore;
        _process = process ?? new BoundedProcessRunner();
        _state = state ?? new CommandStateStore();
        _httpClient = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            MaxConnectionsPerServer = 4,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    /// <summary>
    /// Executes a command synchronously or asynchronously as a native operation.
    /// </summary>
    public async Task<CommandHandlerOutcome> ExecuteAsync(
        CommandDefinition definition,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var parameters = new CommandParameters(request.Parameters);
            if (!definition.ReadOnly && request.Apply && definition.AdministratorAccess == AdministratorAccess.Required && !IsAdministrator())
            {
                return CommandHandlerOutcome.Blocked(
                    "command.elevation_required",
                    $"'{definition.Title}' requires administrator access. Restart WinCare elevated, then review and apply again.");
            }

            if (definition.Id is "catalog" or "presets")
            {
                return ExecuteEmbeddedCatalog(definition.Id);
            }

            if (!OperatingSystem.IsWindows())
            {
                return CommandHandlerOutcome.Blocked(
                    "platform.windows_required",
                    $"'{definition.Title}' requires Windows. No host state was changed.");
            }

            return definition.ReadOnly
                ? await Task.Run(() => ExecuteReadOnlyAsync(definition, parameters, cancellationToken), cancellationToken).ConfigureAwait(false)
                : await Task.Run(() => ExecuteMutationAsync(definition, request, parameters, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (CommandParameterException ex)
        {
            return CommandHandlerOutcome.Blocked("command.parameters_invalid", ex.Message);
        }
        catch (CommandDependencyException ex)
        {
            return CommandHandlerOutcome.Blocked(
                "command.dependency_unavailable",
                $"{ex.Message} No host state was changed.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return CommandHandlerOutcome.Blocked("command.access_denied", ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            return CommandHandlerOutcome.Failed("command.timeout", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or System.ComponentModel.Win32Exception)
        {
            return CommandHandlerOutcome.Failed(
                "command.native_failure",
                $"{definition.Title} failed safely: {ex.Message}");
        }
    }

    private CommandHandlerOutcome ExecuteEmbeddedCatalog(string commandId)
    {
        if (commandId == "catalog")
        {
            JsonElement data = JsonSerializer.SerializeToElement(WinCare.CommandCatalog.RemediationCatalog.LoadRules(), JsonOptions);
            return CommandHandlerOutcome.Succeeded("catalog.loaded", "Command remediation catalog loaded.", data);
        }

        JsonElement presets = JsonSerializer.SerializeToElement(WinCare.CommandCatalog.RemediationCatalog.LoadPresets(), JsonOptions);
        return CommandHandlerOutcome.Succeeded("presets.loaded", "Preset catalog loaded.", presets);
    }

    private async Task<CommandHandlerOutcome> ExecuteReadOnlyAsync(
        CommandDefinition definition,
        CommandParameters p,
        CancellationToken cancellationToken)
    {
        ValidateCommandParameters(definition, p);
        return definition.Id switch
        {
            "system" => await SystemOverviewAsync(cancellationToken).ConfigureAwait(false),
            "applications" => Applications(),
            "cleanup-targets" => await CleanupTargetsAsync(cancellationToken).ConfigureAwait(false),
            "storage" => StorageOverview(),
            "startup" => StartupItems(),
            "health" => HealthOverview(),
            "security" => await SecurityOverviewAsync(cancellationToken).ConfigureAwait(false),
            "network" => NetworkOverview(),
            "pagefile" => PagefileState(),
            "pagefile-recommendation" => PagefileRecommendation(),
            "security-controls" => await SecurityControlsAsync(cancellationToken).ConfigureAwait(false),
            "security-maintenance" => await ReadStateAsync("security-maintenance", cancellationToken).ConfigureAwait(false),
            "tcp-global" => await NativeToolQueryAsync("tcp-global", "netsh.exe", ["interface", "tcp", "show", "global"], cancellationToken).ConfigureAwait(false),
            "network-measure" => await NetworkMeasureAsync(p, cancellationToken).ConfigureAwait(false),
            "network-experiments" => await ReadStateAsync("network-experiments", cancellationToken).ConfigureAwait(false),
            "injection-surfaces" => ProcessInjectionSurface(p),
            "process-modules" => ProcessModules(p),
            "remote-thread-events" => await EventLogQueryAsync("remote-thread-events", "Security", p.Int32("MaxEvents", 100, 1, 1000), cancellationToken).ConfigureAwait(false),
            "etw-sessions" => await NativeToolQueryAsync("etw-sessions", "logman.exe", ["query", "-ets"], cancellationToken).ConfigureAwait(false),
            "wua-search" => await WindowsUpdateSearchAsync(p, cancellationToken).ConfigureAwait(false),
            "wua-history" => await WindowsUpdateHistoryAsync(p, cancellationToken).ConfigureAwait(false),
            "wdac-policies" => await WdacPoliciesAsync(cancellationToken).ConfigureAwait(false),
            "wdac-events" => await EventLogQueryAsync("wdac-events", "Microsoft-Windows-CodeIntegrity/Operational", p.Int32("MaxEvents", 100, 1, 1000), cancellationToken).ConfigureAwait(false),
            "internals-processes" => InternalsProcesses(p),
            "internals-memory" => InternalsMemory(),
            "internals-cpu" => InternalsCpu(),
            "credential-providers" => CredentialProviders(),
            "appcontainer" => AppContainerState(p),
            "desktop-controls" => DesktopControls(),
            "wifi-profiles" => await NativeToolQueryAsync("wifi-profiles", "netsh.exe", ["wlan", "show", "profiles"], cancellationToken).ConfigureAwait(false),
            "shell-extensions" => ShellExtensions(),
            "desktop-shortcuts" => DesktopShortcuts(),
            "boot" => await NativeToolQueryAsync("boot", "bcdedit.exe", ["/enum", "all"], cancellationToken, administratorOptional: true).ConfigureAwait(false),
            "unattend-analyze" => UnattendAnalyze(p),
            "knowledge" => Knowledge(p),
            "reports" => await SystemReportAsync(cancellationToken).ConfigureAwait(false),
            "automation-profiles" => await ReadStateAsync("automation-profiles", cancellationToken).ConfigureAwait(false),
            "widgets" => await WidgetSnapshotAsync(p, cancellationToken).ConfigureAwait(false),
            "widget-catalog" => WidgetCatalog(p),
            "bluetooth" => BluetoothDevices(p, cancellationToken),
            "bluetooth-events" => await EventLogQueryAsync("bluetooth-events", "System", p.Int32("MaxEvents", 100, 1, 1000), cancellationToken, "BTH").ConfigureAwait(false),
            "maintenance" => await ReadStateAsync("maintenance-windows", cancellationToken).ConfigureAwait(false),
            "maintenance-metrics" => await MaintenanceMetricsAsync(cancellationToken).ConfigureAwait(false),
            "context-menu" => ContextMenuEntries(),
            "customization-hosts" => CustomizationHosts(),
            "rainmeter-skins" => RainmeterSkins(),
            "windhawk-mods" => WindhawkMods(),
            "explorerpatcher" => ExplorerPatcherState(),
            "modern-context-validate" => ModernContextValidation(),
            "playbooks" => await ReadStateAsync("playbooks", cancellationToken).ConfigureAwait(false),
            "group-policy" => await NativeToolQueryAsync("group-policy", "gpresult.exe", ["/Scope", "Computer", "/R"], cancellationToken, administratorOptional: true).ConfigureAwait(false),
            "sysmon" => await SysmonStateAsync(cancellationToken).ConfigureAwait(false),
            "offline-images" => await NativeToolQueryAsync("offline-images", "dism.exe", ["/English", "/Get-MountedImageInfo"], cancellationToken, administratorOptional: true).ConfigureAwait(false),
            "offline-drivers" => await DismOfflineQueryAsync("offline-drivers", p, "/Get-Drivers", cancellationToken).ConfigureAwait(false),
            "offline-packages" => await DismOfflineQueryAsync("offline-packages", p, "/Get-Packages", cancellationToken).ConfigureAwait(false),
            "offline-features" => await DismOfflineQueryAsync("offline-features", p, "/Get-Features", cancellationToken).ConfigureAwait(false),
            "power-sessions" => await ReadStateAsync("power-sessions", cancellationToken).ConfigureAwait(false),
            "windows" => WindowInventory(p),
            "monitors" => MonitorInventory(),
            "window-zones" => await ReadStateAsync("window-zones", cancellationToken).ConfigureAwait(false),
            "downloads" => await ReadStateAsync("downloads", cancellationToken).ConfigureAwait(false),
            "downloads-due" => await DownloadsDueAsync(cancellationToken).ConfigureAwait(false),
            "telemetry-snapshot" => TelemetrySnapshot(),
            "telemetry-history" => await ReadStateAsync("telemetry-history", cancellationToken).ConfigureAwait(false),
            "launcher-search" => LauncherSearch(p),
            "calculator" => Calculator(p),
            "steam-games" => SteamGames(),
            "steam-users" => SteamUsers(),
            "steam-cloud-files" => SteamCloudFiles(p),
            "game-integrity" => await GameIntegrityAsync(p, cancellationToken).ConfigureAwait(false),
            "offline-reduction-profiles" => StaticProfileCatalog("offline-reduction"),
            "offline-reduction-assess" => await OfflineReductionAssessAsync(p, cancellationToken).ConfigureAwait(false),
            "workspace-layouts" => await ReadStateAsync("workspace-layouts", cancellationToken).ConfigureAwait(false),
            "color-capture" => ColorCapture(p),
            "color-palette" => await ReadStateAsync("color-palette", cancellationToken).ConfigureAwait(false),
            "image-metadata" => ImageMetadata(p),
            "notes" => await ReadStateAsync("notes", cancellationToken).ConfigureAwait(false),
            "browsers" => BrowserInventory(),
            "browser-extensions" => BrowserExtensions(),
            "remote-support" => await RemoteSupportStateAsync(cancellationToken).ConfigureAwait(false),
            "remote-consent" => await ReadStateAsync("remote-consents", cancellationToken).ConfigureAwait(false),
            "studio-snapshot" => await StudioSnapshotAsync(cancellationToken).ConfigureAwait(false),
            "studio-file-workspaces" => await ReadStateAsync("studio-file-workspaces", cancellationToken).ConfigureAwait(false),
            "studio-layout-profiles" => await ReadStateAsync("studio-layout-profiles", cancellationToken).ConfigureAwait(false),
            "studio-brightness-schedules" => await ReadStateAsync("studio-brightness-schedules", cancellationToken).ConfigureAwait(false),
            "studio-kanata-validate" => StudioToolValidation("kanata", ["kanata.exe"]),
            "studio-syncthing" => StudioToolValidation("syncthing", ["syncthing.exe"]),
            "studio-package" => StudioPackageInventory(),
            "toolkit-diagnostics" => ToolkitDiagnostics(cancellationToken),
            "toolkit-win32-error" => Win32ErrorLookup(p),
            "toolkit-msi" => ToolkitMsi(p),
            "hardening-profiles" => StaticProfileCatalog("hardening"),
            "hardening-assess" => HardeningAssess(cancellationToken),
            "maintenance-templates" => await ReadStateAsync("maintenance-templates", cancellationToken).ConfigureAwait(false),
            "system-shortcuts" => SystemShortcuts(),
            "torrent-metadata" => TorrentMetadata(p),
            "experience-cards" => ExperienceCards(p),
            "experience-visual-asset" => FileDescriptor(p, "Path"),
            "experience-sprite-layout" => SpriteLayout(p),
            "experience-location-state" => LocationState(),
            "experience-remote-profiles" => StaticProfileCatalog("experience-remote"),
            "experience-remote-state" => await ReadStateAsync("experience-remote", cancellationToken).ConfigureAwait(false),
            "experience-power-profiles" => StaticProfileCatalog("experience-power"),
            "experience-power-assess" => await PowerAssessmentAsync(cancellationToken).ConfigureAwait(false),
            "experience-download-strategy" => DownloadStrategy(),
            "experience-privacy-profiles" => StaticProfileCatalog("experience-privacy"),
            "shell-hardware-cards" => ShellHardwareCards(),
            "hardware-inventory" => HardwareInventory(cancellationToken),
            "monitor-controls" => MonitorControls(),
            "window-search" => WindowSearch(p),
            "explorer-session" => ExplorerSession(),
            "explorer-session-records" => await ReadStateAsync("explorer-sessions", cancellationToken).ConfigureAwait(false),
            "ui-automation-snapshot" => UiAutomationSnapshot(p),
            "appx-runtime" => AppxRuntime(),
            "appx-launch-targets" => AppxLaunchTargets(p),
            "archive-inspect" => ArchiveInspect(p),
            "servicing-media" => await NativeToolQueryAsync("servicing-media", "dism.exe", ["/English", "/Get-MountedImageInfo"], cancellationToken, administratorOptional: true).ConfigureAwait(false),
            "terminal-environment" => TerminalEnvironment(),
            "gui-resource-audit" => GuiResourceAudit(),
            "fullscreen-shell-assess" => FullscreenShellAssess(),
            "cleaner-preview-cards" => await CleanerPreviewCardsAsync(cancellationToken).ConfigureAwait(false),
            "cleaner-winapp2" => Winapp2Admission(p),
            "cleaner-relocation-profiles" => StaticProfileCatalog("cleaner-relocation"),
            "cleaner-relocation-assess" => CleanerRelocationAssess(p),
            "file-preview-profiles" => StaticProfileCatalog("file-preview"),
            "file-preview" => FilePreview(p),
            "preview-handlers" => PreviewHandlerAssessment(),
            "peer-display-overrides" => DisplayOverrides(),
            "peer-wlan" => await NativeToolQueryAsync("peer-wlan", "netsh.exe", ["wlan", "show", "networks", "mode=bssid"], cancellationToken).ConfigureAwait(false),
            "peer-log-tail" => PeerLogTail(p),
            "peer-pe" => PeInspection(p),
            "peer-taskboard" => await ReadStateAsync("peer-taskboard", cancellationToken).ConfigureAwait(false),
            "deep-clean-profiles" => StaticProfileCatalog("deep-clean"),
            "appx-selection-profiles" => StaticProfileCatalog("appx-selection"),
            "appx-selection-assess" => AppxSelectionAssess(p),
            "legacy-unsafe-profiles" => StaticProfileCatalog("legacy-unsafe"),
            "capabilities" => CapabilityRegistry(cancellationToken),
            "fleet-inventory" => await FleetInventoryAsync(p, cancellationToken).ConfigureAwait(false),
            "fleet-policy-drift" => FleetPolicyDrift(p, cancellationToken),
            "hypervisor-isolation" => await HypervisorIsolationAsync(cancellationToken).ConfigureAwait(false),
            "forensics-timeline" => ForensicsTimeline(p, cancellationToken),
            "memory-anomalies" => MemoryAnomalies(p),
            "vbs-assurance" => VbsAssurance(),
            "telemetry-lake-records" => await TelemetryLakeRecordsAsync(p, cancellationToken).ConfigureAwait(false),
            "telemetry-lake" => await TelemetryLakeAggregateAsync(p, cancellationToken).ConfigureAwait(false),
            "binary-intelligence" => await BinaryIntelligenceAsync(p, cancellationToken).ConfigureAwait(false),
            "binary-intelligence-capability" => BinaryIntelligenceCapability(),
            "ebpf-capability" => ToolCapability("ebpf", ["netsh.exe"], "eBPF for Windows is exposed through netsh when installed."),
            "ebpf-programs" => await EbpfProgramsAsync(p, cancellationToken).ConfigureAwait(false),
            "display-pipeline" => DisplayPipeline(),
            "virtual-display-capability" => VirtualDisplayCapability(),
            "wireguard-tunnels" => await WireGuardTunnelsAsync(p, cancellationToken).ConfigureAwait(false),
            "quic-capability" => QuicCapability(),
            _ => Block(definition.Id, $"No concrete native inspection route implemented for read-only command '{definition.Id}'."),
        };
    }

    private async Task<CommandHandlerOutcome> ExecuteMutationAsync(
        CommandDefinition definition,
        CommandRequest request,
        CommandParameters p,
        CancellationToken cancellationToken)
    {
        ValidateCommandParameters(definition, p);
        if (!request.Apply)
        {
            return MutationPreview(definition, p);
        }

        return definition.Id switch
        {
            "preset" => await ApplyPresetAsync(p, cancellationToken).ConfigureAwait(false),
            "pagefile-set" => PagefileSet(p, cancellationToken),
            "security-control-reduce" => await SecurityControlReduceAsync(p, cancellationToken).ConfigureAwait(false),
            "security-control-restore" => await SecurityControlRestoreAsync(p, cancellationToken).ConfigureAwait(false),
            "network-experiment" => await NetworkExperimentAsync(p, cancellationToken).ConfigureAwait(false),
            "etw-capture" => await EtwCaptureAsync(p, cancellationToken).ConfigureAwait(false),
            "injection-surface-quarantine" => await InjectionSurfaceQuarantineAsync(p, cancellationToken).ConfigureAwait(false),
            "wua-hide" => await WindowsUpdateSetHiddenAsync(p, hidden: true, cancellationToken).ConfigureAwait(false),
            "wua-unhide" => await WindowsUpdateSetHiddenAsync(p, hidden: false, cancellationToken).ConfigureAwait(false),
            "wua-download" => await WindowsUpdateDownloadAsync(p, cancellationToken).ConfigureAwait(false),
            "wua-install" => await WindowsUpdateInstallAsync(p, cancellationToken).ConfigureAwait(false),
            "wua-uninstall" => await WindowsUpdateUninstallAsync(p, cancellationToken).ConfigureAwait(false),
            "wdac-deploy" => await WdacDeployAsync(p, cancellationToken).ConfigureAwait(false),
            "bcd-export" => await BcdExportAsync(p, cancellationToken).ConfigureAwait(false),
            "provisioning-plan" => await ProvisioningPlanAsync(p, cancellationToken).ConfigureAwait(false),
            "run-automation" => await RunAutomationAsync(p, cancellationToken).ConfigureAwait(false),
            "cancel-operation" => CancelOperation(p),
            "widget-export" => await ExportStateAsync("widgets", p, "wincare-widgets.json", cancellationToken).ConfigureAwait(false),
            "maintenance-create" => await MaintenanceCreateAsync(p, cancellationToken).ConfigureAwait(false),
            "maintenance-transition" => await MaintenanceTransitionAsync(p, cancellationToken).ConfigureAwait(false),
            "maintenance-export" => await ExportStateAsync("maintenance-windows", p, "wincare-maintenance.json", cancellationToken).ConfigureAwait(false),
            "context-menu-set" => ContextMenuSet(p),
            "playbook" => await RunPlaybookAsync(p, cancellationToken).ConfigureAwait(false),
            "group-policy-import" => await GroupPolicyImportAsync(p, cancellationToken).ConfigureAwait(false),
            "sysmon-configure" => await SysmonConfigureAsync(p, cancellationToken).ConfigureAwait(false),
            "sysmon-uninstall" => await SysmonUninstallAsync(p, cancellationToken).ConfigureAwait(false),
            "offline-driver-add" => await DismOfflineMutationAsync("offline-driver-add", p, ["/Add-Driver", "/Driver:" + p.RequiredString("Path")], cancellationToken).ConfigureAwait(false),
            "offline-driver-remove" => await DismOfflineMutationAsync("offline-driver-remove", p, ["/Remove-Driver", "/Driver:" + p.RequiredString("Driver")], cancellationToken).ConfigureAwait(false),
            "offline-package-add" => await DismOfflineMutationAsync("offline-package-add", p, ["/Add-Package", "/PackagePath:" + p.RequiredString("Path")], cancellationToken).ConfigureAwait(false),
            "offline-feature-set" => await DismOfflineFeatureSetAsync(p, cancellationToken).ConfigureAwait(false),
            "power-start" => await PowerSessionStartAsync(p, cancellationToken).ConfigureAwait(false),
            "power-stop" => await PowerSessionStopAsync(p, cancellationToken).ConfigureAwait(false),
            "window-zone-set" => WindowZoneSet(p),
            "window-topmost" => WindowTopmost(p),
            "window-activate" => WindowActivate(p),
            "input-release" => InputRelease(),
            "download-create" => await DownloadCreateAsync(p, cancellationToken).ConfigureAwait(false),
            "download-start" => await DownloadStartAsync(p, cancellationToken).ConfigureAwait(false),
            "download-start-due" => await DownloadStartDueAsync(cancellationToken).ConfigureAwait(false),
            "download-suspend" => await DownloadSuspendAsync(p, cancellationToken).ConfigureAwait(false),
            "download-resume" => await DownloadStartAsync(p, cancellationToken).ConfigureAwait(false),
            "download-reconcile" => await DownloadReconcileAsync(p, cancellationToken).ConfigureAwait(false),
            "download-cancel" => await DownloadCancelAsync(p, cancellationToken).ConfigureAwait(false),
            "download-remove" => await DownloadRemoveAsync(p, cancellationToken).ConfigureAwait(false),
            "telemetry-capture" => await TelemetryCaptureAsync(cancellationToken).ConfigureAwait(false),
            "telemetry-export" => await ExportStateAsync("telemetry-history", p, "wincare-telemetry.json", cancellationToken).ConfigureAwait(false),
            "launcher-open" => LauncherOpen(p),
            "steam-backup" => await SteamBackupAsync(p, cancellationToken).ConfigureAwait(false),
            "steam-restore" => await SteamRestoreAsync(p, cancellationToken).ConfigureAwait(false),
            "offline-reduction-apply" => await OfflineReductionApplyAsync(p, cancellationToken).ConfigureAwait(false),
            "workspace-layout-save" => await WorkspaceLayoutSaveAsync(p, cancellationToken).ConfigureAwait(false),
            "workspace-layout-apply" => await WorkspaceLayoutApplyAsync(p, cancellationToken).ConfigureAwait(false),
            "workspace-layout-remove" => await RemoveStateItemAsync("workspace-layouts", p.RequiredString("Id"), cancellationToken).ConfigureAwait(false),
            "color-add" => await ColorAddAsync(p, cancellationToken).ConfigureAwait(false),
            "color-remove" => await RemoveStateItemAsync("color-palette", p.RequiredString("Id"), cancellationToken).ConfigureAwait(false),
            "note-save" => await NoteSaveAsync(p, cancellationToken).ConfigureAwait(false),
            "note-remove" => await RemoveStateItemAsync("notes", p.RequiredString("Id"), cancellationToken).ConfigureAwait(false),
            "remote-consent-create" => await RemoteConsentCreateAsync(p, cancellationToken).ConfigureAwait(false),
            "remote-consent-state" => await RemoteConsentStateAsync(p, cancellationToken).ConfigureAwait(false),
            "remote-consent-expire" => await RemoteConsentExpireAsync(p, cancellationToken).ConfigureAwait(false),
            "remote-emergency" => await RemoteEmergencyAsync(p, cancellationToken).ConfigureAwait(false),
            "studio-monitoring-export" => await ExportStateAsync("telemetry-history", p, "wincare-studio-monitoring.json", cancellationToken).ConfigureAwait(false),
            "studio-file-workspace-save" => await UpsertStateItemAsync("studio-file-workspaces", p, cancellationToken).ConfigureAwait(false),
            "studio-layout-save" => await UpsertStateItemAsync("studio-layout-profiles", p, cancellationToken).ConfigureAwait(false),
            "studio-brightness-save" => await UpsertStateItemAsync("studio-brightness-schedules", p, cancellationToken).ConfigureAwait(false),
            "studio-brightness-apply" => DisplayCalibrate(p),
            "studio-folder-appearance" => FolderAppearance(p),
            "studio-wezterm" => await StudioWezTermAsync(p, cancellationToken).ConfigureAwait(false),
            "studio-adb-inventory" => await StudioAdbAsync(p, cancellationToken).ConfigureAwait(false),
            "studio-xbox-fse" => await StudioXboxFseAsync(p, cancellationToken).ConfigureAwait(false),
            "hardening-apply" => await HardeningApplyAsync(p, cancellationToken).ConfigureAwait(false),
            "maintenance-template-create" => await UpsertStateItemAsync("maintenance-templates", p, cancellationToken).ConfigureAwait(false),
            "system-shortcuts-export" => await SystemShortcutsExportAsync(p, cancellationToken).ConfigureAwait(false),
            "download-batch" => await DownloadBatchAsync(p, cancellationToken).ConfigureAwait(false),
            "experience-visual-manifest" => await ExperienceVisualManifestAsync(p, cancellationToken).ConfigureAwait(false),
            "experience-location-set" => ExperienceLocationSet(p),
            "experience-remote-set" => await UpsertStateItemAsync("experience-remote", p, cancellationToken).ConfigureAwait(false),
            "experience-power-apply" => await ExperiencePowerApplyAsync(p, cancellationToken).ConfigureAwait(false),
            "experience-privacy-apply" => await ExperiencePrivacyApplyAsync(p, cancellationToken).ConfigureAwait(false),
            "hardware-report" => await HardwareReportAsync(p, cancellationToken).ConfigureAwait(false),
            "monitor-control-set" => DisplayCalibrate(p),
            "explorer-session-save" => await ExplorerSessionSaveAsync(p, cancellationToken).ConfigureAwait(false),
            "explorer-session-restore" => await ExplorerSessionRestoreAsync(p, cancellationToken).ConfigureAwait(false),
            "appx-launch" => AppxLaunch(p),
            "terminal-export" => await TerminalExportAsync(p, cancellationToken).ConfigureAwait(false),
            "cleaner-disk-pressure" => CleanerDiskPressure(p, cancellationToken),
            "cleaner-disk-pressure-schedule" => await UpsertStateItemAsync("cleaner-schedules", p, cancellationToken).ConfigureAwait(false),
            "cleaner-winapp2-run" => CleanerWinapp2Run(p, cancellationToken),
            "cleaner-relocation" => await CleanerRelocationAsync(p, cancellationToken).ConfigureAwait(false),
            "file-preview-export" => await FilePreviewExportAsync(p, cancellationToken).ConfigureAwait(false),
            "peer-display-reset" => DisplayOverridesReset(),
            "peer-container-log-config" => await UpsertStateItemAsync("peer-container-log-config", p, cancellationToken).ConfigureAwait(false),
            "peer-task-save" => await UpsertStateItemAsync("peer-taskboard", p, cancellationToken).ConfigureAwait(false),
            "explorer-quick" => ExplorerQuick(p),
            "deep-clean" => DeepClean(p, cancellationToken),
            "appx-selection" => await AppxSelectionAsync(p, online: true, cancellationToken).ConfigureAwait(false),
            "offline-appx-selection" => await AppxSelectionAsync(p, online: false, cancellationToken).ConfigureAwait(false),
            "legacy-unsafe" => await LegacyUnsafeAsync(p, cancellationToken).ConfigureAwait(false),
            "vbs-harden" => await VbsHardenAsync(p, cancellationToken).ConfigureAwait(false),
            "sandbox-config" => await SandboxConfigAsync(p, cancellationToken).ConfigureAwait(false),
            "telemetry-ingest" => await TelemetryIngestAsync(p, cancellationToken).ConfigureAwait(false),
            "telemetry-retention" => await TelemetryRetentionAsync(p, cancellationToken).ConfigureAwait(false),
            "ebpf-admit" => await EbpfAdmitAsync(p, cancellationToken).ConfigureAwait(false),
            "display-calibrate" => DisplayCalibrate(p),
            _ => throw new InvalidOperationException($"Mutating command '{definition.Id}' is missing a native route."),
        };
    }

    internal static void ValidateCommandParameters(CommandDefinition definition, CommandParameters p)
    {
        static void RequireStrings(CommandParameters parameters, params string[] names)
        {
            foreach (string name in names) _ = parameters.RequiredString(name);
        }

        static void RequireIds(CommandParameters parameters, string name)
        {
            if (parameters.StringArray(name).Count == 0)
            {
                throw new CommandParameterException(name, $"Parameter '{name}' must contain at least one value.");
            }
        }

        switch (definition.Id)
        {
            case "process-modules":
            case "appcontainer":
                _ = p.Int32("ProcessId", 0, 1, int.MaxValue);
                break;
            case "experience-visual-asset":
            case "archive-inspect":
            case "peer-log-tail":
            case "peer-pe":
            case "file-preview":
            case "image-metadata":
                _ = p.RequiredString("Path");
                break;
            case "experience-sprite-layout":
                _ = p.Int32("FrameWidth", 0, 1, 16384);
                _ = p.Int32("FrameHeight", 0, 1, 16384);
                _ = p.Int32("SheetWidth", 0, 1, 65536);
                _ = p.Int32("SheetHeight", 0, 1, 65536);
                break;
            case "preset":
                RequireStrings(p, "PresetId");
                break;
            case "pagefile-set":
            {
                string mode = p.RequiredString("Mode");
                if (!mode.Equals("Automatic", StringComparison.OrdinalIgnoreCase) &&
                    !mode.Equals("SystemManaged", StringComparison.OrdinalIgnoreCase))
                {
                    JsonElement settings = p.Element("Settings");
                    if (settings.ValueKind != JsonValueKind.Array || settings.GetArrayLength() == 0)
                        throw new CommandParameterException("Settings", "Custom pagefile mode requires a non-empty Settings array.");
                }
                break;
            }
            case "security-control-reduce":
            {
                RequireStrings(p, "Control", "Reason");
                string control = p.RequiredString("Control").Trim();
                string[] admitted = ["DefenderRealtime", "SmartScreenShell", "Uac", "WindowsUpdateStack"];
                if (!admitted.Contains(control, StringComparer.OrdinalIgnoreCase))
                    throw new CommandParameterException("Control", $"Control must be one of: {string.Join(", ", admitted)}.");
                _ = p.Int32("DurationMinutes", 15, 5, 1440);
                string reason = p.RequiredString("Reason").Trim();
                if (reason.Length > 500) throw new CommandParameterException("Reason", "Reason must not exceed 500 characters.");
                string environmentClass = p.String("EnvironmentClass", "Workstation").Trim();
                if (!new[] { "Workstation", "Test", "DisposableLab" }.Contains(environmentClass, StringComparer.OrdinalIgnoreCase))
                    throw new CommandParameterException("EnvironmentClass", "EnvironmentClass must be Workstation, Test, or DisposableLab.");
                if (control.Equals("Uac", StringComparison.OrdinalIgnoreCase) && !environmentClass.Equals("DisposableLab", StringComparison.OrdinalIgnoreCase))
                    throw new CommandParameterException("EnvironmentClass", "UAC reduction is limited to DisposableLab environments.");
                break;
            }
            case "security-control-restore": RequireStrings(p, "RecordId"); break;
            case "network-experiment": RequireStrings(p, "Setting", "Value"); break;
            case "etw-capture":
                _ = p.Int32("DurationSeconds", 15, 1, 300);
                break;
            case "injection-surface-quarantine":
                if (!p.Contains("ProcessId")) RequireStrings(p, "SurfaceId");
                else _ = p.Int32("ProcessId", 0, 1, int.MaxValue);
                break;
            case "wua-hide":
            case "wua-unhide":
            case "wua-download":
            case "wua-install":
            case "wua-uninstall":
                RequireIds(p, "UpdateIds");
                break;
            case "wdac-deploy": RequireStrings(p, "Path"); break;
            case "bcd-export": RequireStrings(p, "Destination"); break;
            case "provisioning-plan": RequireStrings(p, "Path"); break;
            case "run-automation":
            {
                JsonElement steps = p.Element("Steps");
                ValidateCommandPlanSteps(steps);
                break;
            }
            case "cancel-operation": RequireStrings(p, "OperationId"); break;
            case "maintenance-create": RequireStrings(p, "Name"); break;
            case "maintenance-transition": RequireStrings(p, "Id", "State"); break;
            case "context-menu-set": RequireStrings(p, "Id", "Command"); break;
            case "playbook": RequireStrings(p, "Id"); break;
            case "group-policy-import":
            case "sysmon-configure":
                RequireStrings(p, "Path");
                break;
            case "offline-driver-add": RequireStrings(p, "ImagePath", "Path"); break;
            case "offline-driver-remove": RequireStrings(p, "ImagePath", "Driver"); break;
            case "offline-package-add": RequireStrings(p, "ImagePath", "Path"); break;
            case "offline-feature-set": RequireStrings(p, "ImagePath", "FeatureName", "State"); break;
            case "power-stop": RequireStrings(p, "Id"); break;
            case "window-zone-set":
            case "window-topmost":
            case "window-activate":
                _ = p.Int64("Handle", 0, 1, long.MaxValue);
                break;
            case "download-create":
            {
                string uri = p.RequiredString("Url");
                if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) || parsed.Scheme is not ("http" or "https"))
                    throw new CommandParameterException("Url", "Url must be an absolute HTTP or HTTPS URL.");
                break;
            }
            case "download-start":
            case "download-suspend":
            case "download-resume":
            case "download-reconcile":
            case "download-cancel":
            case "download-remove":
                RequireStrings(p, "Id");
                break;
            case "download-batch": RequireIds(p, "Ids"); break;
            case "launcher-open": RequireStrings(p, "Target"); break;
            case "steam-backup":
            case "steam-restore": RequireStrings(p, "Source", "Destination"); break;
            case "offline-reduction-apply":
                RequireStrings(p, "ImagePath");
                if (p.StringArray("DisableFeatures").Count + p.StringArray("RemovePackages").Count == 0)
                    throw new CommandParameterException("DisableFeatures", "Provide DisableFeatures and/or RemovePackages explicitly.");
                break;
            case "workspace-layout-apply":
            case "workspace-layout-remove": RequireStrings(p, "Id"); break;
            case "color-add": RequireStrings(p, "Color"); break;
            case "color-remove": RequireStrings(p, "Id"); break;
            case "note-save": RequireStrings(p, "Text"); break;
            case "note-remove": RequireStrings(p, "Id"); break;
            case "remote-consent-create": RequireStrings(p, "Subject"); break;
            case "remote-consent-state": RequireStrings(p, "Id", "State"); break;
            case "remote-consent-expire": RequireStrings(p, "Id"); break;
            case "studio-brightness-apply":
            case "monitor-control-set":
            case "display-calibrate":
                if (!p.Contains("Brightness") && !p.Contains("Contrast"))
                    throw new CommandParameterException("Brightness", "Provide Brightness and/or Contrast.");
                if (p.Contains("Brightness")) _ = p.Int32("Brightness", 0, 0, 100);
                if (p.Contains("Contrast")) _ = p.Int32("Contrast", 0, 0, 100);
                _ = p.Int32("PhysicalIndex", 0, 0, 64);
                break;
            case "studio-folder-appearance": RequireStrings(p, "Path"); break;
            case "studio-adb-inventory": RequireStrings(p, "AdbPath", "Sha256"); break;
            case "studio-xbox-fse": RequireStrings(p, "ViVeToolPath", "Sha256"); break;
            case "experience-visual-manifest":
                RequireStrings(p, "Path");
                if (p.Element("Entries").ValueKind != JsonValueKind.Array)
                    throw new CommandParameterException("Entries", "Entries must be an array.");
                break;
            case "experience-location-set":
                _ = p.Int32("GeoId", -1, 0, int.MaxValue);
                if (!p.Contains("GeoId")) throw new CommandParameterException("GeoId", "GeoId is required.");
                break;
            case "experience-power-apply":
            {
                string profile = p.String("ProfileId", "balanced");
                if (profile is not ("balanced" or "high-performance" or "power-saver") && !Guid.TryParse(p.String("Scheme"), out _))
                    throw new CommandParameterException("ProfileId", "ProfileId must be balanced, high-performance, power-saver, or Scheme must be a GUID.");
                break;
            }
            case "experience-privacy-apply":
                _ = p.Boolean("IncludeTelemetry", true);
                break;
            case "explorer-session-restore": RequireStrings(p, "Id"); break;
            case "appx-launch": RequireStrings(p, "AppUserModelId"); break;
            case "cleaner-disk-pressure": _ = p.Int32("OlderThanDays", 7, 0, 3650); break;
            case "cleaner-winapp2-run": RequireStrings(p, "Path"); break;
            case "cleaner-relocation": RequireStrings(p, "Source", "Destination"); break;
            case "file-preview-export": RequireStrings(p, "Path"); break;
            case "explorer-quick": RequireStrings(p, "Path"); break;
            case "deep-clean":
            {
                string profile = p.String("ProfileId", "temp");
                if (!profile.Equals("temp", StringComparison.OrdinalIgnoreCase) &&
                    !profile.Equals("wincare-cache", StringComparison.OrdinalIgnoreCase))
                    throw new CommandParameterException("ProfileId", "ProfileId must be temp or wincare-cache.");
                break;
            }
            case "appx-selection": RequireIds(p, "PackageNames"); break;
            case "offline-appx-selection": RequireStrings(p, "ImagePath"); RequireIds(p, "PackageNames"); break;
            case "legacy-unsafe": RequireStrings(p, "Action"); break;
            case "sandbox-config": RequireStrings(p, "Path"); break;
            case "telemetry-ingest":
                RequireStrings(p, "Name");
                _ = p.Element("Record");
                break;
            case "telemetry-retention": _ = p.Int32("RetentionDays", 30, 1, 3650); break;
            case "ebpf-admit": RequireStrings(p, "Path"); break;

            case "cleaner-disk-pressure-schedule":
            case "download-start-due":
            case "experience-remote-set":
            case "explorer-session-save":
            case "hardening-apply":
            case "hardware-report":
            case "input-release":
            case "maintenance-export":
            case "maintenance-template-create":
            case "peer-container-log-config":
            case "peer-display-reset":
            case "peer-task-save":
            case "power-start":
            case "remote-emergency":
            case "studio-brightness-save":
            case "studio-file-workspace-save":
            case "studio-layout-save":
            case "studio-monitoring-export":
            case "studio-wezterm":
            case "sysmon-uninstall":
            case "system-shortcuts-export":
            case "telemetry-capture":
            case "telemetry-export":
            case "terminal-export":
            case "vbs-harden":
            case "widget-export":
            case "workspace-layout-save":
                break;

            // Optional or parameterless commands
            default:
                break;
        }
    }

    private static bool IsCommandReversible(string commandId) =>
        commandId is "pagefile-set" or "experience-power-apply" or "security-control-restore" or "security-control-reduce";

    private static object GetAffectedResourcesForPreview(string commandId, CommandParameters p)
    {
        bool isReversible = IsCommandReversible(commandId);
        return commandId switch
        {
            "pagefile-set" => new[] { new { resourceType = "Registry/WMI", path = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", target = "PagingFiles", proposedValue = p.String("Mode", "SystemManaged"), reversible = true } },
            "experience-power-apply" => new[] { new { resourceType = "PowerScheme", path = @"PowerCfg", target = "ActiveScheme", proposedValue = p.String("ProfileId", "balanced"), reversible = true } },
            "security-control-reduce" => new[] { new { resourceType = "SecurityControl", path = p.String("Control", "DefenderRealtime"), target = "State", proposedValue = "Disabled", durationMinutes = p.Int32("DurationMinutes", 15, 5, 1440), reversible = true } },
            "security-control-restore" => new[] { new { resourceType = "SecurityControl", path = p.String("RecordId", "all"), target = "State", proposedValue = "Restored", reversible = true } },
            "cleaner-disk-pressure" => new[] { new { resourceType = "FileSystem", path = @"%TEMP%, %WINDIR%\Temp", target = "Cleanup", olderThanDays = p.Int32("OlderThanDays", 7, 0, 3650), proposedValue = "PurgeExpiredFiles", reversible = false } },
            "deep-clean" => new[] { new { resourceType = "FileSystem", path = @"C:\Windows\Temp", target = "DeepClean", profile = p.String("ProfileId", "temp"), proposedValue = "Purge", reversible = false } },
            "preset" => new[] { new { resourceType = "RemediationPreset", path = p.String("PresetId", ""), target = "PresetExecution", proposedValue = "ApplyAllRules", reversible = false } },
            _ => new[] { new { resourceType = "SystemState", path = commandId, target = "HostMutation", proposedValue = "Apply", reversible = isReversible } }
        };
    }

    private static CommandHandlerOutcome MutationPreview(CommandDefinition definition, CommandParameters p)
    {
        var affectedResources = GetAffectedResourcesForPreview(definition.Id, p);
        bool isReversible = IsCommandReversible(definition.Id);
        JsonElement data = JsonSerializer.SerializeToElement(new
        {
            commandId = definition.Id,
            title = definition.Title,
            action = "preview",
            risk = definition.Risk.ToString(),
            administratorAccess = definition.AdministratorAccess.ToString(),
            restart = definition.Restart.ToString(),
            reversibilityStatus = isReversible ? "Supported" : "Unsupported",
            reversible = isReversible,
            affectedResources,
            parameters = "Validated. Review the concrete affected resources payload before approval.",
        }, JsonOptions);
        return CommandHandlerOutcome.Succeeded(
            definition.Id + ".preview",
            $"Preview ready for '{definition.Title}'. No host state was changed.",
            data);
    }

    private static bool IsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static JsonElement Data(object value) => JsonSerializer.SerializeToElement(value, JsonOptions);

    private static CommandHandlerOutcome Success(string id, string message, object value, bool undo = false) =>
        CommandHandlerOutcome.Succeeded(id + ".ok", message, Data(value), undo);

    private static CommandHandlerOutcome Block(string id, string message) =>
        CommandHandlerOutcome.Blocked(id + ".blocked", message);

    /// <summary>
    /// Validates all steps in a command plan before execution or preview.
    /// </summary>
    /// <param name="steps">JSON array element containing command step objects.</param>
    public static void ValidateCommandPlanSteps(JsonElement steps)
    {
        if (steps.ValueKind != JsonValueKind.Array)
            throw new CommandParameterException("Steps", "Steps must be an array of command objects.");
        if (steps.GetArrayLength() is < 1 or > 50)
            throw new CommandParameterException("Steps", "Steps must be an array containing 1 to 50 commands.");

        foreach (JsonElement step in steps.EnumerateArray())
        {
            if (step.ValueKind != JsonValueKind.Object)
                throw new CommandParameterException("Steps", "Every command-plan step must be an object.");
            string id = step.TryGetProperty("commandId", out JsonElement idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
            if (id.Length == 0)
                throw new CommandParameterException("Steps", "Every command-plan step requires a commandId.");
            if (id is "run-automation" or "playbook" or "preset" or "cancel-operation")
                throw new CommandParameterException("Steps", $"Recursive orchestration command '{id}' is not allowed inside a command plan.");
            CommandDefinition? definition = WinCare.CommandCatalog.CommandCatalog.Find(id);
            if (definition is null)
                throw new CommandParameterException("Steps", $"Unknown command '{id}' in command plan step.");

            JsonElement parameters = step.TryGetProperty("parameters", out JsonElement paramEl) && paramEl.ValueKind == JsonValueKind.Object
                ? paramEl.Clone()
                : Data(new { });

            CommandParameters cmdParams = new(parameters);
            ValidateCommandParameters(definition, cmdParams);
        }
    }

    /// <summary>
    /// Disposes native executor resources and cancels active operations.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (CancellationTokenSource source in _operations.Values)
        {
            source.Cancel();
            source.Dispose();
        }
        _operations.Clear();
        _httpClient.Dispose();
    }
}
