using System.Text.Json;

namespace WinCare.Domain.Commands;

/// <summary>
/// Typed request into the native command plane.
/// </summary>
/// <param name="CommandId">Stable catalog command ID.</param>
/// <param name="Parameters">JSON object parameters.</param>
/// <param name="Apply">Whether to apply mutations or preview them.</param>
/// <param name="CorrelationId">Correlation ID for dispatch, telemetry, and recovery.</param>
/// <param name="Approval">Optional approved mutation plan for mutative requests.</param>
public sealed record CommandRequest(
    string CommandId,
    JsonElement Parameters,
    bool Apply,
    Guid CorrelationId,
    ApprovedMutationPlan? Approval = null)
{
    /// <summary>
    /// Creates a non-mutative preview request.
    /// </summary>
    public static CommandRequest Preview(string commandId, JsonElement parameters) =>
        new(commandId, parameters, Apply: false, Guid.NewGuid());

    /// <summary>
    /// Creates a non-mutative preview request without parameters.
    /// </summary>
    public static CommandRequest Preview(string commandId) =>
        new(commandId, JsonSerializer.SerializeToElement(new { }), Apply: false, Guid.NewGuid());

    /// <summary>
    /// Creates a mutative execution request.
    /// </summary>
    public static CommandRequest Execute(string commandId, JsonElement parameters, ApprovedMutationPlan? approval = null) =>
        new(commandId, parameters, Apply: true, Guid.NewGuid(), approval);
}
