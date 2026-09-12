using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands;

/// <summary>Builds a bounded recovery plan from a completed remediation history record.</summary>
public static class RemediationRecoveryPlanner
{
    public const int SchemaVersion = 1;
    public const int MaximumSteps = 100;

    public static RemediationRecoveryPlan Create(JsonElement history)
    {
        if (history.ValueKind != JsonValueKind.Object) return Failed("Recovery history must be an object.");
        string executionId = ReadString(history, "id");
        if (executionId.Length == 0) return Failed("Recovery history has no execution ID.");
        if (!string.Equals(ReadString(history, "status"), "Applied", StringComparison.Ordinal))
            return Failed("Only a completed applied remediation can be restored.");
        if (!history.TryGetProperty("changes", out JsonElement changes) || changes.ValueKind != JsonValueKind.Array || changes.GetArrayLength() is < 1 or > MaximumSteps)
            return Failed($"Recovery history must contain 1 to {MaximumSteps} applied changes.");

        var steps = new List<RegistryRecoveryStep>();
        foreach (JsonElement change in changes.EnumerateArray().Reverse())
        {
            if (!string.Equals(ReadString(change, "Type"), "SetRegistryValue", StringComparison.Ordinal) ||
                !change.TryGetProperty("detail", out JsonElement detail) || detail.ValueKind != JsonValueKind.Object)
                return Failed("This remediation contains a change type without a complete executable compensator.");
            string path = ReadString(detail, "path");
            string name = ReadString(detail, "name");
            string appliedValueType = ReadString(detail, "valueType");
            if (path.Length == 0 || name.Length == 0 || appliedValueType.Length == 0 || !detail.TryGetProperty("value", out JsonElement appliedValue))
                return Failed("An applied registry change is missing recovery evidence.");
            JsonElement? previous = detail.TryGetProperty("previous", out JsonElement previousValue) ? previousValue.Clone() : null;
            string? previousKind = detail.TryGetProperty("previousKind", out JsonElement kind) && kind.ValueKind == JsonValueKind.String ? kind.GetString() : null;
            steps.Add(new RegistryRecoveryStep(path, name, appliedValue.Clone(), appliedValueType, previous, previousKind));
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
    string AppliedValueType, JsonElement? PreviousValue, string? PreviousValueKind);
