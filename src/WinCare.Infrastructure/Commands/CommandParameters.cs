using System.Globalization;
using System.Text.Json;

namespace WinCare.Infrastructure.Commands;

internal sealed class CommandParameters
{
    private const int MaxStringCharacters = 256 * 1024;
    private const int MaxArrayItems = 256;
    private const int MaxArrayItemCharacters = 32 * 1024;
    private readonly JsonElement _root;

    public CommandParameters(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Command parameters must be a JSON object.", nameof(root));
        }
        _root = root;
    }

    public bool Contains(string name) => _root.TryGetProperty(name, out _);

    public string String(string name, string defaultValue = "")
    {
        if (!_root.TryGetProperty(name, out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return defaultValue;
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new CommandParameterException(name, $"Parameter '{name}' must be a string.");
        }
        string result = value.GetString() ?? defaultValue;
        if (result.Length > MaxStringCharacters)
        {
            throw new CommandParameterException(name, $"Parameter '{name}' exceeds the {MaxStringCharacters}-character limit.");
        }
        return result;
    }

    public string RequiredString(string name)
    {
        string value = String(name).Trim();
        if (value.Length == 0)
        {
            throw new CommandParameterException(name, $"Parameter '{name}' is required.");
        }
        return value;
    }

    public int Int32(string name, int defaultValue = 0, int? min = null, int? max = null)
    {
        if (!_root.TryGetProperty(name, out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return defaultValue;
        }

        int parsed;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out parsed))
        {
            return Bound(name, parsed, min, max);
        }
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
        {
            return Bound(name, parsed, min, max);
        }
        throw new CommandParameterException(name, $"Parameter '{name}' must be an integer.");
    }

    public long Int64(string name, long defaultValue = 0, long? min = null, long? max = null)
    {
        if (!_root.TryGetProperty(name, out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return defaultValue;
        }
        long parsed;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out parsed))
        {
            return Bound(name, parsed, min, max);
        }
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
        {
            return Bound(name, parsed, min, max);
        }
        throw new CommandParameterException(name, $"Parameter '{name}' must be an integer.");
    }

    public double Double(string name, double defaultValue = 0, double? min = null, double? max = null)
    {
        if (!_root.TryGetProperty(name, out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return defaultValue;
        }
        double parsed;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out parsed))
        {
            return Bound(name, parsed, min, max);
        }
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            return Bound(name, parsed, min, max);
        }
        throw new CommandParameterException(name, $"Parameter '{name}' must be a number.");
    }

    public bool Boolean(string name, bool defaultValue = false)
    {
        if (!_root.TryGetProperty(name, out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return defaultValue;
        }
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }
        if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool parsed))
        {
            return parsed;
        }
        throw new CommandParameterException(name, $"Parameter '{name}' must be true or false.");
    }

    public IReadOnlyList<string> StringArray(string name)
    {
        if (!_root.TryGetProperty(name, out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Array.Empty<string>();
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            string single = value.GetString() ?? string.Empty;
            ValidateArrayItem(name, single);
            return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single };
        }
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new CommandParameterException(name, $"Parameter '{name}' must be a string or array of strings.");
        }
        if (value.GetArrayLength() > MaxArrayItems)
        {
            throw new CommandParameterException(name, $"Parameter '{name}' exceeds the {MaxArrayItems}-item limit.");
        }
        var items = new List<string>(value.GetArrayLength());
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new CommandParameterException(name, $"Parameter '{name}' must contain only strings.");
            }
            string text = item.GetString() ?? string.Empty;
            ValidateArrayItem(name, text);
            if (!string.IsNullOrWhiteSpace(text))
            {
                items.Add(text);
            }
        }
        return items;
    }

    private static void ValidateArrayItem(string name, string value)
    {
        if (value.Length > MaxArrayItemCharacters)
        {
            throw new CommandParameterException(name, $"Parameter '{name}' contains a value longer than {MaxArrayItemCharacters} characters.");
        }
    }

    public JsonElement Element(string name)
    {
        if (!_root.TryGetProperty(name, out JsonElement value))
        {
            throw new CommandParameterException(name, $"Parameter '{name}' is required.");
        }
        return value.Clone();
    }

    public JsonElement? OptionalElement(string name) =>
        _root.TryGetProperty(name, out JsonElement value) ? value.Clone() : null;

    public DateTimeOffset DateTimeOffset(string name, DateTimeOffset defaultValue)
    {
        string raw = String(name);
        if (raw.Length == 0)
        {
            return defaultValue;
        }
        if (!System.DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset value))
        {
            throw new CommandParameterException(name, $"Parameter '{name}' must be an ISO-8601 date/time.");
        }
        return value;
    }

    private static int Bound(string name, int value, int? min, int? max)
    {
        if (min.HasValue && value < min.Value || max.HasValue && value > max.Value)
        {
            throw new CommandParameterException(name, $"Parameter '{name}' is outside the allowed range.");
        }
        return value;
    }

    private static long Bound(string name, long value, long? min, long? max)
    {
        if (min.HasValue && value < min.Value || max.HasValue && value > max.Value)
        {
            throw new CommandParameterException(name, $"Parameter '{name}' is outside the allowed range.");
        }
        return value;
    }

    private static double Bound(string name, double value, double? min, double? max)
    {
        if (min.HasValue && value < min.Value || max.HasValue && value > max.Value)
        {
            throw new CommandParameterException(name, $"Parameter '{name}' is outside the allowed range.");
        }
        return value;
    }
}

internal sealed class CommandParameterException(string parameterName, string message) : Exception(message)
{
    public string ParameterName { get; } = parameterName;
}
