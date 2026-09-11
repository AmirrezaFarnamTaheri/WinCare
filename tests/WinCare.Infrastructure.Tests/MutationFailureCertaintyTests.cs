using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Commands;
using Xunit;

namespace WinCare.Infrastructure.Tests;

/// <summary>
/// Regression tests for F-010: IO/Win32 faults during mutating commands were reported as
/// "failed safely" and access faults as admission blocks, although earlier steps of a
/// multi-step mutation may already have applied. Mutating faults must now return explicit
/// state-unknown outcomes with reconciliation guidance.
/// </summary>
public sealed class MutationFailureCertaintyTests : IDisposable
{
    private readonly string _stateRoot;
    private readonly WindowsCommandExecutor _executor;

    public MutationFailureCertaintyTests()
    {
        _stateRoot = Path.Combine(Path.GetTempPath(), "wincare-f010-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_stateRoot);
        _executor = new WindowsCommandExecutor(_stateRoot);
    }

    public void Dispose()
    {
        _executor.Dispose();
        try { Directory.Delete(_stateRoot, recursive: true); } catch (IOException) { }
    }

    private static CommandDefinition Definition(string id) =>
        WinCare.CommandCatalog.CommandCatalog.Find(id) ?? throw new InvalidOperationException($"Command '{id}' missing from catalog.");

    private async Task<CommandHandlerOutcome> ExecuteAsync(string commandId, object parameters)
    {
        JsonElement parameterElement = JsonSerializer.SerializeToElement(parameters);
        var request = CommandRequest.Execute(commandId, parameterElement);
        return await _executor.ExecuteAsync(Definition(commandId), request, CancellationToken.None);
    }

    [Fact]
    public async Task Mutating_export_fault_reports_state_unknown_and_never_claims_failed_safely()
    {
        // A path segment blocked by an existing file makes the destination directory
        // creation fault mid-flow, after the state read step already succeeded.
        string blockerFile = Path.Combine(_stateRoot, "blocked");
        await File.WriteAllTextAsync(blockerFile, "this is a file, not a directory");
        string impossiblePath = Path.Combine(blockerFile, "sub", "widget-export.json");

        CommandHandlerOutcome outcome = await ExecuteAsync("widget-export", new { Path = impossiblePath });

        Assert.Equal(CommandResultStatus.Failed, outcome.Status);
        Assert.Equal("command.failed_state_unknown", outcome.Code);
        Assert.DoesNotContain("failed safely", outcome.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verify the affected system state", outcome.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(outcome.UndoAvailable, "Rollback must not be claimed unless verified.");
    }

    [Fact]
    public async Task Read_only_fault_keeps_read_only_semantics()
    {
        // A read-only command hitting a locked input file reports the failure without
        // mutation claims and without the admission-block classification.
        string lockedFile = Path.Combine(_stateRoot, "locked-input.txt");
        await File.WriteAllTextAsync(lockedFile, "read-only preview input");
        using FileStream lockStream = new(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None);
        try
        {
            CommandHandlerOutcome outcome = await ExecuteAsync("file-preview", new { Path = lockedFile });

            Assert.Equal(CommandResultStatus.Failed, outcome.Status);
            Assert.DoesNotContain("failed safely", outcome.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("without changing host state", outcome.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            lockStream.Dispose();
        }
    }
}
