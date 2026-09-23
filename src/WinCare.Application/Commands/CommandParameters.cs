namespace WinCare.Application.Commands;

using System;
using System.Text.Json;

/// <summary>
/// Parameter container for WinCare 4.0 subsystem command planning and execution.
/// </summary>
public sealed class CommandParameters
{
    private static readonly JsonElement EmptyElement;

    static CommandParameters()
    {
        using var doc = JsonDocument.Parse("{}");
        EmptyElement = doc.RootElement.Clone();
    }

    public JsonElement RawElement { get; }

    public CommandParameters()
    {
        RawElement = EmptyElement;
    }

    public CommandParameters(JsonElement element)
    {
        RawElement = element;
    }

    public static CommandParameters Empty => new(EmptyElement);

    public static CommandParameters FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        using var doc = JsonDocument.Parse(json);
        return new CommandParameters(doc.RootElement.Clone());
    }

    public bool TryGetString(string propertyName, out string? value)
    {
        value = null;
        if (RawElement.ValueKind == JsonValueKind.Object &&
            RawElement.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString();
            return true;
        }

        return false;
    }
}
