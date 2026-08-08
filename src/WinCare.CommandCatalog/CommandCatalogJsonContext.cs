using System.Text.Json.Serialization;
using WinCare.CommandCatalog.Models;

namespace WinCare.CommandCatalog;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CommandCatalogDocument))]
internal sealed partial class CommandCatalogJsonContext : JsonSerializerContext;
