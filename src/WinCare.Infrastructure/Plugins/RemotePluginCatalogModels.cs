using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WinCare.Infrastructure.Plugins;

public class RemotePluginCatalog
{
    [JsonPropertyName("catalogVersion")]
    public string CatalogVersion { get; set; } = "1.0";

    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("plugins")]
    public List<RemotePluginItem> Plugins { get; set; } = new();
}

public class RemotePluginItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("iconUrl")]
    public string IconUrl { get; set; } = string.Empty;

    [JsonPropertyName("packageUrl")]
    public string PackageUrl { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = "General";

    [JsonPropertyName("permissions")]
    public List<string> Permissions { get; set; } = new();

    [JsonPropertyName("commandsProvided")]
    public List<string> CommandsProvided { get; set; } = new();

    [JsonPropertyName("publishedDate")]
    public DateTime PublishedDate { get; set; } = DateTime.UtcNow;
}
