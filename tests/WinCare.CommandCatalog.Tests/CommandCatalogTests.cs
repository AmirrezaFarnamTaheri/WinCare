using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.CommandCatalog.Tests;

public sealed class CommandCatalogTests
{
    [Fact]
    public void Load_preserves_all_269_unique_command_ids()
    {
        IReadOnlyList<CommandDefinition> commands = CommandCatalog.Load();

        Assert.Equal(269, commands.Count);
        Assert.Equal(269, commands.Select(command => command.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("system", "System overview")]
    [InlineData("wua-install", "Install Windows updates")]
    [InlineData("legacy-unsafe", "Winhance maximum profile")]
    [InlineData("quic-capability", "QUIC capability")]
    [InlineData("installer-cache-analysis", "Installer cache analysis")]
    [InlineData("storage-report", "Bounded storage report")]
    [InlineData("app-residual-discovery", "App residual candidates")]
    [InlineData("winget-upgrade-inventory", "Available WinGet upgrades")]
    [InlineData("remediation-restore", "Restore a reversible remediation")]
    public void Find_returns_plain_language_metadata(string id, string expectedTitle)
    {
        CommandDefinition command = Assert.IsType<CommandDefinition>(CommandCatalog.Find(id));

        Assert.Equal(expectedTitle, command.Title);
        Assert.False(string.IsNullOrWhiteSpace(command.Summary));
    }

    [Fact]
    public void Read_only_commands_are_explicitly_marked()
    {
        CommandDefinition system = Assert.IsType<CommandDefinition>(CommandCatalog.Find("system"));
        CommandDefinition install = Assert.IsType<CommandDefinition>(CommandCatalog.Find("wua-install"));

        Assert.True(system.ReadOnly);
        Assert.Equal(CommandRisk.ReadOnly, system.Risk);
        Assert.False(install.ReadOnly);
        Assert.NotEqual(CommandRisk.ReadOnly, install.Risk);
    }

    [Theory]
    [InlineData("system", RiskTier.Safe)]
    [InlineData("note-save", RiskTier.Safe)]
    // Cleaner commands declare Moderate tier.
    [InlineData("cleaner-disk-pressure", RiskTier.Moderate)]
    [InlineData("cleaner-winapp2-run", RiskTier.Moderate)]
    [InlineData("pagefile-set", RiskTier.Moderate)]
    [InlineData("wua-install", RiskTier.Moderate)]
    [InlineData("legacy-unsafe", RiskTier.Destructive)]
    [InlineData("deep-clean", RiskTier.Destructive)]
    [InlineData("sysmon-uninstall", RiskTier.Destructive)]
    public void Commands_have_correct_risk_tier(string id, RiskTier expectedTier)
    {
        CommandDefinition command = Assert.IsType<CommandDefinition>(CommandCatalog.Find(id));
        Assert.Equal(expectedTier, command.RiskTier);
    }

    [Fact]
    public void Parameter_schemas_expose_required_package_lists_and_closed_safety_choices()
    {
        CommandParameterDefinition appxPackages = Assert.Single(
            CommandParameterCatalog.For("appx-selection-assess"),
            parameter => parameter.Name == "PackageNames");
        Assert.True(appxPackages.Required);
        Assert.Equal(CommandParameterKind.StringList, appxPackages.Kind);

        Assert.True(Assert.Single(CommandParameterCatalog.For("appx-installed-remove"), parameter => parameter.Name == "PackageNames").Required);
        Assert.True(Assert.Single(CommandParameterCatalog.For("appx-registered-remove"), parameter => parameter.Name == "PackageNames").Required);
        Assert.True(Assert.Single(CommandParameterCatalog.For("appx-provisioned-remove"), parameter => parameter.Name == "PackageNames").Required);
        Assert.Equal(AdministratorAccess.No, CommandCatalog.Find("appx-installed-remove")!.AdministratorAccess);
        Assert.Equal(AdministratorAccess.Required, CommandCatalog.Find("appx-provisioned-remove")!.AdministratorAccess);

        CommandParameterDefinition hardeningProfile = Assert.Single(
            CommandParameterCatalog.For("hardening-apply"),
            parameter => parameter.Name == "ProfileId");
        Assert.Equal(["balanced", "hail-mary"], hardeningProfile.Options);

        CommandParameterDefinition maintenanceState = Assert.Single(
            CommandParameterCatalog.For("maintenance-transition"),
            parameter => parameter.Name == "State");
        Assert.Equal(["Scheduled", "Running", "Completed", "Cancelled"], maintenanceState.Options);

        CommandParameterDefinition consentState = Assert.Single(
            CommandParameterCatalog.For("remote-consent-state"),
            parameter => parameter.Name == "State");
        Assert.Equal(["Active", "Revoked", "Expired"], consentState.Options);

        CommandParameterDefinition networkSetting = Assert.Single(
            CommandParameterCatalog.For("network-experiment"),
            parameter => parameter.Name == "Setting");
        Assert.Equal(["AutoTuningLevel", "EcnCapability", "Timestamps"], networkSetting.Options);

        CommandParameterDefinition legacyAction = Assert.Single(
            CommandParameterCatalog.For("legacy-unsafe"),
            parameter => parameter.Name == "Action");
        Assert.Equal(["flush-dns", "restart-explorer"], legacyAction.Options);

        CommandParameterDefinition storageRoot = Assert.Single(
            CommandParameterCatalog.For("storage-report"),
            parameter => parameter.Name == "RootPath");
        Assert.True(storageRoot.Required);
        Assert.Equal(CommandParameterKind.Text, storageRoot.Kind);
        Assert.Equal("100000", Assert.Single(CommandParameterCatalog.For("storage-report"), parameter => parameter.Name == "MaxEntries").Maximum);
        Assert.Equal("1000", Assert.Single(CommandParameterCatalog.For("app-residual-discovery"), parameter => parameter.Name == "MaxCandidates").Maximum);
    }
}
