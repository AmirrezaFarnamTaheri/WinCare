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
