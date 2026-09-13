using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Commands;

namespace WinCare.Infrastructure.Tests;

public sealed class AppxSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wincare-appx-" + Guid.NewGuid().ToString("N"));
    private readonly WindowsCommandExecutor _executor;

    public AppxSelectionTests()
    {
        Directory.CreateDirectory(_root);
        _executor = new WindowsCommandExecutor(_root);
    }

    [Fact]
    public async Task Invalid_later_package_id_is_rejected_before_any_package_operation()
    {
        int invocations = 0;
        _executor.AppxProcessRunnerSeam = (_, _, _, _) =>
        {
            invocations++;
            return Task.FromResult(Result(0));
        };

        CommandHandlerOutcome outcome = await ExecuteOfflineAsync(new { ImagePath = _root, PackageNames = new[] { "Microsoft.Valid_1.0", "invalid*wildcard" } });

        Assert.Equal(CommandResultStatus.Blocked, outcome.Status);
        Assert.Equal("command.parameters_invalid", outcome.Code);
        Assert.Equal(0, invocations);
    }

    [Fact]
    public async Task Continues_after_a_package_failure_and_retains_each_package_result()
    {
        Queue<ProcessExecutionResult> outcomes = new([Result(0, "first output"), Result(1603, error: "second error"), Result(3010, "third output")]);
        _executor.AppxProcessRunnerSeam = (_, _, _, _) => Task.FromResult(outcomes.Dequeue());

        CommandHandlerOutcome outcome = await ExecuteOfflineAsync(new { ImagePath = _root, PackageNames = new[] { "Microsoft.First_1.0", "Microsoft.Second_1.0", "Microsoft.Third_1.0" } });

        Assert.Equal(CommandResultStatus.Failed, outcome.Status);
        Assert.Equal("appx-selection.partial_failure", outcome.Code);
        JsonElement receipt = Assert.IsType<JsonElement>(outcome.Data);
        Assert.Equal(2, receipt.GetProperty("succeeded").GetInt32());
        Assert.Equal(1, receipt.GetProperty("failed").GetInt32());
        Assert.True(receipt.GetProperty("restartRequired").GetBoolean());
        Assert.Equal(3, receipt.GetProperty("packages").GetArrayLength());
        Assert.Equal("second error", receipt.GetProperty("packages")[1].GetProperty("standardError").GetString());
        Assert.Contains("All validated package IDs are attempted", receipt.GetProperty("failurePolicy").GetString());
    }

    [Fact]
    public async Task Provisioned_removal_uses_dism_online_and_never_starts_after_invalid_batch_input()
    {
        var calls = new List<(string Executable, IReadOnlyList<string> Arguments)>();
        _executor.AppxProcessRunnerSeam = (executable, arguments, _, _) =>
        {
            calls.Add((executable, arguments));
            return Task.FromResult(Result(0));
        };

        CommandHandlerOutcome outcome = await ExecuteAsync("appx-provisioned-remove", new { PackageNames = new[] { "Microsoft.Valid_1.0", "bad*name" } });

        Assert.Equal(CommandResultStatus.Blocked, outcome.Status);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Provisioned_inventory_returns_exact_dism_package_names_and_retains_output()
    {
        _executor.AppxProcessRunnerSeam = (executable, arguments, _, _) =>
        {
            Assert.Equal("dism.exe", executable);
            Assert.Equal(["/Online", "/Get-ProvisionedAppxPackages", "/English"], arguments);
            return Task.FromResult(Result(0, "PackageName : Microsoft.App_1.0.0.0_neutral__8wekyb3d8bbwe\r\nDisplayName : Microsoft.App"));
        };

        CommandHandlerOutcome outcome = await ExecuteAsync("appx-provisioned-inventory", new { });

        Assert.Equal(CommandResultStatus.Succeeded, outcome.Status);
        JsonElement data = Assert.IsType<JsonElement>(outcome.Data);
        Assert.Equal("OnlineImageProvisioning", data.GetProperty("scope").GetString());
        Assert.Equal("Microsoft.App_1.0.0.0_neutral__8wekyb3d8bbwe", data.GetProperty("packageNames")[0].GetString());
        Assert.Contains("DisplayName", data.GetProperty("standardOutput").GetString());
    }

    [Fact]
    public async Task Registered_removal_is_current_user_only_and_retains_every_package_outcome()
    {
        var attempted = new List<string>();
        _executor.AppxRegisteredRemovalSeam = (package, _) =>
        {
            attempted.Add(package);
            return Task.FromResult(package.StartsWith("bad", StringComparison.Ordinal)
                ? new WindowsCommandExecutor.AppxRegisteredRemovalResult(false, unchecked((int)0x80073CF6), "deployment failure")
                : new WindowsCommandExecutor.AppxRegisteredRemovalResult(true, 0, null));
        };

        CommandHandlerOutcome outcome = await ExecuteAsync("appx-registered-remove", new { PackageNames = new[] { "good.App_1.0_x64__abc", "bad.App_1.0_x64__abc" } });

        Assert.Equal(CommandResultStatus.Failed, outcome.Status);
        Assert.Equal(new[] { "good.App_1.0_x64__abc", "bad.App_1.0_x64__abc" }, attempted);
        JsonElement receipt = Assert.IsType<JsonElement>(outcome.Data);
        Assert.Equal("CurrentUser", receipt.GetProperty("scope").GetString());
        Assert.False(receipt.GetProperty("allUsers").GetBoolean());
        Assert.Equal(2, receipt.GetProperty("packages").GetArrayLength());
    }


    [Fact]
    public async Task Registered_removal_cancellation_stops_before_next_package()
    {
        using var cancellation = new CancellationTokenSource();
        int attempted = 0;
        _executor.AppxRegisteredRemovalSeam = (_, token) =>
        {
            attempted++;
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new WindowsCommandExecutor.AppxRegisteredRemovalResult(true, 0, null));
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ExecuteAsync("appx-registered-remove", new { PackageNames = new[] { "First.App_1_x64__abc", "Second.App_1_x64__abc" } }, cancellation.Token));
        Assert.Equal(1, attempted);
    }

    public void Dispose()
    {
        _executor.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private async Task<CommandHandlerOutcome> ExecuteOfflineAsync(object parameters)
        => await ExecuteAsync("offline-appx-selection", parameters);

    private async Task<CommandHandlerOutcome> ExecuteAsync(string id, object parameters, CancellationToken cancellationToken = default)
    {
        CommandDefinition definition = new(
            id, "Test AppX", "Test", "Repair & recovery", "Repair",
            CommandRisk.Moderate, ReadOnly: id.EndsWith("inventory", StringComparison.Ordinal), AdministratorAccess.No, RestartExpectation.MayBeRequired,
            "test", MigrationStatus.Implemented, []);
        return await _executor.ExecuteAsync(
            definition,
            CommandRequest.Execute(definition.Id, JsonSerializer.SerializeToElement(parameters)),
            cancellationToken);
    }

    private static ProcessExecutionResult Result(int exitCode, string output = "", string error = "") =>
        new(exitCode, output, error, OutputTruncated: false, TimeSpan.FromMilliseconds(1));
}
