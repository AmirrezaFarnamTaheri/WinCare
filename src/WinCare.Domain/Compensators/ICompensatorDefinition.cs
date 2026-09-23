namespace WinCare.Domain.Compensators;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Defines a reversible compensator contract for executing compensating rollback transactions.
/// </summary>
public interface ICompensatorDefinition
{
    /// <summary>
    /// Gets the unique identifier of this compensator.
    /// </summary>
    string CompensatorId { get; }

    /// <summary>
    /// Reverts the mutation using the captured reverse state payload.
    /// </summary>
    /// <param name="snapshotData">The reverse state JSON data captured during execution preview.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if compensation succeeded; otherwise, <c>false</c>.</returns>
    Task<bool> CompensateAsync(JsonElement snapshotData, CancellationToken cancellationToken);
}

/// <summary>
/// Immutable state snapshot captured during pre-mutation preview to enable atomic compensation.
/// </summary>
public sealed record StateSnapshot(
    string SnapshotId,
    string CommandId,
    DateTime CreatedUtc,
    JsonElement ForwardState,
    JsonElement ReverseState,
    bool IsReverted);
