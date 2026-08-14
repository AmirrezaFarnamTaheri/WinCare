namespace WinCare.Application.Plugins;

using System.Text.Json.Serialization;
using WinCare.CommandCatalog.Models;

/// <summary>
/// Represents the declarative manifest (wincare-plugin.json) for a WinCare plugin package.
/// </summary>
public sealed class PluginManifest
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("entryType")]
    public string EntryType { get; init; } = "Manifest"; // "Manifest" or "Assembly"

    [JsonPropertyName("assemblyFileName")]
    public string? AssemblyFileName { get; init; }

    [JsonPropertyName("pluginClassName")]
    public string? PluginClassName { get; init; }

    [JsonPropertyName("tools")]
    public List<PluginToolDefinition> Tools { get; init; } = new();
}

/// <summary>
/// Defines a tool command entry within a plugin manifest.
/// </summary>
public sealed class PluginToolDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    [JsonPropertyName("area")]
    public string Area { get; init; } = string.Empty;

    [JsonPropertyName("section")]
    public string Section { get; init; } = string.Empty;

    [JsonPropertyName("risk")]
    public string Risk { get; init; } = "Low";

    [JsonPropertyName("readOnly")]
    public bool ReadOnly { get; init; }

    [JsonPropertyName("executorType")]
    public string ExecutorType { get; init; } = "PowerShell";

    [JsonPropertyName("scriptPath")]
    public string ScriptPath { get; init; } = string.Empty;

    /// <summary>
    /// Converts this plugin tool definition into a core <see cref="CommandDefinition"/>.
    /// </summary>
    public CommandDefinition ToCommandDefinition(string pluginId)
    {
        Enum.TryParse<CommandRisk>(Risk, true, out var parsedRisk);
        return new CommandDefinition(
            Id: Id,
            Title: Title,
            Summary: Summary,
            Area: Area,
            Section: Section,
            Risk: parsedRisk,
            ReadOnly: ReadOnly,
            AdministratorAccess: AdministratorAccess.None,
            Restart: RestartExpectation.None,
            LegacySource: $"plugin:{pluginId}",
            MigrationStatus: MigrationStatus.NativeReady,
            Keywords: new[] { Id, Title, Area, Section, pluginId }
        );
    }
}
