using System;
using System.Text.Json;
using WinCare.Domain.Activity;
using WinCare.Domain.Commands;
using WinCare.Domain.Serialization;
using Xunit;

namespace WinCare.Application.Tests.Serialization;

public sealed class DomainJsonSerializationTests
{
    [Fact]
    public void WinCareDomainJsonContext_SerializesAndDeserializesActivityRecord()
    {
        var record = new ActivityRecord(
            Id: Guid.NewGuid(),
            CommandId: "system-care",
            Title: "System Diagnostics",
            State: ActivityState.Completed,
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt: DateTimeOffset.UtcNow,
            Result: "Clean run",
            UndoAvailable: true);

        string json = JsonSerializer.Serialize(record, WinCareDomainJsonContext.Default.ActivityRecord);
        Assert.NotNull(json);
        Assert.Contains("Completed", json);
        Assert.Contains("system-care", json);

        var roundtripped = JsonSerializer.Deserialize(json, WinCareDomainJsonContext.Default.ActivityRecord);
        Assert.NotNull(roundtripped);
        Assert.Equal(record.Id, roundtripped.Id);
        Assert.Equal(record.CommandId, roundtripped.CommandId);
        Assert.Equal(record.Title, roundtripped.Title);
        Assert.Equal(record.State, roundtripped.State);
        Assert.Equal(record.Result, roundtripped.Result);
        Assert.Equal(record.UndoAvailable, roundtripped.UndoAvailable);
    }

    [Fact]
    public void WinCareDomainJsonContext_SerializesAndDeserializesApprovedMutationPlan()
    {
        using var doc = JsonDocument.Parse("{\"target\":\"registry\"}");
        var plan = ApprovedMutationPlan.Create("pagefile-set", doc.RootElement, Guid.NewGuid());

        string json = JsonSerializer.Serialize(plan, WinCareDomainJsonContext.Default.ApprovedMutationPlan);
        Assert.NotNull(json);
        Assert.Contains(plan.PlanId, json);
        Assert.Contains("pagefile-set", json);

        var roundtripped = JsonSerializer.Deserialize(json, WinCareDomainJsonContext.Default.ApprovedMutationPlan);
        Assert.NotNull(roundtripped);
        Assert.Equal(plan.PlanId, roundtripped.PlanId);
        Assert.Equal(plan.CommandId, roundtripped.CommandId);
        Assert.Equal(plan.ParametersDigest, roundtripped.ParametersDigest);
        Assert.Equal(plan.CorrelationId, roundtripped.CorrelationId);
    }

    [Fact]
    public void WinCareDomainJsonContext_SerializesAndDeserializesCommandResult()
    {
        var result = new CommandResult(
            CommandId: "storage",
            CorrelationId: Guid.NewGuid(),
            Status: CommandResultStatus.Succeeded,
            Code: "storage.ok",
            Message: "Storage checked",
            Data: null,
            StartedAt: DateTimeOffset.UtcNow.AddSeconds(-2),
            CompletedAt: DateTimeOffset.UtcNow,
            UndoAvailable: false);

        string json = JsonSerializer.Serialize(result, WinCareDomainJsonContext.Default.CommandResult);
        Assert.NotNull(json);
        Assert.Contains("Succeeded", json);
        Assert.Contains("storage.ok", json);

        var roundtripped = JsonSerializer.Deserialize(json, WinCareDomainJsonContext.Default.CommandResult);
        Assert.NotNull(roundtripped);
        Assert.Equal(result.CommandId, roundtripped.CommandId);
        Assert.Equal(result.Status, roundtripped.Status);
        Assert.Equal(result.Code, roundtripped.Code);
        Assert.Equal(result.Message, roundtripped.Message);
    }

    [Fact]
    public void WinCareDomainJsonContext_SerializesAndDeserializesActivityRecordList()
    {
        var list = new System.Collections.Generic.List<ActivityRecord>
        {
            new(Guid.NewGuid(), "cmd1", "Title 1", ActivityState.Completed, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Done", false),
            new(Guid.NewGuid(), "cmd2", "Title 2", ActivityState.Running, DateTimeOffset.UtcNow, null, "", true),
        };

        string json = JsonSerializer.Serialize(list, WinCareDomainJsonContext.Default.ListActivityRecord);
        Assert.NotNull(json);
        Assert.Contains("cmd1", json);
        Assert.Contains("cmd2", json);

        var roundtripped = JsonSerializer.Deserialize(json, WinCareDomainJsonContext.Default.ListActivityRecord);
        Assert.NotNull(roundtripped);
        Assert.Equal(2, roundtripped.Count);
        Assert.Equal("cmd1", roundtripped[0].CommandId);
        Assert.Equal("cmd2", roundtripped[1].CommandId);
    }
}
