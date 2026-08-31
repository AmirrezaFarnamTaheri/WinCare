// Source-Driven Development Citation:
// Pattern: System.Text.Json compile-time source generation for .NET 8
// Source: https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation
// "Source generators generate code at compile time, improving performance and eliminating reflection."

namespace WinCare.Domain.Serialization;

using System.Text.Json.Serialization;
using WinCare.Domain.Activity;
using WinCare.Domain.Commands;

/// <summary>
/// Compile-time source-generated JSON serializer context for WinCare domain contracts.
/// Eliminates runtime reflection overhead and enables zero-allocation serialization paths.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Default,
    Converters = [typeof(JsonStringEnumConverter<ActivityState>), typeof(JsonStringEnumConverter<CommandResultStatus>)])]
[JsonSerializable(typeof(ActivityRecord))]
[JsonSerializable(typeof(ActivityRecord[]))]
[JsonSerializable(typeof(System.Collections.Generic.List<ActivityRecord>))]
[JsonSerializable(typeof(ApprovedMutationPlan))]
[JsonSerializable(typeof(CommandRequest))]
[JsonSerializable(typeof(CommandResult))]
[JsonSerializable(typeof(ActivityState))]
[JsonSerializable(typeof(CommandResultStatus))]
public sealed partial class WinCareDomainJsonContext : JsonSerializerContext
{
}
