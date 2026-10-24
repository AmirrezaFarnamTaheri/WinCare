using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands;

/// <summary>Builds a bounded recovery plan from a completed remediation history record.</summary>
public static class RemediationRecoveryPlanner
{
    public const int SchemaVersion = 1;
    public const int MaximumSteps = 100;

    /// <summary>Change kind this planner knows how to compensate.</summary>
    public const string RegistryChangeKind = "SetRegistryValue";

    /// <summary>Status of a remediation whose applied changes can be restored.</summary>
    public const string AppliedStatus = "Applied";

    public static RemediationRecoveryPlan Create(JsonElement history)
    {
        if (history.ValueKind != JsonValueKind.Object) return Failed("Recovery history must be an object.");
        string executionId = ReadString(history, "id");
        if (executionId.Length == 0) return Failed("Recovery history has no execution ID.");
        string status = ReadString(history, "status");
        bool isComplete = string.Equals(status, AppliedStatus, StringComparison.Ordinal);
        bool isInterrupted = status is "Applying" or "PartiallyApplied" or "Cancelled";
        // Older partial receipts omitted the planned change kinds. They cannot prove all
        // side effects are registry-only, so never promise a complete compensation for them.
        if (!isComplete && (!isInterrupted ||
            !string.Equals(ReadString(history, "recoveryScope"), "RegistryOnly", StringComparison.Ordinal)))
            return Failed("Only applied or journaled registry-only remediation can be restored.");

        var evidence = new List<(JsonElement Change, bool PendingUncertain)>();
        if (history.TryGetProperty("changes", out JsonElement changes) && changes.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement change in changes.EnumerateArray())
                evidence.Add((change, false));
        }
        else
        {
            return Failed("Recovery history has no applied changes array.");
        }

        if (history.TryGetProperty("pendingRegistryChange", out JsonElement pending) &&
            pending.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (isComplete || pending.ValueKind != JsonValueKind.Object)
                return Failed("Recovery history has invalid pending change evidence.");
            evidence.Add((pending, true));
        }

        if (evidence.Count is < 1 or > MaximumSteps)
            return Failed($"Recovery history must contain 1 to {MaximumSteps} applied or prepared changes.");

        var steps = new List<RegistryRecoveryStep>();
        foreach ((JsonElement change, bool pendingUncertain) in evidence.AsEnumerable().Reverse())
        {
            if (change.ValueKind != JsonValueKind.Object ||
                !string.Equals(ReadString(change, "Type"), RegistryChangeKind, StringComparison.Ordinal) ||
                !change.TryGetProperty("detail", out JsonElement detail) || detail.ValueKind != JsonValueKind.Object)
                return Failed("This remediation contains a change type without a complete executable compensator.");
            string path = ReadString(detail, "path");
            string name = ReadString(detail, "name");
            string appliedValueType = ReadString(detail, "valueType");
            if (path.Length == 0 || name.Length == 0 || appliedValueType.Length == 0 || !detail.TryGetProperty("value", out JsonElement appliedValue))
                return Failed("A registry change is missing applied-value recovery evidence.");
            // Missing 'previous' is corruption, while explicit JSON null means the
            // value was absent before the write and must be removed during recovery.
            if (!detail.TryGetProperty("previous", out JsonElement previousValue))
                return Failed($"Registry change '{name}' has no previous-value evidence.");
            JsonElement? previous = previousValue.ValueKind == JsonValueKind.Null ? null : previousValue.Clone();
            string? previousKind = detail.TryGetProperty("previousKind", out JsonElement kind) &&
                kind.ValueKind == JsonValueKind.String ? kind.GetString() : null;
            steps.Add(new RegistryRecoveryStep(
                path, name, appliedValue.Clone(), appliedValueType, previous, previousKind, pendingUncertain));
        }
        string digest = ApprovedMutationPlan.ComputeCanonicalDigest(history).ToLowerInvariant();
        return new RemediationRecoveryPlan(SchemaVersion, executionId, digest, steps, []);
    }

    private static RemediationRecoveryPlan Failed(string message) => new(SchemaVersion, string.Empty, string.Empty, [], [message]);
    private static string ReadString(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}

public sealed record RemediationRecoveryPlan(int SchemaVersion, string ExecutionId, string Digest,
    IReadOnlyList<RegistryRecoveryStep> Steps, IReadOnlyList<string> Failures)
{
    public bool IsExecutable => Failures.Count == 0 && Steps.Count > 0;
}

public sealed record RegistryRecoveryStep(string Path, string Name, JsonElement AppliedValue,
    string AppliedValueType, JsonElement? PreviousValue, string? PreviousValueKind, bool PendingUncertain = false);
