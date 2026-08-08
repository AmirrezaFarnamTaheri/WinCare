using System.Text.Json.Serialization;
using WinCare.CommandCatalog.Models;

namespace WinCare.CommandCatalog;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(RemediationCatalogDocument))]
[JsonSerializable(typeof(PresetCatalogDocument))]
[JsonSerializable(typeof(RemediationRule[]))]
[JsonSerializable(typeof(PresetDefinition[]))]
internal sealed partial class RemediationCatalogJsonContext : JsonSerializerContext;
