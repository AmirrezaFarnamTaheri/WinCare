using System.Text.Json;
using WinCare.Application.Commands;

namespace WinCare.Application.Tests;

public sealed class RemediationRecoveryPlannerTests
{
    [Fact]
    public void Completed_registry_changes_produce_reverse_order_compensators()
    {
        JsonElement history = JsonSerializer.SerializeToElement(new
        {
            id = "run-1", status = "Applied", changes = new object[]
            {
                new { Type = "SetRegistryValue", detail = new { path = @"HKCU:\A", name = "One", previous = 1, previousKind = "DWord", value = 0, valueType = "DWord" } },
                new { Type = "SetRegistryValue", detail = new { path = @"HKCU:\B", name = "Two", previous = "old", previousKind = "String", value = "new", valueType = "String" } },
            }
        });
        RemediationRecoveryPlan plan = RemediationRecoveryPlanner.Create(history);
        Assert.True(plan.IsExecutable);
        Assert.Equal("run-1", plan.ExecutionId);
        Assert.Equal(["Two", "One"], plan.Steps.Select(step => step.Name));
        Assert.Equal(64, plan.Digest.Length);
    }

    [Fact]
    public void Applied_change_that_created_a_value_restores_by_deleting_it()
    {
        // `previous: null` has a precise meaning on the execution side: the registry value did not
        // exist before the remediation, so the compensator deletes the newly-created value. The
        // apply path always records the property (as null in exactly this case), so the plan built
        // from it must stay executable rather than reporting a valid undo as missing evidence.
        JsonElement history = JsonSerializer.SerializeToElement(new
        {
            id = "run-5", status = "Applied", changes = new object[]
            {
                new { Type = "SetRegistryValue", detail = new { path = @"HKCU:\A", name = "One", previous = (object?)null, previousKind = (string?)null, value = 0, valueType = "DWord" } },
            }
        });
        RemediationRecoveryPlan plan = RemediationRecoveryPlanner.Create(history);
        Assert.True(plan.IsExecutable);
        Assert.Single(plan.Steps);
        Assert.Empty(plan.Failures);
        Assert.Null(plan.Steps[0].PreviousValue);
    }

    [Fact]
    public void Applied_change_without_a_previous_property_fails_closed()
    {
        // The apply path always writes `previous`; a record without the property is corrupt or
        // predates the field, and no compensator can be built from it. It must not be reported as
        // executable, because discovery cannot tell delete-on-undo from missing evidence.
        JsonElement history = JsonSerializer.SerializeToElement(new
        {
            id = "run-6", status = "Applied", changes = new object[]
            {
                new { Type = "SetRegistryValue", detail = new { path = @"HKCU:\A", name = "One", value = 0, valueType = "DWord" } },
            }
        });
        RemediationRecoveryPlan plan = RemediationRecoveryPlanner.Create(history);
        Assert.False(plan.IsExecutable);
        Assert.Empty(plan.Steps);
        Assert.Single(plan.Failures);
    }

    [Fact]
    public void Mixed_or_incomplete_history_fails_closed()
    {
        JsonElement mixed = JsonSerializer.SerializeToElement(new { id = "run-2", status = "Applied", changes = new[] { new { Type = "SetServiceStartMode", detail = new { } } } });
        Assert.False(RemediationRecoveryPlanner.Create(mixed).IsExecutable);
        JsonElement partial = JsonSerializer.SerializeToElement(new { id = "run-3", status = "PartiallyApplied", changes = new[] { new { Type = "SetRegistryValue", detail = new { } } } });
        Assert.False(RemediationRecoveryPlanner.Create(partial).IsExecutable);
    }

    [Fact]
    public void Digest_is_stable_when_receipt_properties_are_reordered()
    {
        using JsonDocument first = JsonDocument.Parse("""{"id":"run-4","status":"Applied","changes":[{"Type":"SetRegistryValue","detail":{"path":"HKCU:\\\\A","name":"One","previous":1,"previousKind":"DWord","value":0,"valueType":"DWord"}}]}""");
        using JsonDocument reordered = JsonDocument.Parse("""{"changes":[{"detail":{"valueType":"DWord","value":0,"previousKind":"DWord","previous":1,"name":"One","path":"HKCU:\\\\A"},"Type":"SetRegistryValue"}],"status":"Applied","id":"run-4"}""");

        Assert.Equal(
            RemediationRecoveryPlanner.Create(first.RootElement).Digest,
            RemediationRecoveryPlanner.Create(reordered.RootElement).Digest);
    }

    [Fact]
    public void Interrupted_remediation_remains_non_executable_until_durable_journal_writes_are_verified()
    {
        // Recording an intent without durably writing the registry preimage before the
        // mutation cannot establish crash recovery. A new scope label is not proof.
        JsonElement interrupted = JsonSerializer.SerializeToElement(new
        {
            id = "crash-between-write-and-receipt", status = "PartiallyApplied",
            recoveryScope = "RegistryOnly",
            changes = new object[]
            {
                new { Type = "SetRegistryValue", detail = new
                    { path = @"HKCU:\A", name = "One", previous = 1, previousKind = "DWord", value = 0, valueType = "DWord" } }
            }
        });
        JsonElement pending = JsonSerializer.SerializeToElement(new
        {
            id = "crash-after-preparation", status = "Applying", recoveryScope = "RegistryOnly",
            changes = Array.Empty<object>(),
            pendingRegistryChange = new { Type = "SetRegistryValue", detail = new
                { path = @"HKCU:\A", name = "Pending", previous = 1, previousKind = "DWord", value = 0, valueType = "DWord" } }
        });

        Assert.False(RemediationRecoveryPlanner.Create(interrupted).IsExecutable);
        Assert.False(RemediationRecoveryPlanner.Create(pending).IsExecutable);
    }
    [Fact]
    public void Interrupted_registry_only_receipt_with_durable_pending_preimage_can_be_reviewed()
    {
        JsonElement record = JsonSerializer.SerializeToElement(new
        {
            id = "journaled-1", status = "Applying", journalSchemaVersion = 2,
            recoveryScope = "RegistryOnly",
            changes = new object[]
            {
                new { Type = "SetRegistryValue", detail = new { path = @"HKCU:\A", name = "First", previous = 1, previousKind = "DWord", value = 0, valueType = "DWord" } },
            },
            pendingRegistryChange = new
            {
                Type = "SetRegistryValue",
                detail = new { path = @"HKCU:\B", name = "Second", previous = (object?)null, previousKind = (string?)null, value = 1, valueType = "DWord" }
            }
        });

        RemediationRecoveryPlan plan = RemediationRecoveryPlanner.Create(record);

        Assert.True(plan.IsExecutable, string.Join(" ", plan.Failures));
        Assert.Equal(["Second", "First"], plan.Steps.Select(step => step.Name));
        Assert.True(plan.Steps[0].PendingUncertain);
        Assert.False(plan.Steps[1].PendingUncertain);
        Assert.Null(plan.Steps[0].PreviousValue);
    }

    [Fact]
    public void Only_journal_version_two_registry_only_incomplete_receipts_are_recoverable()
    {
        JsonElement unversioned = JsonSerializer.SerializeToElement(new
        {
            id = "not-journaled", status = "PartiallyApplied", recoveryScope = "RegistryOnly",
            changes = new[] { new { Type = "SetRegistryValue", detail = new { path = @"HKCU:\A", name = "Value", previous = 1, previousKind = "DWord", value = 0, valueType = "DWord" } } }
        });
        JsonElement mixed = JsonSerializer.SerializeToElement(new
        {
            id = "mixed", status = "Applying", journalSchemaVersion = 2, recoveryScope = "Mixed",
            changes = new[] { new { Type = "SetRegistryValue", detail = new { path = @"HKCU:\A", name = "Value", previous = 1, previousKind = "DWord", value = 0, valueType = "DWord" } } }
        });

        Assert.False(RemediationRecoveryPlanner.Create(unversioned).IsExecutable);
        Assert.False(RemediationRecoveryPlanner.Create(mixed).IsExecutable);
    }

    [Fact]
    public void Non_null_preimage_without_registry_kind_fails_closed()
    {
        JsonElement record = JsonSerializer.SerializeToElement(new
        {
            id = "missing-kind", status = "Applied",
            changes = new[] { new { Type = "SetRegistryValue", detail = new { path = @"HKCU:\A", name = "Value", previous = 1, value = 0, valueType = "DWord" } } }
        });
        Assert.False(RemediationRecoveryPlanner.Create(record).IsExecutable);
    }

}
