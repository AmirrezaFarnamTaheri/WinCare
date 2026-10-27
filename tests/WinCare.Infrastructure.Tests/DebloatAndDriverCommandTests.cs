using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Commands;

namespace WinCare.Infrastructure.Tests;

public sealed class DebloatAndDriverCommandTests
{
    private static WindowsCommandExecutor CreateExecutor()
    {
        return new WindowsCommandExecutor();
    }

    private static CommandParameters Params(object data)
    {
        return new CommandParameters(JsonSerializer.SerializeToElement(data));
    }

    [Fact]
    public void ShellTweak_rejects_unknown_tweak_option()
    {
        var executor = CreateExecutor();
        var p = Params(new { Tweak = "invalid-tweak" });

        var ex = Assert.Throws<CommandParameterException>(() =>
        {
            var method = typeof(WindowsCommandExecutor).GetMethod(
                "ApplyShellTweak",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            try
            {
                method!.Invoke(executor, [p]);
            }
            catch (System.Reflection.TargetInvocationException tie)
            {
                throw tie.InnerException!;
            }
        });

        Assert.Equal("Tweak", ex.ParameterName);
    }

    // Shell tweak application writes real HKCU/HKLM settings. Run that coverage only
    // in a disposable Windows integration VM with before/after registry assertions.
    // Unit tests must never change the developer or CI runner's shell configuration.

    [Fact]
    public void DriverServiceAudit_returns_success_with_service_list()
    {
        var executor = CreateExecutor();
        var method = typeof(WindowsCommandExecutor).GetMethod(
            "DriverServiceAudit",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var outcome = (CommandHandlerOutcome)method!.Invoke(executor, null)!;

        Assert.Equal(CommandResultStatus.Succeeded, outcome.Status);
        Assert.Equal("driver-service-audit.ok", outcome.Code);
        Assert.NotNull(outcome.Data);
    }

    [Fact]
    public void DriverCacheCleanup_runs_dry_or_bounded_without_throwing()
    {
        var executor = CreateExecutor();
        var p = Params(new { Vendor = "none" });
        var method = typeof(WindowsCommandExecutor).GetMethod(
            "DriverCacheCleanup",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var outcome = (CommandHandlerOutcome)method!.Invoke(executor, [p])!;

        Assert.Equal(CommandResultStatus.Succeeded, outcome.Status);
        Assert.Equal("driver-cache-cleanup.ok", outcome.Code);
    }

    [Fact]
    public void HardwareInventory_includes_driverServices_data()
    {
        var executor = CreateExecutor();
        var method = typeof(WindowsCommandExecutor).GetMethod(
            "HardwareInventory",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var outcome = (CommandHandlerOutcome)method!.Invoke(executor, [CancellationToken.None])!;

        Assert.Equal(CommandResultStatus.Succeeded, outcome.Status);
        Assert.NotNull(outcome.Data);
        Assert.True(outcome.Data.Value.TryGetProperty("driverServices", out JsonElement driverServices));
        Assert.True(driverServices.TryGetProperty("services", out _));
    }

    [Fact]
    public void VbsAssurance_returns_security_posture_data()
    {
        var executor = CreateExecutor();
        var method = typeof(WindowsCommandExecutor).GetMethod(
            "VbsAssurance",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var outcome = (CommandHandlerOutcome)method!.Invoke(executor, null)!;

        Assert.Equal(CommandResultStatus.Succeeded, outcome.Status);
        Assert.NotNull(outcome.Data);
        Assert.True(outcome.Data.Value.TryGetProperty("hypervisorEnforcedCodeIntegrity", out _));
        Assert.True(outcome.Data.Value.TryGetProperty("vulnerableDriverBlocklist", out _));
        Assert.True(outcome.Data.Value.TryGetProperty("smartAppControlState", out _));
    }
}
