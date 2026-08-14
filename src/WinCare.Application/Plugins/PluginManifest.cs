namespace WinCare.Application.Plugins;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using WinCare.CommandCatalog.Models;

/// <summary>
/// Represents the declarative manifest (wincare-plugin.json) for a WinCare plugin package.
/// </summary>
public sealed class PluginManifest
{
    /// <summary>Unique plugin package ID.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name of the plugin.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Version string of the plugin.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    /// <summary>Author or organization publishing the plugin.</summary>
    [JsonPropertyName("author")]
    public string Author { get; init; } = string.Empty;

    /// <summary>Summary description of the plugin.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>Category bucket for the plugin.</summary>
    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    /// <summary>Entry type ("Manifest" or "Assembly").</summary>
    [JsonPropertyName("entryType")]
    public string EntryType { get; init; } = "Manifest";

    /// <summary>Optional assembly filename for binary C# plugins.</summary>
    [JsonPropertyName("assemblyFileName")]
    public string? AssemblyFileName { get; init; }

    /// <summary>Optional plugin class name for binary C# plugins.</summary>
    [JsonPropertyName("pluginClassName")]
    public string? PluginClassName { get; init; }

    /// <summary>Declared security capabilities & permissions (e.g. filesystem.read, process.spawn).</summary>
    [JsonPropertyName("declaredCapabilities")]
    public List<string> DeclaredCapabilities { get; init; } = new();

    /// <summary>List of tool command definitions provided by the plugin.</summary>
    [JsonPropertyName("tools")]
    public List<PluginToolDefinition> Tools { get; init; } = new();
}

/// <summary>
/// Defines a tool command entry within a plugin manifest.
/// </summary>
public sealed class PluginToolDefinition
{
    /// <summary>Command ID.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Display title.</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Summary description.</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    /// <summary>Area bucket.</summary>
    [JsonPropertyName("area")]
    public string Area { get; init; } = string.Empty;

    /// <summary>Section within area.</summary>
    [JsonPropertyName("section")]
    public string Section { get; init; } = string.Empty;

    /// <summary>Declared risk tier.</summary>
    [JsonPropertyName("risk")]
    public string Risk { get; init; } = "Low";

    /// <summary>Declared administrator access requirement.</summary>
    [JsonPropertyName("administratorAccess")]
    public string AdministratorAccess { get; init; } = "No";

    /// <summary>Declared restart expectation.</summary>
    [JsonPropertyName("restart")]
    public string Restart { get; init; } = "No";

    /// <summary>Whether command is read-only.</summary>
    [JsonPropertyName("readOnly")]
    public bool ReadOnly { get; init; }

    /// <summary>Executor engine type.</summary>
    [JsonPropertyName("executorType")]
    public string ExecutorType { get; init; } = "PowerShell";

    /// <summary>Relative script path.</summary>
    [JsonPropertyName("scriptPath")]
    public string ScriptPath { get; init; } = string.Empty;

    /// <summary>
    /// Converts this plugin tool definition into a core <see cref="CommandDefinition"/>, failing closed on invalid metadata.
    /// </summary>
    public CommandDefinition ToCommandDefinition(string pluginId)
    {
        if (!Enum.TryParse<CommandRisk>(Risk, true, out var parsedRisk))
        {
            throw new FormatException($"Invalid declared Risk tier '{Risk}' in plugin '{pluginId}'. Must be ReadOnly, Low, Moderate, High, or Critical.");
        }

        if (!Enum.TryParse<AdministratorAccess>(AdministratorAccess, true, out var parsedAdmin))
        {
            throw new FormatException($"Invalid declared AdministratorAccess '{AdministratorAccess}' in plugin '{pluginId}'. Must be No, MayBeRequired, or Required.");
        }

        if (!Enum.TryParse<RestartExpectation>(Restart, true, out var parsedRestart))
        {
            throw new FormatException($"Invalid declared Restart expectation '{Restart}' in plugin '{pluginId}'. Must be No, MayBeRequired, or Required.");
        }

        return new CommandDefinition(
            Id: Id,
            Title: Title,
            Summary: Summary,
            Area: Area,
            Section: Section,
            Risk: parsedRisk,
            ReadOnly: ReadOnly,
            AdministratorAccess: parsedAdmin,
            Restart: parsedRestart,
            LegacySource: $"plugin:{pluginId}",
            MigrationStatus: MigrationStatus.BehaviorVerified,
            Keywords: new[] { Id, Title, Area, Section, pluginId }
        );
    }
}
