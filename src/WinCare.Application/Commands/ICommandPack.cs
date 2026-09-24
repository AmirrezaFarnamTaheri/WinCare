using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;

namespace WinCare.Application.Commands;

/// <summary>
/// One extension pack: a self-contained set of catalog commands with their own implementations.
/// A pack ships its command definitions as an embedded catalog fragment
/// (<c>commands.&lt;packId&gt;.json</c>, loaded through <see cref="WinCare.CommandCatalog.CommandPackCatalog" />)
/// and supplies the handlers for exactly those commands.
/// </summary>
/// <remarks>
/// Packs extend the kernel rather than forking it: admission, parameter validation, risk-tier
/// policy, preview receipts, and activity journaling all stay with <see cref="CommandDispatcher" />.
/// A pack only decides how its own commands execute.
/// </remarks>
public interface ICommandPack
{
    /// <summary>
    /// Gets the pack id. It identifies the pack's catalog fragment and appears in diagnostics.
    /// </summary>
    string PackId { get; }

    /// <summary>
    /// Loads the pack's command definitions. The default implementation reads the embedded
    /// fragment named <c>commands.&lt;PackId&gt;.json</c> from the pack's own assembly.
    /// </summary>
    IReadOnlyList<CommandDefinition> LoadCommands() =>
        WinCare.CommandCatalog.CommandPackCatalog.Load(GetType().Assembly, PackId).Commands;

    /// <summary>
    /// Creates the handlers for this pack's commands. Every handler id must be one this pack
    /// declared through <see cref="LoadCommands" />; handlers for ids the pack does not declare are
    /// rejected at composition.
    /// </summary>
    /// <param name="context">The kernel service surface the handlers may use.</param>
    IEnumerable<ICommandHandler> CreateHandlers(ICommandOperationContext context);
}
