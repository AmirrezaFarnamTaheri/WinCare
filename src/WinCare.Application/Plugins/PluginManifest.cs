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
    private string? _targetFramework;

    // Raw, per-spelling inputs so conflicting alias spellings (e.g. both `assemblyEntry` and
    // `assemblyFileName` with different values) can be rejected instead of silently letting
    // the last-written value win. See ValidateAliasConsistency.
    private string? _rawAssemblyFileName;
    private string? _rawAssemblyEntry;
    private string? _rawPluginClassName;
    private string? _rawEntryClass;

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
            _rawAssemblyFileName = value;
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
            _rawAssemblyEntry = value;
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
    public string? PluginClassName
    {
        get => _pluginClassName;
        init
        {
            _rawPluginClassName = value;
            _pluginClassName = value;
        }
    }

    /// <summary>Target framework required by a compiled assembly plugin.</summary>
    [JsonPropertyName("targetFramework")]
    public string? TargetFramework { get => _targetFramework; init => _targetFramework = value; }

    /// <summary>Optional plugin class alias.</summary>
    [JsonPropertyName("entryClass")]
    public string? EntryClass
    {
        get => _pluginClassName;
        init
        {
            _rawEntryClass = value;
            _pluginClassName = value;
        }
    }

    /// <summary>Declared security capabilities and permissions (e.g. filesystem.read, process.spawn).</summary>
    [JsonPropertyName("declaredCapabilities")]
    public List<string> DeclaredCapabilities { get; init; } = new();

    /// <summary>List of tool command definitions provided by the plugin.</summary>
    [JsonPropertyName("tools")]
    public List<PluginToolDefinition> Tools { get; init; } = new();

    /// <summary>
    /// Rejects manifests whose alias spellings conflict, and recurses into each tool
    /// definition for the same check. Called by the loader after deserialization.
    /// </summary>
    public void ValidateAliasConsistency()
    {
        RequireAgreement("assemblyEntry", _rawAssemblyEntry, "assemblyFileName", _rawAssemblyFileName);
        RequireAgreement("entryClass", _rawEntryClass, "pluginClassName", _rawPluginClassName);

        foreach (PluginToolDefinition tool in Tools)
        {
            tool.ValidateAliasConsistency();
        }
    }

    private static void RequireAgreement(string aliasName, string? aliasValue, string canonicalName, string? canonicalValue)
    {
        if (string.IsNullOrWhiteSpace(aliasValue) || string.IsNullOrWhiteSpace(canonicalValue))
        {
            return;
        }

        if (!string.Equals(aliasValue, canonicalValue, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException(
                $"Conflicting plugin metadata: '{aliasName}' ('{aliasValue}') and '{canonicalName}' ('{canonicalValue}') disagree.");
        }
    }
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

    // Raw, per-spelling inputs for alias conflict detection (see ValidateAliasConsistency).
    private string? _rawTitle;
    private string? _rawName;
    private string? _rawSummary;
    private string? _rawDescription;
    private string? _rawRisk;
    private string? _rawRiskLevel;
    private string? _rawExecutorType;
    private string? _rawExecutionType;
    private string? _rawScriptPath;
    private string? _rawScript;

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
            _rawTitle = value;
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
            _rawName = value;
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
            _rawSummary = value;
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
            _rawDescription = value;
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
            _rawRisk = value;
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
            _rawRiskLevel = value;
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
            _rawExecutorType = value;
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
            _rawExecutionType = value;
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
            _rawScriptPath = value;
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
            _rawScript = value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                _scriptPath = value;
            }
        }
    }

    /// <summary>Optional target core command ID for built-in or alias plugin commands.</summary>
    [JsonPropertyName("aliasOf")]
    public string? AliasOf { get; init; }

    /// <summary>Rejects conflicting alias spellings for this tool definition.</summary>
    public void ValidateAliasConsistency()
    {
        RequireAgreement("name", _rawName, "title", _rawTitle);
        RequireAgreement("description", _rawDescription, "summary", _rawSummary);
        RequireAgreement("executionType", _rawExecutionType, "executorType", _rawExecutorType);
        RequireAgreement("script", _rawScript, "scriptPath", _rawScriptPath);

        // `riskLevel` uses legacy enum-like spellings that map onto the canonical `risk`
        // tier; compare effective values rather than raw spellings.
        if (!string.IsNullOrWhiteSpace(_rawRisk) && !string.IsNullOrWhiteSpace(_rawRiskLevel))
        {
            string effectiveRiskLevel = MapRiskLevelAlias(_rawRiskLevel);
            if (!string.Equals(_rawRisk, effectiveRiskLevel, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException(
                    $"Conflicting plugin metadata: 'risk' ('{_rawRisk}') and 'riskLevel' ('{_rawRiskLevel}') disagree.");
            }
        }
    }

    private static string MapRiskLevelAlias(string value) => value.ToLowerInvariant() switch
    {
        "readonly" => "ReadOnly",
        "mutating" => "Moderate",
        "elevated" => "High",
        _ => value,
    };

    private static void RequireAgreement(string aliasName, string? aliasValue, string canonicalName, string? canonicalValue)
    {
        if (string.IsNullOrWhiteSpace(aliasValue) || string.IsNullOrWhiteSpace(canonicalValue))
        {
            return;
        }

        if (!string.Equals(aliasValue, canonicalValue, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException(
                $"Conflicting plugin metadata: '{aliasName}' ('{aliasValue}') and '{canonicalName}' ('{canonicalValue}') disagree.");
        }
    }

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
