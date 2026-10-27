using Microsoft.Win32;
using WinCare.Infrastructure.Commands;

namespace WinCare.Infrastructure.Tests;

public sealed class RemediationRecoveryValueComparisonTests
{
    [Fact]
    public void Recovery_rejects_same_text_in_different_registry_value_kinds()
    {
        Assert.False(WindowsCommandExecutor.RecoveryRegistryValueMatches(
            "same", RegistryValueKind.ExpandString, "same", RegistryValueKind.String));
    }

    [Fact]
    public void Recovery_recognizes_an_already_restored_previous_value()
    {
        Assert.True(WindowsCommandExecutor.RecoveryRegistryValueMatches(
            "previous", RegistryValueKind.String, "previous", RegistryValueKind.String));
    }

    [Fact]
    public void Recovery_distinguishes_absent_previous_values_from_present_values()
    {
        Assert.True(WindowsCommandExecutor.RecoveryRegistryValueMatches(null, null, null, null));
        Assert.False(WindowsCommandExecutor.RecoveryRegistryValueMatches(
            0, RegistryValueKind.DWord, null, null));
    }

    [Fact]
    public void Recovery_compares_binary_registry_values_by_content()
    {
        Assert.True(WindowsCommandExecutor.RecoveryRegistryValueMatches(
            new byte[] { 1, 2 }, RegistryValueKind.Binary,
            new byte[] { 1, 2 }, RegistryValueKind.Binary));
        Assert.False(WindowsCommandExecutor.RecoveryRegistryValueMatches(
            new byte[] { 1, 3 }, RegistryValueKind.Binary,
            new byte[] { 1, 2 }, RegistryValueKind.Binary));
    }
}
