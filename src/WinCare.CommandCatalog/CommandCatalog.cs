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
    private const int ExpectedCommandCount = 259;

    private static readonly Lazy<IReadOnlyList<CommandDefinition>> Commands = new(LoadCore);
    private static readonly Lazy<IReadOnlyDictionary<string, CommandDefinition>> CommandsById = new(
        () => new ReadOnlyDictionary<string, CommandDefinition>(
            Commands.Value.ToDictionary(command => command.Id, StringComparer.Ordinal)));

    /// <summary>
    /// Returns all cataloged command definitions.
    /// </summary>
    public static IReadOnlyList<CommandDefinition> Load() => Commands.Value;

    /// <summary>
    /// Finds a single command definition by ID, or <c>null</c> when the ID is unknown.
    /// </summary>
    public static CommandDefinition? Find(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return CommandsById.Value.GetValueOrDefault(id);
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

        if (document.CommandCount != ExpectedCommandCount || document.Commands.Count != ExpectedCommandCount)
        {
            throw new InvalidOperationException(
                $"The native command catalog must contain exactly {ExpectedCommandCount} commands.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
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
