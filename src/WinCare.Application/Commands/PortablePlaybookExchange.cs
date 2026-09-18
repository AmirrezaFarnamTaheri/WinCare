using System.Text.Json;
using WinCare.CommandCatalog;
using WinCare.CommandCatalog.Models;

namespace WinCare.Application.Commands;

/// <summary>
/// Validates a portable, declarative command list. Imported data has no approval
/// or execution authority and must be previewed again through the dispatcher.
/// </summary>
public static class PortablePlaybookExchange
{
    public const int SchemaVersion = 1;
    private const int MaximumSteps = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string Export(
        string name,
        IEnumerable<PortablePlaybookStep> steps,
        IReadOnlyList<CommandDefinition> catalog,
        Func<CommandDefinition, bool>? isAllowedByPolicy = null)
    {
        PortablePlaybookDocument document = new(SchemaVersion, name, steps.ToArray());
        Validate(document, catalog, isAllowedByPolicy);
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    public static ImportedPortablePlaybook Import(
        string json,
        IReadOnlyList<CommandDefinition> catalog,
        Func<CommandDefinition, bool>? isAllowedByPolicy = null)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new PortablePlaybookValidationException("The portable playbook is empty.");
        try
        {
            using JsonDocument parsed = JsonDocument.Parse(json);
            PortablePlaybookDocument document = ParseDocument(parsed.RootElement);
            Validate(document, catalog, isAllowedByPolicy);
            return new ImportedPortablePlaybook(document.Name, document.Steps, RequiresFreshPreview: true);
        }
        catch (JsonException exception)
        {
            throw new PortablePlaybookValidationException("The portable playbook is not valid JSON.", exception);
        }
    }

    private static PortablePlaybookDocument ParseDocument(JsonElement root)
    {
        RequireObject(root, "Portable playbook document");
        RequireOnlyProperties(root, "schemaVersion", "name", "steps");
        int schemaVersion = RequiredInt32(root, "schemaVersion");
        if (schemaVersion != SchemaVersion) throw new PortablePlaybookValidationException($"Unsupported portable playbook schema version '{schemaVersion}'.");
        string name = RequiredString(root, "name", 128);
        JsonElement steps = RequiredProperty(root, "steps");
        if (steps.ValueKind != JsonValueKind.Array) throw new PortablePlaybookValidationException("Portable playbook 'steps' must be an array.");
        var result = new List<PortablePlaybookStep>(steps.GetArrayLength());
        foreach (JsonElement step in steps.EnumerateArray())
        {
            RequireObject(step, "Portable playbook step");
            RequireOnlyProperties(step, "commandId", "parameters");
            string commandId = RequiredString(step, "commandId", 128);
            JsonElement parameters = RequiredProperty(step, "parameters");
            RequireObject(parameters, $"Parameters for '{commandId}'");
            result.Add(new PortablePlaybookStep(commandId, parameters.Clone()));
        }
        return new PortablePlaybookDocument(schemaVersion, name, result.ToArray());
    }

    private static void Validate(
        PortablePlaybookDocument document,
        IReadOnlyList<CommandDefinition> catalog,
        Func<CommandDefinition, bool>? isAllowedByPolicy)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(catalog);
        if (string.IsNullOrWhiteSpace(document.Name) || document.Name.Length > 128)
            throw new PortablePlaybookValidationException("Portable playbook name must contain 1 to 128 characters.");
        if (document.Steps is null || document.Steps.Count is 0 or > MaximumSteps)
            throw new PortablePlaybookValidationException($"Portable playbooks must contain 1 to {MaximumSteps} steps.");

        IReadOnlyDictionary<string, CommandDefinition> commands = catalog.ToDictionary(command => command.Id, StringComparer.OrdinalIgnoreCase);
        foreach (PortablePlaybookStep step in document.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.CommandId) || !commands.TryGetValue(step.CommandId, out CommandDefinition? command))
                throw new PortablePlaybookValidationException($"Command '{step.CommandId}' is not in the installed catalog.");
            if (command.MigrationStatus is not (MigrationStatus.Implemented or MigrationStatus.BehaviorVerified))
                throw new PortablePlaybookValidationException($"Command '{command.Id}' is not executable in this installation.");
            if (command.Id is "playbook" or "run-automation")
                throw new PortablePlaybookValidationException($"Command '{command.Id}' cannot be nested in a portable playbook.");
            if (isAllowedByPolicy is not null && !isAllowedByPolicy(command))
                throw new PortablePlaybookValidationException($"Command '{command.Id}' is not permitted by the active policy.");

            ValidateParameters(command.Id, step.Parameters);
        }
    }

    private static void ValidateParameters(string commandId, JsonElement parameters)
    {
        RequireObject(parameters, $"Parameters for '{commandId}'");

        // A JSON parameter is not permitted in an imported playbook: the import path cannot
        // re-check an arbitrary nested payload the way an interactive dispatch can.
        foreach (CommandParameterDefinition jsonParameter in CommandParameterCatalog.For(commandId)
                     .Where(parameter => parameter.Kind == CommandParameterKind.Json))
        {
            if (parameters.TryGetProperty(jsonParameter.Name, out _))
            {
                throw new PortablePlaybookValidationException($"JSON parameter '{jsonParameter.Name}' is not permitted in portable playbooks.");
            }
        }

        // Everything else (required presence, declared names, types, ranges, options) is shared with
        // the dispatch path through CommandParameterValidator so the two entry points agree.
        foreach (string error in CommandParameterValidator.Validate(commandId, parameters))
        {
            throw new PortablePlaybookValidationException(error);
        }
    }

    private static void RequireObject(JsonElement value, string description)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new PortablePlaybookValidationException($"{description} must be an object.");
    }

    private static JsonElement RequiredProperty(JsonElement objectValue, string name) =>
        objectValue.TryGetProperty(name, out JsonElement property)
            ? property
            : throw new PortablePlaybookValidationException($"Portable playbook property '{name}' is required.");

    private static string RequiredString(JsonElement objectValue, string name, int maximumLength)
    {
        JsonElement value = RequiredProperty(objectValue, name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > maximumLength)
            throw new PortablePlaybookValidationException($"Portable playbook property '{name}' must be a non-empty string up to {maximumLength} characters.");
        return value.GetString()!;
    }

    private static int RequiredInt32(JsonElement objectValue, string name)
    {
        JsonElement value = RequiredProperty(objectValue, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
            throw new PortablePlaybookValidationException($"Portable playbook property '{name}' must be an integer.");
        return result;
    }

    private static void RequireOnlyProperties(JsonElement objectValue, params string[] allowed)
    {
        foreach (JsonProperty property in objectValue.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                throw new PortablePlaybookValidationException($"Portable playbook property '{property.Name}' is not supported.");
    }
}

public sealed record PortablePlaybookDocument(int SchemaVersion, string Name, IReadOnlyList<PortablePlaybookStep> Steps);
public sealed record PortablePlaybookStep(string CommandId, JsonElement Parameters);
public sealed record ImportedPortablePlaybook(string Name, IReadOnlyList<PortablePlaybookStep> Steps, bool RequiresFreshPreview);
public sealed class PortablePlaybookValidationException(string message, Exception? innerException = null) : Exception(message, innerException);
