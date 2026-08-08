using WinCare.CommandCatalog.Models;

namespace WinCare.CommandCatalog.Tests;

public sealed class CommandCatalogTests
{
    [Fact]
    public void Load_preserves_all_259_unique_command_ids()
    {
        IReadOnlyList<CommandDefinition> commands = CommandCatalog.Load();

        Assert.Equal(259, commands.Count);
        Assert.Equal(259, commands.Select(command => command.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("system", "System overview")]
    [InlineData("wua-install", "Install Windows updates")]
    [InlineData("legacy-unsafe", "Winhance maximum profile")]
    [InlineData("quic-capability", "QUIC capability")]
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
}
