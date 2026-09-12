using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Commands;
using Catalog = WinCare.CommandCatalog.CommandCatalog;

namespace WinCare.Infrastructure.Tests;

public sealed class DiscoveryCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wincare-discovery-" + Guid.NewGuid().ToString("N"));
    private readonly WindowsCommandExecutor _executor;

    public DiscoveryCommandTests()
    {
        Directory.CreateDirectory(_root);
        _executor = new WindowsCommandExecutor(_root);
    }

    [Fact]
    public async Task Storage_report_returns_bounded_typed_evidence_without_changing_the_directory()
    {
        string file = Path.Combine(_root, "large.bin");
        await File.WriteAllBytesAsync(file, new byte[128]);

        CommandHandlerOutcome outcome = await ExecuteAsync("storage-report", new { RootPath = _root, MaxEntries = 10, MaxDepth = 2, LargestFileCount = 3 });

        Assert.Equal(CommandResultStatus.Succeeded, outcome.Status);
        JsonElement data = Assert.IsType<JsonElement>(outcome.Data);
        Assert.Equal(Path.GetFullPath(_root), data.GetProperty("RootPath").GetString());
        Assert.Equal(128UL, data.GetProperty("AccountedBytes").GetUInt64());
        Assert.Equal(file, data.GetProperty("LargestFiles")[0].GetProperty("Path").GetString());
        Assert.True(File.Exists(file));
        Assert.Contains("Read-only", data.GetProperty("safety").GetString());
    }

    [Fact]
    public async Task Storage_report_rejects_a_missing_root_before_any_scan()
    {
        CommandHandlerOutcome outcome = await ExecuteAsync("storage-report", new { RootPath = Path.Combine(_root, "missing") });

        Assert.Equal(CommandResultStatus.Blocked, outcome.Status);
        Assert.Equal("command.parameters_invalid", outcome.Code);
    }

    [Fact]
    public async Task Winget_upgrade_inventory_uses_structured_read_only_arguments_and_retains_payload()
    {
        _executor.ExecutableFinderSeam = name => name == "winget.exe" ? "winget.exe" : null;
        _executor.AppxProcessRunnerSeam = (executable, arguments, _, _) =>
        {
            Assert.Equal("winget.exe", executable);
            Assert.Equal(["upgrade", "--output", "json", "--accept-source-agreements", "--disable-interactivity"], arguments);
            return Task.FromResult(new ProcessExecutionResult(0, "{\"Sources\":[]}", string.Empty, false, TimeSpan.Zero));
        };

        CommandHandlerOutcome outcome = await ExecuteAsync("winget-upgrade-inventory", new { });

        Assert.Equal(CommandResultStatus.Succeeded, outcome.Status);
        JsonElement data = Assert.IsType<JsonElement>(outcome.Data);
        Assert.Equal("WinGet", data.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Array, data.GetProperty("inventory").GetProperty("Sources").ValueKind);
        Assert.Contains("does not install", data.GetProperty("safety").GetString());
    }

    [Fact]
    public async Task Winget_upgrade_inventory_reports_unparseable_success_output_without_claiming_an_upgrade()
    {
        _executor.ExecutableFinderSeam = _ => "winget.exe";
        _executor.AppxProcessRunnerSeam = (_, _, _, _) =>
            Task.FromResult(new ProcessExecutionResult(0, "not json", string.Empty, false, TimeSpan.Zero));

        CommandHandlerOutcome outcome = await ExecuteAsync("winget-upgrade-inventory", new { });

        Assert.Equal(CommandResultStatus.Failed, outcome.Status);
        Assert.Equal("winget-upgrade-inventory.invalid_output", outcome.Code);
        Assert.Contains("No package was changed", outcome.Message);
    }

    public void Dispose()
    {
        _executor.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private Task<CommandHandlerOutcome> ExecuteAsync(string id, object parameters) =>
        _executor.ExecuteAsync(
            Catalog.Find(id)!,
            CommandRequest.Preview(id, JsonSerializer.SerializeToElement(parameters)),
            CancellationToken.None);
}
