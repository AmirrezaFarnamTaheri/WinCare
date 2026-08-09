using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.Application.Commands.Handlers;
using WinCare.Application.Native;
using WinCare.Domain.Commands;

namespace WinCare.Application.Tests;

public sealed class HandlerSafetyTests
{
    [Fact]
    public async Task Disk_cleanup_rejects_sibling_path_that_only_shares_allowed_prefix()
    {
        RecordingNativeCore native = new();
        DiskCleanupCommandHandler handler = new(native);
        string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        string parent = Path.GetDirectoryName(temp) ?? Path.GetPathRoot(temp)!;
        string sibling = Path.Combine(parent, Path.GetFileName(temp) + "Backup");
        CommandRequest request = Request(
            "cleaner-disk-pressure",
            new { targets = new[] { sibling } },
            apply: false);

        CommandHandlerOutcome outcome = await handler.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, outcome.Status);
        Assert.Equal("disk_cleanup.target_denied", outcome.Code);
        Assert.Equal(0, native.DirectorySizeCalls);
    }

    [Fact]
    public async Task Disk_cleanup_fails_closed_when_preview_measurement_fails()
    {
        RecordingNativeCore native = new() { DirectorySizeException = new IOException("unavailable") };
        DiskCleanupCommandHandler handler = new(native);
        CommandRequest request = Request(
            "cleaner-disk-pressure",
            new { targets = new[] { Environment.ExpandEnvironmentVariables("%TEMP%") } },
            apply: false);

        CommandHandlerOutcome outcome = await handler.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(CommandResultStatus.Failed, outcome.Status);
        Assert.Equal("disk_cleanup.measurement_failed", outcome.Code);
        Assert.True(native.DirectorySizeCalls > 0);
    }

    [Fact]
    public async Task Cleanup_targets_handler_never_clears_logs_even_if_called_with_apply()
    {
        LogCleanupCommandHandler handler = new();

        CommandHandlerOutcome outcome = await handler.ExecuteAsync(
            Request("cleanup-targets", new { }, apply: true),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, outcome.Status);
        Assert.Equal("log_cleanup.readonly", outcome.Code);
    }

    private static CommandRequest Request(string commandId, object parameters, bool apply) =>
        new(commandId, JsonSerializer.SerializeToElement(parameters), apply, Guid.NewGuid());

    private sealed class RecordingNativeCore : INativeCoreService
    {
        public int DirectorySizeCalls { get; private set; }
        public Exception? DirectorySizeException { get; init; }

        public uint GetAbiVersion() => 1;

        public Task<string> HashFileAsync(string path, ulong maxBytes, CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);

        public Task<ulong> GetDirectorySizeAsync(string path, CancellationToken cancellationToken)
        {
            DirectorySizeCalls++;
            return DirectorySizeException is null
                ? Task.FromResult(0UL)
                : Task.FromException<ulong>(DirectorySizeException);
        }

        public Task<string> GetSystemInfoJsonAsync(CancellationToken cancellationToken) =>
            Task.FromResult("{}");
    }
}
