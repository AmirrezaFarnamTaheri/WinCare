using System.Collections.Generic;
using System.Linq;
using WinCare.Application.Tools;
using Xunit;

namespace WinCare.Application.Tests;

/// <summary>
/// Regression tests for built-in category shortcuts resolving to nonempty command sets.
/// </summary>
public sealed class CategoryShortcutTests
{
    /// <summary>
    /// Mirror of the ToolSearchQuery values in SystemCarePageViewModel, SecurityPageViewModel
    /// and RepairRecoveryPageViewModel (WinCare.App is not referenced from test code).
    /// </summary>
    public static TheoryData<string> BuiltInShortcutQueries => new()
    {
        // System care tabs
        "storage cleanup", "performance", "startup applications", "network update", "preset",
        // Security tabs
        "security", "security-control", "privacy", "hardening",
        // Repair & recovery tabs
        "repair", "restore", "reports", "export backup", "recovery reset",
    };

    private readonly ToolCatalogService _service = new();

    [Theory]
    [MemberData(nameof(BuiltInShortcutQueries))]
    public void Every_built_in_category_shortcut_resolves_to_a_nonempty_command_set(string query)
    {
        IReadOnlyList<CommandCatalog.Models.CommandDefinition> results = _service.Search(query);

        Assert.NotEmpty(results);
    }

    [Fact]
    public void Storage_cleanup_shortcut_resolves_the_cleanup_family()
    {
        var ids = _service.Search("storage cleanup").Select(command => command.Id).ToHashSet();

        Assert.Contains("cleanup-targets", ids);
        Assert.Contains("cleaner-disk-pressure", ids);
    }

    [Fact]
    public void Network_update_shortcut_resolves_the_network_and_update_families()
    {
        var ids = _service.Search("network update").Select(command => command.Id).ToHashSet();

        Assert.Contains("network", ids);
        Assert.Contains("wua-search", ids);
    }

    [Fact]
    public void Previously_fabricated_defender_firewall_shortcut_is_gone()
    {
        // The catalog has never had Defender/firewall commands; the Security protection tab
        // must target the security-control family instead.
        Assert.DoesNotContain(_service.Search("defender firewall"), command => command.Id.Contains("defender", StringComparison.Ordinal));
        Assert.Contains(_service.Search("security-control"), command => command.Id == "security-control-reduce");
    }

    [Fact]
    public void Export_backup_shortcut_resolves_export_and_backup_families()
    {
        var ids = _service.Search("export backup").Select(command => command.Id).ToHashSet();

        Assert.Contains("bcd-export", ids);
        Assert.Contains("steam-backup", ids);
    }

    [Fact]
    public void Recovery_reset_shortcut_resolves_recovery_and_reset_families()
    {
        var ids = _service.Search("recovery reset").Select(command => command.Id).ToHashSet();

        Assert.Contains("bcd-export", ids);
        Assert.Contains("deep-clean", ids);
    }
}
