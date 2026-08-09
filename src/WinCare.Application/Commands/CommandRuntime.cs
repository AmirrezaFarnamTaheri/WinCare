using WinCare.Application.Activity;
using WinCare.Application.Commands.Handlers;
using WinCare.Infrastructure.Native;

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
    public static CommandDispatcher CreateDefault()
    {
        NativeCoreService nativeCore = new();
        ActivityJournalService journal = new();
        _lastJournal = journal;

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
