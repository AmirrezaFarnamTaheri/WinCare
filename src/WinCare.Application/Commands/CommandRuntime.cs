using WinCare.Application.Activity;
using WinCare.Application.Native;
using WinCare.CommandCatalog.Models;

namespace WinCare.Application.Commands;

/// <summary>
/// Composition helper for the native command plane. Lifetime ownership stays with the caller.
/// </summary>
public static class CommandRuntime
{
    /// <summary>
    /// Creates a dispatcher for all catalog commands using one platform executor and one optional journal.
    /// </summary>
    public static CommandDispatcher CreateDefault(
        ICommandOperationExecutor executor,
        INativeCoreService? nativeCore = null,
        IActivityJournalService? journal = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(executor);
        IReadOnlyList<CommandDefinition> definitions = WinCare.CommandCatalog.CommandCatalog.Load();
        var handlers = definitions
            .Select(definition => (ICommandHandler)new DelegatingCommandHandler(definition, executor))
            .ToArray();

        return new CommandDispatcher(
            definitions,
            handlers,
            timeProvider ?? TimeProvider.System,
            nativeCore,
            journal);
    }
}
