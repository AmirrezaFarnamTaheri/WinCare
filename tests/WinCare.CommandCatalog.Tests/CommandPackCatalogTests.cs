using System.Text.Json;
using WinCare.CommandCatalog;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.CommandCatalog.Tests;

/// <summary>
/// Catalog merging and fragment loading for the extension-pack mechanism.
/// </summary>
public class CommandPackCatalogTests
{
    private const string CoreId = "cleanup-targets";

    private static CommandDefinition MakeCommand(string id, string title = "Pack command") =>
        new(id, title, "Summary", "All tools", "Commands", CommandRisk.Low,
            ReadOnly: true, AdministratorAccess.No, RestartExpectation.No,
            LegacySource: "pack", MigrationStatus.Implemented,
            Keywords: Array.Empty<string>(), ExplicitRiskTier: null);

    private static CommandPackFragment MakeFragment(string packId, params string[] ids) =>
        new(packId, Array.AsReadOnly(ids.Select(id => MakeCommand(id)).ToArray()));

    [Fact]
    public void LoadWithPacks_Null_ReturnsTheCoreCatalog()
    {
        IReadOnlyList<CommandDefinition> merged = CommandCatalog.LoadWithPacks(null);

        Assert.Equal(CommandCatalog.Load().Count, merged.Count);
        Assert.Equal(CommandCatalog.Load(), merged);
    }

    [Fact]
    public void LoadWithPacks_Empty_ReturnsTheCoreCatalog()
    {
        IReadOnlyList<CommandDefinition> merged = CommandCatalog.LoadWithPacks(Array.Empty<CommandPackFragment>());

        Assert.Equal(CommandCatalog.Load().Count, merged.Count);
    }

    [Fact]
    public void LoadWithPacks_AppendsFragmentCommandsAfterTheCore()
    {
        CommandPackFragment pack = MakeFragment("demo", "demo-one", "demo-two");

        IReadOnlyList<CommandDefinition> merged = CommandCatalog.LoadWithPacks(new[] { pack });
        IReadOnlyList<CommandDefinition> core = CommandCatalog.Load();

        Assert.Equal(core.Count + 2, merged.Count);
        Assert.True(merged.Take(core.Count).SequenceEqual(core));
        Assert.Equal("demo-one", merged[core.Count].Id);
        Assert.Equal("demo-two", merged[core.Count + 1].Id);
    }

    [Fact]
    public void LoadWithPacks_RejectsACoreIdReDeclaration()
    {
        CommandPackFragment pack = MakeFragment("demo", CoreId);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CommandCatalog.LoadWithPacks(new[] { pack }));

        Assert.Contains(CoreId, ex.Message, StringComparison.Ordinal);
        Assert.Contains("already owned", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadWithPacks_RejectsAnIdDeclaredByTwoPacks()
    {
        CommandPackFragment first = MakeFragment("first", "shared-id");
        CommandPackFragment second = MakeFragment("second", "shared-id");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CommandCatalog.LoadWithPacks(new[] { first, second }));

        Assert.Contains("shared-id", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadWithPacks_RejectsAPackWithNoId()
    {
        CommandPackFragment pack = new("   ", Array.Empty<CommandDefinition>());

        Assert.Throws<ArgumentException>(() => CommandCatalog.LoadWithPacks(new[] { pack }));
    }

    [Fact]
    public void FindIn_UsesPackFragmentDefinitions()
    {
        CommandPackFragment pack = MakeFragment("demo", "demo-one");

        CommandDefinition? found = CommandCatalog.FindIn(new[] { pack }, "demo-one");
        CommandDefinition? missing = CommandCatalog.FindIn(new[] { pack }, "demo-two");
        CommandDefinition? core = CommandCatalog.FindIn(new[] { pack }, CoreId);

        Assert.NotNull(found);
        Assert.Equal("demo-one", found!.Id);
        Assert.Null(missing);
        Assert.NotNull(core);
    }

    [Fact]
    public void FindIn_FallsBackToTheCoreCatalogWithoutPacks()
    {
        CommandDefinition? core = CommandCatalog.FindIn(null, CoreId);
        CommandDefinition? absent = CommandCatalog.FindIn(null, "no-such-command");

        Assert.NotNull(core);
        Assert.Equal(CoreId, core!.Id);
        Assert.Null(absent);
    }

    [Fact]
    public void Load_StillReturnsTheFrozenCoreCatalog()
    {
        IReadOnlyList<CommandDefinition> core = CommandCatalog.Load();

        Assert.Equal(CommandCatalog.Load().Count, core.Count);
        Assert.Contains(core, command => command.Id == CoreId);
    }

    [Fact]
    public void EmbeddedPackFragment_RoundTripsThroughTheFragmentLoader()
    {
        CommandPackFragment fragment = CommandPackCatalog.Load(
            typeof(CommandPackCatalogTests).Assembly, "testpack");

        Assert.Equal("testpack", fragment.PackId);
        Assert.Equal(2, fragment.Commands.Count);
        Assert.Equal("testpack-one", fragment.Commands[0].Id);
        Assert.Equal("testpack-two", fragment.Commands[1].Id);
        Assert.All(fragment.Commands, command => Assert.Equal("All tools", command.Area));
    }

    [Fact]
    public async Task EmbeddedPackFragment_ProducesExecutableCommandDefinitions()
    {
        // Fragment commands are plain CommandDefinitions on the public surface, so the derived
        // admission tier and metadata every consumer reads off a core command work identically.
        CommandPackFragment fragment = CommandPackCatalog.Load(
            typeof(CommandPackCatalogTests).Assembly, "testpack");

        CommandDefinition first = fragment.Commands[0];

        Assert.Equal("testpack-one", first.Id);
        Assert.True(first.ReadOnly);
        Assert.Equal(MigrationStatus.Implemented, first.MigrationStatus);
        Assert.Equal(RiskTier.Safe, first.RiskTier);

        await Task.CompletedTask;
    }

    [Fact]
    public void Load_ThrowsWhenNoFragmentMatchesThePackId()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CommandPackCatalog.Load(typeof(CommandPackCatalogTests).Assembly, "no-such-pack"));

        Assert.Contains("no-such-pack", ex.Message, StringComparison.Ordinal);
        Assert.Contains("no embedded catalog fragment", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ThrowsWhenTheDeclaredCountDoesNotMatchTheList()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CommandPackCatalog.Load(typeof(CommandPackCatalogTests).Assembly, "mismatch"));

        Assert.Contains("declares 4 commands but lists 2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ThrowsWhenASchemaVersionIsUnsupported()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CommandPackCatalog.Load(typeof(CommandPackCatalogTests).Assembly, "unschema"));

        Assert.Contains("unsupported catalog schema 7", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ThrowsWhenACommandLacksPlainLanguageMetadata()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CommandPackCatalog.Load(typeof(CommandPackCatalogTests).Assembly, "nometa"));

        Assert.Contains("missing an ID or plain-language metadata", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ThrowsWhenTheFragmentReDeclaresItsOwnCommand()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CommandPackCatalog.Load(typeof(CommandPackCatalogTests).Assembly, "duplicate"));

        Assert.Contains("re-declares its own command", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ResolvesPacksWhoseIdsDifferOnlyBeforeTheSuffix()
    {
        // The naming convention is an exact-tail match, so two fragments whose ids differ only in a
        // dotted prefix are distinct packs rather than an ambiguous pair.
        CommandPackFragment plain = CommandPackCatalog.Load(typeof(CommandPackCatalogTests).Assembly, "ambiguous");
        CommandPackFragment alt = CommandPackCatalog.Load(typeof(CommandPackCatalogTests).Assembly, "ambiguous.alt");

        Assert.Equal("ambiguous", plain.PackId);
        Assert.Equal("ambiguous.alt", alt.PackId);
        Assert.Equal("ambiguous-a", plain.Commands[0].Id);
        Assert.Equal("ambiguous-b", alt.Commands[0].Id);
    }

    [Fact]
    public void FragmentResourceSuffix_MatchesTheDocumentedNamingConvention()
    {
        Assert.Equal("commands.", CommandPackCatalog.FragmentResourceSuffix);
        Assert.Equal(8 * 1024 * 1024, CommandPackCatalog.MaximumFragmentBytes);
    }

    [Fact]
    public void PackTestFragment_IsDisjointFromTheCoreCatalog()
    {
        // The composition invariants only hold when test fixtures never collide with the real
        // catalog: a pack re-declaring a core id is a composition error, not a shadowing feature.
        CommandPackFragment fragment = CommandPackCatalog.Load(
            typeof(CommandPackCatalogTests).Assembly, "testpack");

        HashSet<string> coreIds = new(CommandCatalog.Load().Select(command => command.Id), StringComparer.Ordinal);
        Assert.DoesNotContain(fragment.Commands, command => coreIds.Contains(command.Id));
    }
}
