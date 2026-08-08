using WinCare.Application.Activity;
using WinCare.Application.Commands.Handlers;
using WinCare.Infrastructure.Native;

namespace WinCare.Application.Commands;

/// <summary>
/// Composition entry point for the native command plane.
/// </summary>
public static class CommandRuntime
{
    /// <summary>
    /// Holds reference to the last created journal service instance.
    /// </summary>
    public static ActivityJournalService LastJournal { get; private set; } = new();

    /// <summary>
    /// Creates the default dispatcher wired to the frozen native catalog and the
    /// implemented runtime handlers.
    /// </summary>
    public static CommandDispatcher CreateDefault()
    {
        NativeCoreService nativeCore = new();
        ActivityJournalService journal = new();
        LastJournal = journal;

        return new CommandDispatcher(
            WinCare.CommandCatalog.CommandCatalog.Load(),
            [
                new CatalogCommandHandler(),
                new PresetsCommandHandler(),
                new SystemInfoCommandHandler(nativeCore),
                new StorageHealthCommandHandler(),
                new NetworkStatusCommandHandler(),
                new PrivacyStatusCommandHandler(),
                new DiskCleanupCommandHandler(nativeCore),
                new LogCleanupCommandHandler(),
            ],
            TimeProvider.System,
            nativeCore,
            journal);
    }
}
