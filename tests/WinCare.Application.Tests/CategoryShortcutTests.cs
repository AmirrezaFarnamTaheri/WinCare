using WinCare.Application.Tools;
using Xunit;

namespace WinCare.Application.Tests;

/// <summary>
/// Regression coverage for the user-facing care taxonomy. Product tabs must project exact
/// catalog sections instead of fuzzy shortcut queries.
/// </summary>
public sealed class CategoryShortcutTests
{
    private readonly ToolCatalogService _service = new();

    public static TheoryData<string, string, int> CareSections => new()
    {
        { "System care", "Clean up", 11 },
        { "System care", "Performance", 59 },
        { "System care", "Apps & startup", 12 },
        { "System care", "Network & updates", 26 },
        { "System care", "Routines", 8 },
        { "Security", "Status", 20 },
        { "Security", "Protection", 2 },
        { "Security", "Privacy", 2 },
        { "Security", "Hardening", 10 },
        { "Repair & recovery", "Repair", 18 },
        { "Repair & recovery", "Restore", 1 },
        { "Repair & recovery", "Backup", 2 },
        { "Repair & recovery", "Reset & media", 4 },
    };

    [Theory]
    [MemberData(nameof(CareSections))]
    public void Care_taxonomy_projects_only_the_exact_area_and_section(string area, string section, int expectedCount)
    {
        var results = CareAreaProjectionService.Project(_service, new CareAreaSelection(area, section), []);

        Assert.Equal(expectedCount, results.Count);
        Assert.All(results, item =>
        {
            Assert.True(string.Equals(area, item.Command.Area, StringComparison.OrdinalIgnoreCase));
            Assert.True(string.Equals(section, item.Command.Section, StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Routines_and_maintenance_can_be_combined_without_cross_area_leakage()
    {
        var selection = new CareAreaSelection("System care", ["Routines", "Maintenance"]);
        var results = CareAreaProjectionService.Project(_service, selection, []);

        Assert.Equal(9, results.Count);
        Assert.All(results, item => Assert.True(string.Equals("System care", item.Command.Area, StringComparison.OrdinalIgnoreCase)));
        Assert.All(results, item => Assert.Contains(item.Command.Section, selection.Sections));
    }
}
