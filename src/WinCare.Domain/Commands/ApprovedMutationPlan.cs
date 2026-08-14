using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WinCare.Domain.Commands;

/// <summary>
/// Bound approval descriptor linking previewed mutation plan to an execution request.
/// </summary>
/// <param name="PlanId">Unique approved plan identifier.</param>
/// <param name="CommandId">Catalog command ID.</param>
/// <param name="ParametersDigest">SHA256 digest of canonical parameter payload.</param>
/// <param name="ApprovedAtUtc">Timestamp when approval was recorded.</param>
/// <param name="CorrelationId">Correlation ID of preview request.</param>
public sealed record ApprovedMutationPlan(
    string PlanId,
    string CommandId,
    string ParametersDigest,
    DateTimeOffset ApprovedAtUtc,
    Guid CorrelationId)
{
    /// <summary>
    /// Creates an approved mutation plan with a canonical parameter digest and a new correlation ID.
    /// </summary>
    public static ApprovedMutationPlan Create(string commandId, JsonElement parameters) =>
        Create(commandId, parameters, Guid.NewGuid());

    /// <summary>
    /// Creates an approved mutation plan with a canonical parameter digest.
    /// </summary>
    public static ApprovedMutationPlan Create(string commandId, JsonElement parameters, Guid correlationId)
    {
        string digest = ComputeCanonicalDigest(parameters);
        return new ApprovedMutationPlan(
            "AMP-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            commandId,
            digest,
            DateTimeOffset.UtcNow,
            correlationId);
    }

    /// <summary>
    /// Creates an approved mutation plan from raw JSON string.
    /// </summary>
    public static ApprovedMutationPlan Create(string commandId, string parametersJson, Guid correlationId)
    {
        using JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson);
        return Create(commandId, doc.RootElement, correlationId);
    }

    /// <summary>
    /// Validates whether the approval matches the target command ID and parameter payload, is non-empty, and is within the expiration window.
    /// </summary>
    public bool IsValid(string commandId, JsonElement parameters, Guid? expectedCorrelationId = null, TimeSpan? maxAge = null)
    {
        if (string.IsNullOrWhiteSpace(PlanId)) return false;
        if (CorrelationId == Guid.Empty) return false;
        if (!string.Equals(CommandId, commandId, StringComparison.OrdinalIgnoreCase)) return false;
        if (expectedCorrelationId.HasValue && CorrelationId != expectedCorrelationId.Value) return false;
        TimeSpan age = DateTimeOffset.UtcNow - ApprovedAtUtc;
        if (age < TimeSpan.Zero || age > (maxAge ?? TimeSpan.FromMinutes(15))) return false;
        string expectedDigest = ComputeCanonicalDigest(parameters);
        return string.Equals(ParametersDigest, expectedDigest, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Computes a canonical SHA256 digest for a JSON element, sorting property keys recursively.
    /// </summary>
    public static string ComputeCanonicalDigest(JsonElement element)
    {
        string canonicalJson = CanonicalizeJson(element);
        byte[] bytes = Encoding.UTF8.GetBytes(canonicalJson);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Canonicalizes a JSON element by recursively sorting object property keys and stripping unnecessary whitespace.
    /// </summary>
    public static string CanonicalizeJson(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var sortedProps = element.EnumerateObject()
                    .OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => $"{JsonSerializer.Serialize(p.Name)}:{CanonicalizeJson(p.Value)}");
                return "{" + string.Join(",", sortedProps) + "}";
            case JsonValueKind.Array:
                var items = element.EnumerateArray().Select(CanonicalizeJson);
                return "[" + string.Join(",", items) + "]";
            case JsonValueKind.String:
                return JsonSerializer.Serialize(element.GetString());
            case JsonValueKind.Number:
                return element.GetRawText();
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return "null";
            default:
                return "{}";
        }
    }
}
