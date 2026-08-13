using System.Security.Cryptography;
using System.Text;

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
    /// Creates an approved mutation plan for a command request.
    /// </summary>
    public static ApprovedMutationPlan Create(string commandId, string parametersJson, Guid correlationId)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(parametersJson ?? "{}");
        byte[] hash = SHA256.HashData(bytes);
        string digest = Convert.ToHexString(hash);
        return new ApprovedMutationPlan(
            "AMP-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            commandId,
            digest,
            DateTimeOffset.UtcNow,
            correlationId);
    }
}
