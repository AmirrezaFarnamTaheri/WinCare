using System.Globalization;
using System.Text.Json;
using WinCare.CommandCatalog.Models;

namespace WinCare.CommandCatalog;

/// <summary>
/// Validates a JSON parameter payload against a command's declared <see cref="CommandParameterCatalog"/>
/// schema, returning the set of human-readable validation errors (an empty result means the payload
/// satisfies the declared contract). The same validator backs both playbook import and direct
/// dispatch so the parameter trust boundary cannot become asymmetric between the two entry points.
/// </summary>
/// <remarks>
/// A command that declares no parameter schema (dynamic plugin commands, catalog commands without a
/// parameter block) is accepted unchanged. JSON-valued parameters are permitted here — the stricter
/// "no JSON parameters" rule is an import-only restriction enforced by
/// <c>PortablePlaybookExchange</c>, because an imported playbook cannot be re-checked by the
/// declaring handler the way an interactive dispatch can.
/// </remarks>
public static class CommandParameterValidator
{
    private const int MaxTextParameterCharacters = 256 * 1024;
    private const int MaxStringListItems = 256;
    private const int MaxStringListItemCharacters = 32 * 1024;

    /// <summary>
    /// Validates the parameters for the specified command, or returns an empty list when the command
    /// declares no parameter schema (in which case the payload is unconstrained).
    /// </summary>
    public static IReadOnlyList<string> Validate(string commandId, JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return new[] { $"Parameters for '{commandId}' must be an object." };
        }

        IReadOnlyList<CommandParameterDefinition> schema = CommandParameterCatalog.For(commandId);
        if (schema.Count == 0)
        {
            return Array.Empty<string>();
        }

        var errors = new List<string>();
        Dictionary<string, CommandParameterDefinition> byName =
            schema.ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);

        foreach (JsonProperty property in parameters.EnumerateObject())
        {
            if (!byName.TryGetValue(property.Name, out CommandParameterDefinition? definition))
            {
                errors.Add($"Parameter '{property.Name}' is not declared for '{commandId}'.");
                continue;
            }

            ValidateParameter(commandId, definition, property.Value, errors);
        }

        foreach (CommandParameterDefinition definition in schema.Where(parameter => parameter.Required))
        {
            if (!parameters.TryGetProperty(definition.Name, out _))
            {
                errors.Add($"Parameter '{definition.Name}' is required for '{commandId}'.");
            }
        }

        return errors;
    }

    private static void ValidateParameter(string commandId, CommandParameterDefinition definition, JsonElement value, List<string> errors)
    {
        switch (definition.Kind)
        {
            case CommandParameterKind.Text:
            case CommandParameterKind.DateTime:
                if (value.ValueKind != JsonValueKind.String)
                {
                    errors.Add(InvalidType(commandId, definition, "a string"));
                    break;
                }
                string text = value.GetString() ?? string.Empty;
                if (text.Length > MaxTextParameterCharacters)
                {
                    errors.Add($"Parameter '{definition.Name}' for '{commandId}' is too long.");
                }
                else if (definition.Kind == CommandParameterKind.DateTime &&
                         !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
                {
                    errors.Add($"Parameter '{definition.Name}' for '{commandId}' must be an ISO-8601 date/time.");
                }
                else if (definition.Options is { Count: > 0 } && !definition.Options.Contains(text, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"Parameter '{definition.Name}' for '{commandId}' has an unsupported value.");
                }
                break;
            case CommandParameterKind.Integer:
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int integer))
                    errors.Add(InvalidType(commandId, definition, "an integer"));
                else
                    ValidateNumberRange(commandId, definition, integer, errors);
                break;
            case CommandParameterKind.Long:
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long longValue))
                    errors.Add(InvalidType(commandId, definition, "an integer"));
                else
                    ValidateNumberRange(commandId, definition, longValue, errors);
                break;
            case CommandParameterKind.Number:
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) || !double.IsFinite(number))
                    errors.Add(InvalidType(commandId, definition, "a finite number"));
                else
                    ValidateNumberRange(commandId, definition, number, errors);
                break;
            case CommandParameterKind.Boolean:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    errors.Add(InvalidType(commandId, definition, "a boolean"));
                break;
            case CommandParameterKind.StringList:
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > MaxStringListItems)
                {
                    errors.Add(InvalidType(commandId, definition, "an array of strings"));
                }
                else
                {
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String || (item.GetString()?.Length ?? 0) > MaxStringListItemCharacters)
                        {
                            errors.Add(InvalidType(commandId, definition, "an array of strings"));
                            break;
                        }
                    }
                }
                break;
            case CommandParameterKind.Json:
                break;
            default:
                errors.Add($"Parameter '{definition.Name}' for '{commandId}' has an unsupported type.");
                break;
        }
    }

    private static void ValidateNumberRange(string commandId, CommandParameterDefinition definition, double value, List<string> errors)
    {
        if ((definition.Minimum is not null && value < double.Parse(definition.Minimum, CultureInfo.InvariantCulture)) ||
            (definition.Maximum is not null && value > double.Parse(definition.Maximum, CultureInfo.InvariantCulture)))
        {
            errors.Add($"Parameter '{definition.Name}' for '{commandId}' is outside its supported range.");
        }
    }

    private static string InvalidType(string commandId, CommandParameterDefinition definition, string expected) =>
        $"Parameter '{definition.Name}' for '{commandId}' must be {expected}.";
}
