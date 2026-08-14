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
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _version = string.Empty;
    private string _author = string.Empty;
    private string _description = string.Empty;
    private string _category = string.Empty;
    private string _entryType = "Manifest";
    private string? _assemblyFileName;
    private string? _pluginClassName;

    /// <summary>Unique plugin package ID.</summary>
    [JsonPropertyName("id")]
    public string Id { get => _id; init => _id = value; }

    /// <summary>Display name of the plugin.</summary>
    [JsonPropertyName("name")]
    public string Name { get => _name; init => _name = value; }

    /// <summary>Version string of the plugin.</summary>
    [JsonPropertyName("version")]
    public string Version { get => _version; init => _version = value; }

    /// <summary>Author or organization publishing the plugin.</summary>
    [JsonPropertyName("author")]
    public string Author { get => _author; init => _author = value; }

    /// <summary>Summary description of the plugin.</summary>
    [JsonPropertyName("description")]
    public string Description { get => _description; init => _description = value; }

    /// <summary>Category bucket for the plugin.</summary>
    [JsonPropertyName("category")]
    public string Category { get => _category; init => _category = value; }

    /// <summary>Entry type ("Manifest" or "Assembly").</summary>
    [JsonPropertyName("entryType")]
    public string EntryType
    {
        get => _entryType;
        init => _entryType = string.IsNullOrWhiteSpace(value) ? "Manifest" : value;
    }

    /// <summary>Optional assembly filename for binary C# plugins.</summary>
    [JsonPropertyName("assemblyFileName")]
    public string? AssemblyFileName
    {
        get => _assemblyFileName;
        init
        {
            _assemblyFileName = value;
            if (!string.IsNullOrWhiteSpace(value) && _entryType == "Manifest")
            {
                _entryType = "Assembly";
            }
        }
    }

    /// <summary>Optional assembly entry alias.</summary>
    [JsonPropertyName("assemblyEntry")]
    public string? AssemblyEntry
    {
        get => _assemblyFileName;
        init
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _assemblyFileName = value;
                if (_entryType == "Manifest")
                {
                    _entryType = "Assembly";
                }
            }
        }
    }

    /// <summary>Optional plugin class name for binary C# plugins.</summary>
    [JsonPropertyName("pluginClassName")]
    public string? PluginClassName { get => _pluginClassName; init => _pluginClassName = value; }

    /// <summary>Optional plugin class alias.</summary>
    [JsonPropertyName("entryClass")]
    public string? EntryClass { get => _pluginClassName; init => _pluginClassName = value; }

    /// <summary>Declared security capabilities and permissions (e.g. filesystem.read, process.spawn).</summary>
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
    private string _id = string.Empty;
    private string _title = string.Empty;
    private string _summary = string.Empty;
    private string _area = "Utilities";
    private string _section = "General";
    private string _risk = "Low";
    private string _administratorAccess = "No";
    private string _restart = "No";
    private string _executorType = "Script";
    private string _scriptPath = string.Empty;
    private bool _readOnly;

    /// <summary>Command ID.</summary>
    [JsonPropertyName("id")]
    public string Id { get => _id; init => _id = value; }

    /// <summary>Display title.</summary>
    [JsonPropertyName("title")]
    public string Title
    {
        get => _title;
        init
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _title = value;
            }
        }
    }

    /// <summary>Name alias for title.</summary>
    [JsonPropertyName("name")]
    public string? NameAlias
    {
        get => _title;
        init
        {
            if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(_title))
            {
                _title = value;
            }
        }
    }

    /// <summary>Summary description.</summary>
    [JsonPropertyName("summary")]
    public string Summary
    {
        get => _summary;
        init
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _summary = value;
            }
        }
    }

    /// <summary>Description alias for summary.</summary>
    [JsonPropertyName("description")]
    public string? DescriptionAlias
    {
        get => _summary;
        init
        {
            if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(_summary))
            {
                _summary = value;
            }
        }
    }

    /// <summary>Area bucket.</summary>
    [JsonPropertyName("area")]
    public string Area
    {
        get => _area;
        init => _area = string.IsNullOrWhiteSpace(value) ? "Utilities" : value;
    }

    /// <summary>Section within area.</summary>
    [JsonPropertyName("section")]
    public string Section
    {
        get => _section;
        init => _section = string.IsNullOrWhiteSpace(value) ? "General" : value;
    }

    /// <summary>Declared risk tier.</summary>
    [JsonPropertyName("risk")]
    public string Risk
    {
        get => _risk;
        init
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _risk = value;
            }
        }
    }

    /// <summary>RiskLevel alias for risk tier.</summary>
    [JsonPropertyName("riskLevel")]
    public string? RiskLevelAlias
    {
        get => _risk;
        init
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                if (string.Equals(value, "ReadOnly", StringComparison.OrdinalIgnoreCase))
                {
                    _risk = "ReadOnly";
                    _readOnly = true;
                }
                else if (string.Equals(value, "Mutating", StringComparison.OrdinalIgnoreCase))
                {
                    _risk = "Moderate";
                    _readOnly = false;
                }
                else if (string.Equals(value, "Elevated", StringComparison.OrdinalIgnoreCase))
                {
                    _risk = "High";
                    _readOnly = false;
                    _administratorAccess = "Required";
                }
                else
                {
                    _risk = value;
                }
            }
        }
    }

    /// <summary>Declared administrator access requirement.</summary>
    [JsonPropertyName("administratorAccess")]
    public string AdministratorAccess
    {
        get => _administratorAccess;
        init => _administratorAccess = string.IsNullOrWhiteSpace(value) ? "No" : value;
    }

    /// <summary>Declared restart expectation.</summary>
    [JsonPropertyName("restart")]
    public string Restart
    {
        get => _restart;
        init => _restart = string.IsNullOrWhiteSpace(value) ? "No" : value;
    }

    /// <summary>Whether command is read-only.</summary>
    [JsonPropertyName("readOnly")]
    public bool ReadOnly
    {
        get => _readOnly;
        init => _readOnly = value;
    }

    /// <summary>Mutation type alias.</summary>
    [JsonPropertyName("mutationType")]
    public string? MutationType
    {
        get => _readOnly ? "ReadOnly" : "Mutation";
        init => _readOnly = string.Equals(value, "ReadOnly", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Executor engine type.</summary>
    [JsonPropertyName("executorType")]
    public string ExecutorType
    {
        get => _executorType;
        init
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _executorType = value;
            }
        }
    }

    /// <summary>ExecutionType alias for executor type.</summary>
    [JsonPropertyName("executionType")]
    public string? ExecutionTypeAlias
    {
        get => _executorType;
        init
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _executorType = value;
            }
        }
    }

    /// <summary>Relative script path.</summary>
    [JsonPropertyName("scriptPath")]
    public string ScriptPath
    {
        get => _scriptPath;
        init
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _scriptPath = value;
            }
        }
    }

    /// <summary>Script path alias.</summary>
    [JsonPropertyName("script")]
    public string? ScriptAlias
    {
        get => _scriptPath;
        init
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _scriptPath = value;
            }
        }
    }

    /// <summary>Optional target core command ID for built-in or alias plugin commands.</summary>
    [JsonPropertyName("aliasOf")]
    public string? AliasOf { get; init; }

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

        var effectiveReadOnly = ReadOnly || parsedRisk == CommandRisk.ReadOnly;

        return new CommandDefinition(
            Id: Id,
            Title: string.IsNullOrWhiteSpace(Title) ? Id : Title,
            Summary: string.IsNullOrWhiteSpace(Summary) ? Title : Summary,
            Area: string.IsNullOrWhiteSpace(Area) ? "Utilities" : Area,
            Section: string.IsNullOrWhiteSpace(Section) ? "General" : Section,
            Risk: parsedRisk,
            ReadOnly: effectiveReadOnly,
            AdministratorAccess: parsedAdmin,
            Restart: parsedRestart,
            LegacySource: $"plugin:{pluginId}",
            MigrationStatus: MigrationStatus.BehaviorVerified,
            Keywords: new[] { Id, Title, Area, Section, pluginId }
        );
    }
}

