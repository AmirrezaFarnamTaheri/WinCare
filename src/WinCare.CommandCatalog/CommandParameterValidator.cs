using System.Globalization;
using System.Numerics;
using System.Text.Json;
using WinCare.CommandCatalog.Models;

namespace WinCare.CommandCatalog;

/// <summary>
/// Validates a JSON parameter payload against the command's declared schema or the generated core
/// <see cref="CommandParameterCatalog"/>, returning human-readable validation errors (an empty result
/// means the payload satisfies the declared contract). The same validator backs playbook import and direct
/// dispatch so the parameter trust boundary cannot become asymmetric between the two entry points.
/// </summary>
/// <remarks>
/// A command that declares no parameter schema is accepted unchanged: neither dynamic commands nor
/// extension-pack entries that omit a schema are constrained by the generated core schema table,
/// so the payload reaches the declaring handler as supplied. JSON-valued
/// parameters are permitted here; the stricter
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
    public static IReadOnlyList<string> Validate(
        string commandId,
        JsonElement parameters,
        IReadOnlyList<CommandParameterDefinition>? declaredSchema = null)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return new[] { $"Parameters for '{commandId}' must be an object." };
        }

        bool hasDeclaredSchema = declaredSchema is not null;
        IReadOnlyList<CommandParameterDefinition> schema = declaredSchema ?? CommandParameterCatalog.For(commandId);
        if (schema.Count == 0 && !hasDeclaredSchema)
        {
            return Array.Empty<string>();
        }

        var errors = new List<string>();
        // A schema that declares one name twice under different casing cannot be applied
        // unambiguously. Building this lookup with ToDictionary threw on the duplicate, and that
        // throw escaped dispatch as an unhandled fault instead of a blocked request.
        Dictionary<string, CommandParameterDefinition> byName = new(StringComparer.OrdinalIgnoreCase);
        foreach (CommandParameterDefinition parameter in schema)
        {
            if (!byName.TryAdd(parameter.Name, parameter))
            {
                errors.Add($"Parameter '{parameter.Name}' is declared more than once for '{commandId}'.");
            }
        }

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
            // The declared-name lookup above is case-insensitive, so the presence check must be
            // too. A caller that supplied "packagenames" for a declared "PackageNames" was
            // already accepted; reporting it as missing here rejected a valid request.
            if (!ContainsParameter(parameters, definition.Name))
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
                    ValidateIntegerRange(commandId, definition, integer, errors);
                break;
            case CommandParameterKind.Long:
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long longValue))
                    errors.Add(InvalidType(commandId, definition, "an integer"));
                else
                    ValidateIntegerRange(commandId, definition, longValue, errors);
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

    /// <summary>
    /// Compares an integral value against the declared range using exact integer arithmetic.
    /// </summary>
    /// <remarks>
    /// A 64-bit value does not survive a round trip through <see cref="double"/>: values beyond
    /// 2^53 lose their low-order digits, so a bound and a value differing by one collapse onto
    /// the same double and an out-of-range value was admitted. Exact integer arithmetic keeps the
    /// comparison correct.
    /// </remarks>
    private static void ValidateIntegerRange(string commandId, CommandParameterDefinition definition, long value, List<string> errors)
    {
        bool minimumRead = TryReadIntegerBound(definition.Minimum, roundUp: true, out BigInteger? minimum);
        bool maximumRead = TryReadIntegerBound(definition.Maximum, roundUp: false, out BigInteger? maximum);
        if (!minimumRead || !maximumRead)
        {
            errors.Add(UnreadableRange(commandId, definition));
            return;
        }

        BigInteger candidate = value;
        if (minimum is { } lower && candidate < lower)
        {
            errors.Add(OutsideRange(commandId, definition));
            return;
        }

        if (maximum is { } upper && candidate > upper)
        {
            errors.Add(OutsideRange(commandId, definition));
        }
    }

    /// <summary>
    /// Compares a floating-point value against the declared range.
    /// </summary>
    private static void ValidateNumberRange(string commandId, CommandParameterDefinition definition, double value, List<string> errors)
    {
        if (!TryReadDoubleBound(definition.Minimum, out double? minimum) ||
            !TryReadDoubleBound(definition.Maximum, out double? maximum))
        {
            errors.Add(UnreadableRange(commandId, definition));
            return;
        }

        if (minimum is { } lower && value < lower)
        {
            errors.Add(OutsideRange(commandId, definition));
            return;
        }

        if (maximum is { } upper && value > upper)
        {
            errors.Add(OutsideRange(commandId, definition));
        }
    }

    /// <summary>
    /// Reads an integral bound.
    /// </summary>
    /// <param name="bound">The declared bound, or <c>null</c> when the parameter declares none.</param>
    /// <param name="roundUp">
    /// <c>true</c> for a minimum, where the bound is rounded up to the smallest integer that
    /// satisfies it; <c>false</c> for a maximum, where it is rounded down to the largest integer
    /// that satisfies it.
    /// </param>
    /// <param name="value">The bound as an exact integer.</param>
    /// <returns><c>true</c> when the bound was read or was absent; <c>false</c> when it is unreadable.</returns>
    /// <remarks>
    /// <para>
    /// The bound is read as an exact integer rather than a <see cref="double"/>. A 64-bit value
    /// does not survive a round trip through <see cref="double"/>: values beyond 2^53 lose their
    /// low-order digits, so a bound and a value differing by one collapse onto the same double
    /// and an out-of-range value was admitted.
    /// </para>
    /// <para>
    /// A bound that cannot be read at all is reported as a schema defect rather than parsed,
    /// because parsing it would fault the whole dispatch instead of blocking the one request.
    /// </para>
    /// </remarks>
    private static bool TryReadIntegerBound(string? bound, bool roundUp, out BigInteger? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(bound)) return true;

        try
        {
            string text = bound.Trim();

            // Plain integer form.
            if (BigInteger.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger parsed))
            {
                value = parsed;
                return true;
            }

            // An exponent form such as "1e30" is an exact integer the decimal type cannot always
            // hold. The arbitrary-precision reader rejects an exponent style outright, so the
            // digits are expanded instead, which keeps the comparison exact.
            if (TryExpandExponent(text, out string? expanded) && expanded is not null)
            {
                value = BigInteger.Parse(expanded, CultureInfo.InvariantCulture);
                return true;
            }

            // A fractional bound still constrains an integer value, and it constrains it in the
            // direction that keeps the bound meaningful: a minimum of 1.5 admits 2 and above, and
            // a maximum of 1.5 admits 1 and below.
            if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal real))
            {
                value = new BigInteger(roundUp ? decimal.Ceiling(real) : decimal.Floor(real));
                return true;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double approximate) &&
                double.IsFinite(approximate))
            {
                // The bound lies beyond the range a 64-bit value can occupy, so it is clamped to
                // the nearest edge of that range, in the direction that keeps the comparison
                // meaningful.
                if (approximate > long.MaxValue)
                {
                    value = roundUp ? new BigInteger(long.MaxValue) + 1 : new BigInteger(long.MaxValue);
                    return true;
                }

                if (approximate < long.MinValue)
                {
                    value = roundUp ? new BigInteger(long.MinValue) : new BigInteger(long.MinValue) - 1;
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            // This validator runs before the dispatcher's own error handling, so a throw here
            // would escape the dispatch call instead of blocking the one request.
            value = null;
            return false;
        }
    }

    /// <summary>
    /// Expands an exponent-form number into its exact plain integer digits.
    /// </summary>
    /// <returns><c>true</c> when the text is an exponent form that denotes an exact integer.</returns>
    private static bool TryExpandExponent(string text, out string? expanded)
    {
        expanded = null;
        int exponentIndex = text.IndexOfAny(['e', 'E']);
        if (exponentIndex <= 0 || exponentIndex == text.Length - 1) return false;

        if (!int.TryParse(text[(exponentIndex + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int exponent) ||
            exponent < 0)
        {
            return false;
        }

        string mantissa = text[..exponentIndex];
        bool negative = false;
        if (mantissa.StartsWith('-'))
        {
            negative = true;
            mantissa = mantissa[1..];
        }
        else if (mantissa.StartsWith('+'))
        {
            mantissa = mantissa[1..];
        }

        int pointIndex = mantissa.IndexOf('.');
        string digits;
        int scale;
        if (pointIndex >= 0)
        {
            digits = mantissa[..pointIndex] + mantissa[(pointIndex + 1)..];
            scale = mantissa.Length - pointIndex - 1;
        }
        else
        {
            digits = mantissa;
            scale = 0;
        }

        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit)) return false;

        int shift = exponent - scale;
        if (shift < 0 || shift > 4096 || digits.Length > 4096 - shift) return false;

        string magnitude = digits.TrimStart('0');
        if (magnitude.Length == 0)
        {
            expanded = "0";
            return true;
        }

        expanded = (negative ? "-" : string.Empty) + magnitude + new string('0', shift);
        return true;
    }

    /// <summary>Reads a floating-point bound, reporting an unreadable bound as a schema defect.</summary>
    private static bool TryReadDoubleBound(string? bound, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(bound)) return true;

        try
        {
            if (!double.TryParse(bound, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
                !double.IsFinite(parsed))
            {
                return false;
            }

            value = parsed;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            // This validator runs before the dispatcher's own error handling, so a throw here
            // would escape the dispatch call instead of blocking the one request.
            value = null;
            return false;
        }
    }

    /// <summary>Reports whether the payload carries a parameter under the declared name, ignoring case.</summary>
    private static bool ContainsParameter(JsonElement parameters, string name)
    {
        if (parameters.TryGetProperty(name, out _)) return true;
        foreach (JsonProperty property in parameters.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string OutsideRange(string commandId, CommandParameterDefinition definition) =>
        $"Parameter '{definition.Name}' for '{commandId}' is outside its supported range.";

    private static string UnreadableRange(string commandId, CommandParameterDefinition definition) =>
        $"Parameter '{definition.Name}' for '{commandId}' declares an unreadable range and cannot be validated.";

    private static string InvalidType(string commandId, CommandParameterDefinition definition, string expected) =>
        $"Parameter '{definition.Name}' for '{commandId}' must be {expected}.";
}
