using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using WinCare.CommandCatalog.Models;

namespace WinCare.CommandCatalog;

/// <summary>
/// Loads and validates the embedded native command catalog.
/// </summary>
public static class CommandCatalog
{
    private const string ResourceName = "WinCare.CommandCatalog.Data.commands.json";
    // 259 frozen legacy IDs, five native AppX inventory/removal commands, installer-cache analysis,
    // three read-only discovery routes, and one receipt-bound remediation restore route.
    private const int ExpectedCommandCount = 269;

    private static readonly Lazy<IReadOnlyList<CommandDefinition>> Commands = new(LoadCore);
    private static readonly Lazy<IReadOnlyDictionary<string, CommandDefinition>> CommandsById = new(
        () => new ReadOnlyDictionary<string, CommandDefinition>(
            Commands.Value.ToDictionary(command => command.Id, StringComparer.OrdinalIgnoreCase)));

    /// <summary>
    /// Returns all cataloged command definitions.
    /// </summary>
    public static IReadOnlyList<CommandDefinition> Load() => Commands.Value;

    /// <summary>
    /// Returns the core catalog merged with every supplied extension-pack fragment. Fragment ids
    /// must be disjoint from the core and from each other; a pack re-declaring an existing id is
    /// rejected instead of silently shadowing it.
    /// </summary>
    public static IReadOnlyList<CommandDefinition> LoadWithPacks(IReadOnlyCollection<CommandPackFragment>? packs)
    {
        if (packs is null || packs.Count == 0)
        {
            return Load();
        }

        List<CommandDefinition> merged = new(Load());
        HashSet<string> ids = new(merged.Count, StringComparer.OrdinalIgnoreCase);
        foreach (CommandDefinition core in merged)
        {
            ids.Add(core.Id);
        }

        HashSet<string> packIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (CommandPackFragment pack in packs)
        {
            ArgumentNullException.ThrowIfNull(pack);
            CommandPackCatalog.ValidatePackId(pack.PackId);
            if (!packIds.Add(pack.PackId))
            {
                throw new InvalidOperationException($"Extension pack id '{pack.PackId}' is registered more than once.");
            }
            CommandPackCatalog.ValidateCommands(pack.PackId, pack.Commands);
            foreach (CommandDefinition command in pack.Commands)
            {
                if (!ids.Add(command.Id))
                {
                    throw new InvalidOperationException(
                        $"Extension pack '{pack.PackId}' re-declares command '{command.Id}' already owned by the core catalog or another pack.");
                }
                merged.Add(CommandPackCatalog.NormalizeCommand(command));
            }
        }

        return Array.AsReadOnly(merged.ToArray());
    }

    /// <summary>
    /// Finds a single command definition by ID, or <c>null</c> when the ID is unknown.
    /// </summary>
    public static CommandDefinition? Find(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return CommandsById.Value.GetValueOrDefault(id);
    }

    /// <summary>
    /// Finds a command definition by ID across the core catalog plus extension-pack fragments.
    /// </summary>
    public static CommandDefinition? FindIn(IReadOnlyCollection<CommandPackFragment>? packs, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (packs is null || packs.Count == 0)
        {
            return Find(id);
        }

        return LoadWithPacks(packs).FirstOrDefault(command =>
            string.Equals(command.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<CommandDefinition> LoadCore()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded command catalog '{ResourceName}' is missing.");

        CommandCatalogDocument document = JsonSerializer.Deserialize(stream, CommandCatalogJsonContext.Default.CommandCatalogDocument)
            ?? throw new InvalidOperationException("The embedded command catalog is empty.");

        Validate(document);
        return Array.AsReadOnly(document.Commands.ToArray());
    }

    private static void Validate(CommandCatalogDocument document)
    {
        if (document.SchemaVersion != 1)
        {
            throw new InvalidOperationException($"Unsupported command catalog schema {document.SchemaVersion}.");
        }

        if (document.Commands is null ||
            document.CommandCount != ExpectedCommandCount || document.Commands.Count != ExpectedCommandCount)
        {
            throw new InvalidOperationException(
                $"The native command catalog must contain exactly {ExpectedCommandCount} commands.");
        }

        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (CommandDefinition command in document.Commands)
        {
            if (string.IsNullOrWhiteSpace(command.Id) ||
                string.IsNullOrWhiteSpace(command.Title) ||
                string.IsNullOrWhiteSpace(command.Summary) ||
                string.IsNullOrWhiteSpace(command.Area) ||
                string.IsNullOrWhiteSpace(command.Section))
            {
                throw new InvalidOperationException("Every command requires an ID and plain-language metadata.");
            }

            if (!ids.Add(command.Id))
            {
                throw new InvalidOperationException($"Duplicate command ID '{command.Id}'.");
            }
        }
    }
}
