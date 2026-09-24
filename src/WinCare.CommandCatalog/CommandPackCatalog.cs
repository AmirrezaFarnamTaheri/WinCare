using System.Reflection;
using System.Text.Json;
using WinCare.CommandCatalog.Models;

namespace WinCare.CommandCatalog;

/// <summary>
/// A catalog fragment contributed by one extension pack: its pack id and the command definitions
/// the pack declares. A fragment never re-declares a core command id; the pack owns its commands
/// outright, so removing a pack removes exactly its own commands from the admitted surface.
/// </summary>
public sealed record CommandPackFragment(string PackId, IReadOnlyList<CommandDefinition> Commands);

/// <summary>
/// Loads and validates embedded catalog fragments carried by extension assemblies.
/// </summary>
/// <remarks>
/// A fragment is a JSON document with the same shape as the embedded core catalog
/// (<see cref="CommandCatalogDocument" />): a schema version, an exact command count, and the
/// command list. Naming convention: an embedded resource whose name ends with
/// <c>commands.&lt;packId&gt;.json</c> is the fragment for pack <c>&lt;packId&gt;</c>.
/// </remarks>
public static class CommandPackCatalog
{
    /// <summary>Resource-name suffix locating a pack fragment, appended after the pack id.</summary>
    public const string FragmentResourceSuffix = "commands.";

    /// <summary>Upper bound on a single catalog fragment, in bytes.</summary>
    public const int MaximumFragmentBytes = 8 * 1024 * 1024;

    /// <summary>Upper bound on command definitions in one extension fragment.</summary>
    public const int MaximumCommands = 10_000;

    /// <summary>
    /// Loads the fragment for one pack from an assembly's embedded resources.
    /// </summary>
    /// <param name="assembly">The assembly carrying the fragment.</param>
    /// <param name="packId">The pack id; the resource name must end with <c>commands.&lt;packId&gt;.json</c>.</param>
    /// <returns>The validated fragment.</returns>
    /// <exception cref="InvalidOperationException">The fragment is missing, duplicated, malformed, or self-inconsistent.</exception>
    public static CommandPackFragment Load(Assembly assembly, string packId)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ValidatePackId(packId);

        string suffix = FragmentResourceSuffix + packId + ".json";
        string[] matches = Array.FindAll(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith(suffix, StringComparison.Ordinal));

        if (matches.Length == 0)
        {
            throw new InvalidOperationException(
                $"Extension pack '{packId}' declares no embedded catalog fragment ending in '{suffix}'.");
        }

        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"Extension pack '{packId}' declares multiple embedded catalog fragments: {string.Join(", ", matches)}.");
        }

        using JsonDocument document = EmbeddedJsonResource.Read(assembly, matches[0], MaximumFragmentBytes);
        CommandCatalogDocument fragment = JsonSerializer.Deserialize(document.RootElement, CommandCatalogJsonContext.Default.CommandCatalogDocument)
            ?? throw new InvalidOperationException($"Extension pack '{packId}' has an empty catalog fragment.");

        ValidateFragment(packId, fragment);
        CommandDefinition[] normalizedCommands = fragment.Commands
            .Select(NormalizeCommand)
            .ToArray();
        return new CommandPackFragment(packId, Array.AsReadOnly(normalizedCommands));
    }

    /// <summary>
    /// Validates a fragment's self-consistency. Cross-pack and core collisions are rejected by
    /// <see cref="CommandCatalog.LoadWithPacks" /> when the merged set is assembled.
    /// </summary>
    private static void ValidateFragment(string packId, CommandCatalogDocument document)
    {
        if (document.SchemaVersion != 1)
        {
            throw new InvalidOperationException(
                $"Extension pack '{packId}' declares unsupported catalog schema {document.SchemaVersion}.");
        }

        if (document.Commands is null)
        {
            throw new InvalidOperationException($"Extension pack '{packId}' has a null commands collection.");
        }

        if (document.CommandCount != document.Commands.Count)
        {
            throw new InvalidOperationException(
                $"Extension pack '{packId}' declares {document.CommandCount} commands but lists {document.Commands.Count}.");
        }

        ValidateCommands(packId, document.Commands);
    }

    /// <summary>Validates command definitions even when a pack supplies them programmatically.</summary>
    public static void ValidateCommands(string packId, IReadOnlyList<CommandDefinition>? commands)
    {
        if (commands is null)
        {
            throw new InvalidOperationException($"Extension pack '{packId}' returned a null command list.");
        }
        if (commands.Count > MaximumCommands)
        {
            throw new InvalidOperationException(
                $"Extension pack '{packId}' declares {commands.Count} commands; the limit is {MaximumCommands}.");
        }

        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (CommandDefinition? command in commands)
        {
            if (command is null || string.IsNullOrWhiteSpace(command.Id) ||
                string.IsNullOrWhiteSpace(command.Title) ||
                string.IsNullOrWhiteSpace(command.Summary) ||
                string.IsNullOrWhiteSpace(command.Area) ||
                string.IsNullOrWhiteSpace(command.Section) ||
                !Enum.IsDefined(command.Risk) ||
                !Enum.IsDefined(command.AdministratorAccess) ||
                !Enum.IsDefined(command.Restart) ||
                !Enum.IsDefined(command.MigrationStatus) ||
                command.ExplicitRiskTier is { } riskTier && !Enum.IsDefined(riskTier))
            {
                throw new InvalidOperationException(
                    $"Extension pack '{packId}' has a command missing an ID or plain-language metadata, or with invalid enum metadata.");
            }

            if (!IsValidCommandId(command.Id) || !ids.Add(command.Id))
            {
                throw new InvalidOperationException(
                    $"Extension pack '{packId}' re-declares its own command or has a whitespace-padded command id '{command.Id}'.");
            }

            ValidateParameterSchema(packId, command);
        }
    }

    /// <summary>Validates an id used to identify an extension pack and its embedded resource.</summary>
    public static void ValidatePackId(string packId)
    {
        if (string.IsNullOrWhiteSpace(packId) || packId.Length > 128 ||
            !char.IsAsciiLetterOrDigit(packId[0]) || !char.IsAsciiLetterOrDigit(packId[^1]) ||
            packId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_')) ||
            packId.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A pack id must be 1-128 ASCII letters, digits, dots, hyphens, or underscores and start and end with a letter or digit.",
                nameof(packId));
        }
    }

    public static CommandDefinition NormalizeCommand(CommandDefinition command) => command with
    {
        Keywords = Array.AsReadOnly((command.Keywords ?? Array.Empty<string>()).ToArray()),
        Parameters = Array.AsReadOnly((command.Parameters ?? Array.Empty<CommandParameterDefinition>())
            .Select(parameter => parameter with
            {
                Options = parameter.Options is null ? null : Array.AsReadOnly(parameter.Options.ToArray())
            })
            .ToArray())
    };

    private static void ValidateParameterSchema(string packId, CommandDefinition command)
    {
        if (command.Parameters is null) return;

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (CommandParameterDefinition? parameter in command.Parameters)
        {
            if (parameter is null || string.IsNullOrWhiteSpace(parameter.Name) ||
                !IsValidParameterName(parameter.Name) ||
                !Enum.IsDefined(parameter.Kind) || !names.Add(parameter.Name) ||
                parameter.Options?.Any(string.IsNullOrWhiteSpace) == true)
            {
                throw new InvalidOperationException(
                    $"Extension pack '{packId}' has an invalid or duplicate parameter definition for '{command.Id}'.");
            }

            double? minimum = ParseLimit(parameter.Minimum, packId, command.Id, parameter.Name);
            double? maximum = ParseLimit(parameter.Maximum, packId, command.Id, parameter.Name);
            if (minimum is not null && maximum is not null && minimum > maximum)
            {
                throw new InvalidOperationException(
                    $"Extension pack '{packId}' has an inverted range for '{command.Id}.{parameter.Name}'.");
            }
        }
    }

    private static bool IsValidCommandId(string id) =>
        id.Length <= 128 && char.IsAsciiLetterOrDigit(id[0]) && char.IsAsciiLetterOrDigit(id[^1]) &&
        id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_') &&
        !id.Contains("..", StringComparison.Ordinal);

    private static bool IsValidParameterName(string name) =>
        name.Length <= 128 && char.IsAsciiLetterOrDigit(name[0]) && char.IsAsciiLetterOrDigit(name[^1]) &&
        name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static double? ParseLimit(string? value, string packId, string commandId, string parameterName)
    {
        if (value is null) return null;
        if (!double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed) || !double.IsFinite(parsed))
        {
            throw new InvalidOperationException(
                $"Extension pack '{packId}' has an invalid numeric bound for '{commandId}.{parameterName}'.");
        }
        return parsed;
    }
}
