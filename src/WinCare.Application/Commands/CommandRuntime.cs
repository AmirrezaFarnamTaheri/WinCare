using WinCare.Application.Activity;
using WinCare.Application.Commands.Handlers;
using WinCare.Application.Native;

namespace WinCare.Application.Commands;

/// <summary>
/// Composition entry point for the native command plane.
/// </summary>
public static class CommandRuntime
{
    private static ActivityJournalService? _lastJournal;

    /// <summary>
    /// Returns the <see cref="ActivityJournalService"/> created by the most recent
    /// <see cref="CreateDefault"/> call, falling back to a new empty instance.
    /// This exists so XAML design-time and parameterless constructors can bind to
    /// a real journal without requiring a full DI container.
    /// </summary>
    public static ActivityJournalService LastJournal => _lastJournal ??= new ActivityJournalService();

    /// <summary>
    /// Creates the default dispatcher wired to the frozen native catalog and the
    /// implemented runtime handlers.
    /// </summary>
    public static CommandDispatcher CreateDefault(INativeCoreService? nativeCore = null)
    {
        ActivityJournalService journal = new();
        _lastJournal = journal;

        var handlers = new List<ICommandHandler>
        {
            new CatalogCommandHandler(),
            new PresetsCommandHandler(),
            new StorageHealthCommandHandler(),
            new NetworkStatusCommandHandler(),
            new PrivacyStatusCommandHandler(),
            new LogCleanupCommandHandler(),
            new StartupCommandHandler(),
            new SecurityStatusCommandHandler(),
            new ApplicationsCommandHandler(),
            new HealthStatusCommandHandler(),
            new VirtualMemoryCommandHandler(),
            new TcpGlobalCommandHandler(),
            new PagefileRecommendationCommandHandler(),
            new SecurityControlsCommandHandler(),
            new NetworkMeasureCommandHandler(),
            new ProcessModulesCommandHandler(),
            new SecurityMaintenanceCommandHandler(),
            new NetworkExperimentsCommandHandler(),
            new EtwSessionsCommandHandler(),
            new WuaHistoryCommandHandler(),
            new InjectionSurfacesCommandHandler(),
            new RemoteThreadEventsCommandHandler(),
            new WuaSearchCommandHandler(),
            new PresetExecutionCommandHandler(),
            new WdacPoliciesCommandHandler(),
            new WdacEventsCommandHandler(),
            new InternalsProcessesCommandHandler(),
            new WuaHideCommandHandler(),
            new InternalsMemoryCommandHandler(),
            new InternalsCpuCommandHandler(),
            new CredentialProvidersCommandHandler(),
            new AppContainerCommandHandler(),
            new PagefileSetCommandHandler(),
            new SecurityControlReduceCommandHandler(),
            new SecurityControlRestoreCommandHandler(),
            new NetworkExperimentCommandHandler(),
            new EtwCaptureCommandHandler(),
            new InjectionSurfaceQuarantineCommandHandler(),
            new WuaUnhideCommandHandler(),
            new WuaDownloadCommandHandler(),
            new WuaInstallCommandHandler(),
            new WuaUninstallCommandHandler(),
            new WdacDeployCommandHandler(),
            new DesktopControlsCommandHandler(),
            new WifiProfilesCommandHandler(),
            new ShellExtensionsCommandHandler(),
            new DesktopShortcutsCommandHandler(),
            new BootCommandHandler(),
            new BcdExportCommandHandler(),
            new UnattendAnalyzeCommandHandler(),
            new ProvisioningPlanCommandHandler(),
            new KnowledgeCommandHandler(),
            new ReportsCommandHandler(),
            new AutomationProfilesCommandHandler(),
            new RunAutomationCommandHandler(),
            new CancelOperationCommandHandler(),
            new WidgetsCommandHandler(),
            new WidgetCatalogCommandHandler(),
            new WidgetExportCommandHandler(),
            new BluetoothCommandHandler(),
            new BluetoothEventsCommandHandler(),
            new MaintenanceCommandHandler(),
            new MaintenanceCreateCommandHandler(),
            new MaintenanceTransitionCommandHandler(),
            new MaintenanceMetricsCommandHandler(),
            new MaintenanceExportCommandHandler(),
            new ContextMenuCommandHandler(),
            new ContextMenuSetCommandHandler(),
            new CustomizationHostsCommandHandler(),
            new RainmeterSkinsCommandHandler(),
            new WindhawkModsCommandHandler(),
            new ExplorerPatcherCommandHandler(),
            new ModernContextValidateCommandHandler(),
            new PlaybooksCommandHandler(),
            new PlaybookExecutionCommandHandler(),
            new GroupPolicyCommandHandler(),
            new GroupPolicyImportCommandHandler(),
            new SysmonCommandHandler(),
            new SysmonConfigureCommandHandler(),
            new SysmonUninstallCommandHandler(),
            new OfflineImagesCommandHandler(),
            new OfflineDriversCommandHandler(),
            new OfflinePackagesCommandHandler(),
            new OfflineFeaturesCommandHandler(),
            new OfflineDriverAddCommandHandler(),
            new OfflineDriverRemoveCommandHandler(),
            new OfflinePackageAddCommandHandler(),
            new OfflineFeatureSetCommandHandler(),
            new PowerSessionsCommandHandler(),
            new PowerStartCommandHandler(),
            new PowerStopCommandHandler(),
            new WindowsInventoryCommandHandler(),
            new MonitorsCommandHandler(),
            new WindowZonesCommandHandler(),
            new WindowZoneSetCommandHandler(),
            new WindowTopmostCommandHandler(),
            new WindowActivateCommandHandler(),
            new InputReleaseCommandHandler(),
            new DownloadsCommandHandler(),
            new DownloadsDueCommandHandler(),
            new DownloadCreateCommandHandler(),
            new DownloadStartCommandHandler(),
            new DownloadStartDueCommandHandler(),
            new DownloadSuspendCommandHandler(),
            new DownloadResumeCommandHandler(),
            new DownloadReconcileCommandHandler(),
            new DownloadCancelCommandHandler(),
            new DownloadRemoveCommandHandler(),
            new TelemetrySnapshotCommandHandler(),
            new TelemetryHistoryCommandHandler(),
            new TelemetryCaptureCommandHandler(),
            new TelemetryExportCommandHandler(),
            new LauncherSearchCommandHandler(),
            new LauncherOpenCommandHandler(),
            new CalculatorCommandHandler(),
            new SteamGamesCommandHandler(),
            new SteamUsersCommandHandler(),
            new SteamCloudFilesCommandHandler(),
            new SteamBackupCommandHandler(),
            new SteamRestoreCommandHandler(),
            new GameIntegrityCommandHandler(),
            new OfflineReductionProfilesCommandHandler(),
            new OfflineReductionAssessCommandHandler(),
            new OfflineReductionApplyCommandHandler(),
            new WorkspaceLayoutsCommandHandler(),
            new WorkspaceLayoutSaveCommandHandler(),
            new WorkspaceLayoutApplyCommandHandler(),
            new WorkspaceLayoutRemoveCommandHandler(),
            new ColorCaptureCommandHandler(),
            new ColorPaletteCommandHandler(),
            new ColorAddCommandHandler(),
            new ColorRemoveCommandHandler(),
            new ImageMetadataCommandHandler(),
            new NotesCommandHandler(),
            new NoteSaveCommandHandler(),
            new NoteRemoveCommandHandler(),
            new BrowsersCommandHandler(),
            new BrowserExtensionsCommandHandler(),
            new RemoteSupportCommandHandler(),
            new RemoteConsentCommandHandler(),
            new RemoteConsentCreateCommandHandler(),
            new RemoteConsentStateCommandHandler(),
            new RemoteConsentExpireCommandHandler(),
            new RemoteEmergencyCommandHandler(),
            new StudioSnapshotCommandHandler(),
            new StudioMonitoringExportCommandHandler(),
            new StudioFileWorkspacesCommandHandler(),
            new StudioFileWorkspaceSaveCommandHandler(),
            new StudioLayoutProfilesCommandHandler(),
            new StudioLayoutSaveCommandHandler(),
            new StudioPackageCommandHandler(),
            new StudioBrightnessSchedulesCommandHandler(),
            new StudioBrightnessSaveCommandHandler(),
            new StudioBrightnessApplyCommandHandler(),
            new StudioFolderAppearanceCommandHandler(),
            new StudioKanataValidateCommandHandler(),
            new StudioSyncthingCommandHandler(),
            new StudioWeztermCommandHandler(),
            new StudioAdbInventoryCommandHandler(),
            new StudioXboxFseCommandHandler(),
            new ToolkitDiagnosticsCommandHandler(),
            new ToolkitWin32ErrorCommandHandler(),
            new ToolkitMsiCommandHandler(),
            new HardeningProfilesCommandHandler(),
            new HardeningAssessCommandHandler(),
            new HardeningApplyCommandHandler(),
            new MaintenanceTemplatesCommandHandler(),
            new MaintenanceTemplateCreateCommandHandler(),
            new SystemShortcutsCommandHandler(),
            new SystemShortcutsExportCommandHandler(),
            new DownloadBatchCommandHandler(),
            new TorrentMetadataCommandHandler(),
        };

        if (nativeCore is not null)
        {
            handlers.Add(new SystemInfoCommandHandler(nativeCore));
            handlers.Add(new DiskCleanupCommandHandler(nativeCore));
        }

        return new CommandDispatcher(
            WinCare.CommandCatalog.CommandCatalog.Load(),
            handlers,
            TimeProvider.System,
            nativeCore,
            journal);
    }
}
