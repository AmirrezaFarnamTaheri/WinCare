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

    [Theory]
    [InlineData("end-task", true)]
    [InlineData("last-active-click", false)]
    [InlineData("show-file-extensions", true)]
    [InlineData("show-hidden-files", false)]
    [InlineData("launch-to-this-pc", true)]
    [InlineData("hide-search-box", true)]
    [InlineData("background-apps", true)]
    [InlineData("keyboard-latency", true)]
    public void ShellTweak_applies_hkcu_tweaks_successfully(string tweak, bool enable)
    {
        var executor = CreateExecutor();
        var p = Params(new { Tweak = tweak, Enable = enable });
        var method = typeof(WindowsCommandExecutor).GetMethod(
            "ApplyShellTweak",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var outcome = (CommandHandlerOutcome)method!.Invoke(executor, [p])!;

        Assert.Equal(CommandResultStatus.Succeeded, outcome.Status);
        Assert.Equal("tweak-shell.ok", outcome.Code);
    }

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

    [Fact]
    public void ShellTweak_copilot_key_remap_handles_execution()
    {
        var executor = CreateExecutor();
        var p = Params(new { Tweak = "copilot-key-remap", Enable = false });
        var method = typeof(WindowsCommandExecutor).GetMethod(
            "ApplyShellTweak",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        try
        {
            var outcome = (CommandHandlerOutcome)method!.Invoke(executor, [p])!;
            Assert.Equal(CommandResultStatus.Succeeded, outcome.Status);
            Assert.Equal("tweak-shell.ok", outcome.Code);
        }
        catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is UnauthorizedAccessException)
        {
            // Non-elevated test runner; permissions correctly guarded by OS
        }
    }

    [Fact]
    public void AiTweak_recall_handles_execution()
    {
        var executor = CreateExecutor();
        var p = Params(new { Component = "recall" });
        var method = typeof(WindowsCommandExecutor).GetMethod(
            "ApplyAiTweak",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        try
        {
            var outcome = (CommandHandlerOutcome)method!.Invoke(executor, [p, false])!;
            Assert.Equal(CommandResultStatus.Succeeded, outcome.Status);
            Assert.Equal("tweak-ai.ok", outcome.Code);
        }
        catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is UnauthorizedAccessException)
        {
            // Non-elevated test runner; permissions correctly guarded by OS
        }
    }

    [Theory]
    [InlineData("setup-hardware-bypass")]
    [InlineData("oobe-network-bypass")]
    public void ShellTweak_hardware_and_oobe_bypass_handles_execution(string tweak)
    {
        var executor = CreateExecutor();
        var p = Params(new { Tweak = tweak, Enable = false });
        var method = typeof(WindowsCommandExecutor).GetMethod(
            "ApplyShellTweak",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        try
        {
            var outcome = (CommandHandlerOutcome)method!.Invoke(executor, [p])!;
            Assert.Equal(CommandResultStatus.Succeeded, outcome.Status);
            Assert.Equal("tweak-shell.ok", outcome.Code);
        }
        catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is UnauthorizedAccessException)
        {
            // Non-elevated test runner; permissions correctly guarded by OS
        }
    }
}