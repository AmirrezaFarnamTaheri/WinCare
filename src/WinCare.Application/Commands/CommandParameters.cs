namespace WinCare.Application.Commands;

using System;
using System.Text.Json;

/// <summary>
/// Parameter container for WinCare 4.0 subsystem command planning and execution.
/// Supports strongly-typed value extraction and canonical JSON document resolution.
/// </summary>
public sealed class SubsystemCommandParameters
{
    private static readonly JsonElement EmptyElement;

    static SubsystemCommandParameters()
    {
        using var doc = JsonDocument.Parse("{}");
        EmptyElement = doc.RootElement.Clone();
    }

    /// <summary>
    /// Gets the underlying raw <see cref="JsonElement"/> representing parameter values.
    /// </summary>
    public JsonElement RawElement { get; }

    /// <summary>
    /// Initializes an empty instance of <see cref="SubsystemCommandParameters"/>.
    /// </summary>
    public SubsystemCommandParameters()
    {
        RawElement = EmptyElement;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SubsystemCommandParameters"/> wrapping an existing <see cref="JsonElement"/>.
    /// </summary>
    /// <param name="element">The JSON element containing parameter attributes.</param>
    public SubsystemCommandParameters(JsonElement element)
    {
        if (element.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object))
        {
            throw new ArgumentException("Command parameters must be a JSON object.", nameof(element));
        }

        RawElement = element.ValueKind == JsonValueKind.Undefined ? EmptyElement : element.Clone();
    }

    /// <summary>
    /// Gets a shared empty instance of <see cref="SubsystemCommandParameters"/>.
    /// </summary>
    public static SubsystemCommandParameters Empty => new(EmptyElement);

    /// <summary>
    /// Parses a JSON string into a structured <see cref="SubsystemCommandParameters"/> container.
    /// </summary>
    /// <param name="json">The JSON payload string.</param>
    /// <returns>A parsed <see cref="SubsystemCommandParameters"/> instance.</returns>
    /// <exception cref="JsonException">The input is malformed or is not a JSON object.</exception>
    public static SubsystemCommandParameters FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Command parameters must be a JSON object.");
        }

        return new SubsystemCommandParameters(doc.RootElement);
    }

    /// <summary>
    /// Determines whether the parameters payload contains the specified property name.
    /// </summary>
    public bool ContainsKey(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName)) return false;
        return RawElement.ValueKind == JsonValueKind.Object && RawElement.TryGetProperty(propertyName, out _);
    }

    /// <summary>
    /// Attempts to extract a string parameter by key name.
    /// </summary>
    public bool TryGetString(string propertyName, out string? value)
    {
        value = null;
        if (!string.IsNullOrWhiteSpace(propertyName) && RawElement.ValueKind == JsonValueKind.Object &&
            RawElement.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.String)
            {
                value = prop.GetString();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to extract an integer or long parameter by key name.
    /// </summary>
    public bool TryGetInt64(string propertyName, out long value)
    {
        value = 0;
        if (!string.IsNullOrWhiteSpace(propertyName) && RawElement.ValueKind == JsonValueKind.Object &&
            RawElement.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out value))
            {
                return true;
            }

        }

        return false;
    }

    /// <summary>
    /// Attempts to extract a boolean parameter by key name.
    /// </summary>
    public bool TryGetBoolean(string propertyName, out bool value)
    {
        value = false;
        if (!string.IsNullOrWhiteSpace(propertyName) && RawElement.ValueKind == JsonValueKind.Object &&
            RawElement.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) { value = true; return true; }
            if (prop.ValueKind == JsonValueKind.False) { value = false; return true; }

        }

        return false;
    }
}
