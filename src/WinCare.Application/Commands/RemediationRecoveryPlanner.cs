using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands;

/// <summary>
/// Builds bounded compensators from completed registry receipts or from an explicitly
/// journaled interrupted registry-only operation. An unjournaled/partial legacy record
/// cannot establish the previous host state and always fails closed.
/// </summary>
public static class RemediationRecoveryPlanner
{
    public const int SchemaVersion = 1;
    public const int JournalSchemaVersion = 2;
    public const int MaximumSteps = 100;
    public const string RegistryChangeKind = "SetRegistryValue";
    public const string AppliedStatus = "Applied";

    public static RemediationRecoveryPlan Create(JsonElement history)
    {
        if (history.ValueKind != JsonValueKind.Object)
            return Failed("Recovery history must be an object.");

        string executionId = ReadString(history, "id");
        if (executionId.Length == 0)
            return Failed("Recovery history has no execution ID.");

        string status = ReadString(history, "status");
        bool complete = string.Equals(status, AppliedStatus, StringComparison.Ordinal);
        bool interrupted = status is "Applying" or "PartiallyApplied" or "Cancelled";
        bool hasDurableJournal =
            history.TryGetProperty("journalSchemaVersion", out JsonElement version) &&
            version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out int number) &&
            number == JournalSchemaVersion &&
            string.Equals(ReadString(history, "recoveryScope"), "RegistryOnly", StringComparison.Ordinal);

        if (!complete && (!interrupted || !hasDurableJournal))
            return Failed("Only a completed or durably journaled registry-only remediation can be restored.");
        if (!history.TryGetProperty("changes", out JsonElement changes) || changes.ValueKind != JsonValueKind.Array)
            return Failed("Recovery history is missing its committed changes array.");

        var evidence = new List<(JsonElement Change, bool PendingUncertain)>();
        foreach (JsonElement change in changes.EnumerateArray())
            evidence.Add((change, false));

        if (history.TryGetProperty("pendingRegistryChange", out JsonElement pending) &&
            pending.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (complete || !hasDurableJournal || pending.ValueKind != JsonValueKind.Object)
                return Failed("The recovery receipt contains invalid pending mutation evidence.");
            evidence.Add((pending, true));
        }

        if (evidence.Count is < 1 or > MaximumSteps)
            return Failed($"Recovery history must contain 1 to {MaximumSteps} committed or prepared changes.");

        var steps = new List<RegistryRecoveryStep>(evidence.Count);
        foreach ((JsonElement change, bool pendingUncertain) in evidence.AsEnumerable().Reverse())
        {
            if (change.ValueKind != JsonValueKind.Object ||
                !string.Equals(ReadString(change, "Type"), RegistryChangeKind, StringComparison.Ordinal) ||
                !change.TryGetProperty("detail", out JsonElement detail) || detail.ValueKind != JsonValueKind.Object)
                return Failed("This remediation contains a change without a complete registry compensator.");

            string path = ReadString(detail, "path");
            string name = ReadString(detail, "name");
            string appliedValueType = ReadString(detail, "valueType");
            if (path.Length == 0 || name.Length == 0 || appliedValueType.Length == 0 ||
                !detail.TryGetProperty("value", out JsonElement appliedValue))
                return Failed("A registry change is missing applied-value recovery evidence.");
            if (!detail.TryGetProperty("previous", out JsonElement previousValue))
                return Failed($"Registry change '{name}' has no preimage, so it cannot be restored.");

            JsonElement? previous = previousValue.ValueKind == JsonValueKind.Null ? null : previousValue.Clone();
            string? previousKind = detail.TryGetProperty("previousKind", out JsonElement kind) &&
                kind.ValueKind == JsonValueKind.String ? kind.GetString() : null;
            if (previous is not null && string.IsNullOrWhiteSpace(previousKind))
                return Failed($"Registry change '{name}' has no previous registry value kind.");

            steps.Add(new RegistryRecoveryStep(path, name, appliedValue.Clone(), appliedValueType,
                previous, previousKind, pendingUncertain));
        }

        string digest = ApprovedMutationPlan.ComputeCanonicalDigest(history).ToLowerInvariant();
        return new RemediationRecoveryPlan(SchemaVersion, executionId, digest, steps, []);
    }

    private static RemediationRecoveryPlan Failed(string message) =>
        new(SchemaVersion, string.Empty, string.Empty, [], [message]);

    private static string ReadString(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;
}

public sealed record RemediationRecoveryPlan(
    int SchemaVersion, string ExecutionId, string Digest,
    IReadOnlyList<RegistryRecoveryStep> Steps, IReadOnlyList<string> Failures)
{
    public bool IsExecutable => Failures.Count == 0 && Steps.Count > 0;
}

public sealed record RegistryRecoveryStep(
    string Path, string Name, JsonElement AppliedValue, string AppliedValueType,
    JsonElement? PreviousValue, string? PreviousValueKind, bool PendingUncertain = false);
