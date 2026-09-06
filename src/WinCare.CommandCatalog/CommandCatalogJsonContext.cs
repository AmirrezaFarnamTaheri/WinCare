using System.Text.Json.Serialization;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.CommandCatalog;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CommandCatalogDocument))]
[JsonSerializable(typeof(RiskTier))]
internal sealed partial class CommandCatalogJsonContext : JsonSerializerContext;
