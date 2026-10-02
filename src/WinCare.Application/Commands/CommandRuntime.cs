using WinCare.Application.Activity;
using WinCare.Application.Native;
using WinCare.CommandCatalog;
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
        return Create(executor, WinCare.CommandCatalog.CommandCatalog.Load(), Array.Empty<ICommandHandler>(),
            nativeCore, journal, timeProvider, isProcessElevated);
    }

    /// <summary>
    /// Creates a dispatcher for the core catalog plus extension packs. Pack handlers win for their
    /// own command ids; a pack may not re-declare a core command id, and two packs may not both
    /// implement the same id. The core executor still backs every command the packs do not implement.
    /// </summary>
    /// <param name="executor">Fail-closed command executor backing core command ids.</param>
    /// <param name="packs">Extension packs supplying additional catalog commands and their handlers.</param>
    /// <param name="context">Kernel service surface passed to <see cref="ICommandPack.CreateHandlers" />.</param>
    /// <param name="nativeCore">Optional native core interop passed through to the dispatcher.</param>
    /// <param name="journal">Optional activity journal passed through to the dispatcher and the context.</param>
    /// <param name="timeProvider">Optional clock passed through to the dispatcher.</param>
    /// <param name="isProcessElevated">Optional override for the elevation admission-gate probe.</param>
    public static CommandDispatcher CreateDefault(
        ICommandOperationExecutor executor,
        IReadOnlyCollection<ICommandPack> packs,
        ICommandOperationContext context,
        INativeCoreService? nativeCore = null,
        IActivityJournalService? journal = null,
        TimeProvider? timeProvider = null,
        Func<bool>? isProcessElevated = null)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(context);

        List<CommandPackFragment> fragments = new(packs.Count);
        List<(ICommandPack Pack, CommandPackFragment Fragment)> loadedPacks = new(packs.Count);
        HashSet<string> packCommandIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (ICommandPack pack in packs)
        {
            ArgumentNullException.ThrowIfNull(pack);
            CommandPackCatalog.ValidatePackId(pack.PackId);
            string packId = pack.PackId;

            IReadOnlyList<CommandDefinition> commands = pack.LoadCommands()
                ?? throw new InvalidOperationException($"Extension pack '{packId}' returned no command definitions.");
            if (!string.Equals(packId, pack.PackId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("An extension pack changed its PackId while its commands were being loaded.");
            }
            CommandPackCatalog.ValidateCommands(packId, commands);
            CommandDefinition[] normalizedCommands = commands
                .Select(CommandPackCatalog.NormalizeCommand)
                .ToArray();
            CommandPackFragment fragment = new(packId, Array.AsReadOnly(normalizedCommands));
            fragments.Add(fragment);
            loadedPacks.Add((pack, fragment));

            foreach (CommandDefinition command in fragment.Commands)
            {
                if (!packCommandIds.Add(command.Id))
                {
                    throw new ArgumentException(
                        $"Command '{command.Id}' is declared by more than one extension pack.", nameof(packs));
                }
            }
        }

        // Reject invalid catalogs before invoking extension code that may have factory side effects.
        IReadOnlyList<CommandDefinition> definitions = WinCare.CommandCatalog.CommandCatalog.LoadWithPacks(fragments);
        List<ICommandHandler> packHandlers = new();
        HashSet<string> implementedIds = new(StringComparer.OrdinalIgnoreCase);
        foreach ((ICommandPack pack, CommandPackFragment fragment) in loadedPacks)
        {
            HashSet<string> declaredByPack = new(fragment.Commands.Select(command => command.Id), StringComparer.OrdinalIgnoreCase);
            HashSet<string> implementedByPack = new(StringComparer.OrdinalIgnoreCase);
            foreach (ICommandHandler handler in pack.CreateHandlers(context)
                ?? throw new InvalidOperationException($"Extension pack '{pack.PackId}' returned no handlers."))
            {
                ArgumentNullException.ThrowIfNull(handler);
                if (!declaredByPack.Contains(handler.CommandId))
                {
                    throw new ArgumentException(
                        $"Extension pack '{pack.PackId}' implements '{handler.CommandId}', which it does not declare in its catalog fragment.",
                        nameof(packs));
                }
                if (!implementedIds.Add(handler.CommandId))
                {
                    throw new ArgumentException($"More than one extension handler implements '{handler.CommandId}'.", nameof(packs));
                }
                implementedByPack.Add(handler.CommandId);
                packHandlers.Add(handler);
            }

            string[] missing = declaredByPack.Except(implementedByPack, StringComparer.OrdinalIgnoreCase).ToArray();
            if (missing.Length > 0)
            {
                throw new ArgumentException(
                    $"Extension pack '{pack.PackId}' declares commands without handlers: {string.Join(", ", missing)}.", nameof(packs));
            }
        }

        return Create(executor, definitions, packHandlers, nativeCore, journal, timeProvider, isProcessElevated);
    }

    /// <summary>
    /// Builds the dispatcher from a merged definition set, where extension handlers override the
    /// delegating core handlers for exactly the ids they own.
    /// </summary>
    private static CommandDispatcher Create(
        ICommandOperationExecutor executor,
        IReadOnlyList<CommandDefinition> definitions,
        IReadOnlyCollection<ICommandHandler> extensionHandlers,
        INativeCoreService? nativeCore,
        IActivityJournalService? journal,
        TimeProvider? timeProvider,
        Func<bool>? isProcessElevated)
    {
        Dictionary<string, ICommandHandler> handlersById = new(definitions.Count, StringComparer.OrdinalIgnoreCase);
        foreach (CommandDefinition definition in definitions)
        {
            handlersById[definition.Id] = new DelegatingCommandHandler(definition, executor);
        }

        foreach (ICommandHandler extension in extensionHandlers)
        {
            // Extension packs own their command ids outright (verified disjoint from the core at
            // load), so an override here replaces the delegating core handler rather than colliding
            // with it. Two packs implementing the same id were already rejected above.
            handlersById[extension.CommandId] = extension;
        }

        return new CommandDispatcher(
            definitions,
            handlersById.Values,
            timeProvider ?? TimeProvider.System,
            nativeCore,
            journal,
            isProcessElevated);
    }
}
