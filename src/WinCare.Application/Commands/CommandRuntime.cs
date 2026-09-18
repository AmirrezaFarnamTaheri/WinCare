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
    /// <param name="executor">Fail-closed command executor backing the dispatcher.</param>
    /// <param name="nativeCore">Optional native core interop passed through to the dispatcher.</param>
    /// <param name="journal">Optional activity journal passed through to the dispatcher.</param>
    /// <param name="timeProvider">Optional clock passed through to the dispatcher.</param>
    /// <param name="isProcessElevated">
    /// Optional override for the elevation probe backing the administrator-access admission gate;
    /// see <see cref="CommandDispatcher.CommandDispatcher(IReadOnlyList{CommandDefinition}, IEnumerable{ICommandHandler}, TimeProvider?, INativeCoreService?, IActivityJournalService?, Func{bool}?)"/>.
    /// </param>
    public static CommandDispatcher CreateDefault(
        ICommandOperationExecutor executor,
        INativeCoreService? nativeCore = null,
        IActivityJournalService? journal = null,
        TimeProvider? timeProvider = null,
        Func<bool>? isProcessElevated = null)
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
            journal,
            isProcessElevated);
    }
}
