using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.CommandCatalog;
using WinCare.CommandCatalog.Models;

namespace WinCare.Application.Tests;

public sealed class PortablePlaybookExchangeTests
{
    private static readonly IReadOnlyList<CommandDefinition> Catalog = WinCare.CommandCatalog.CommandCatalog.Load();

    [Fact]
    public void Export_then_import_keeps_catalog_ids_and_requires_a_fresh_preview()
    {
        PortablePlaybookStep step = Step("cleaner-disk-pressure", """{ "OlderThanDays": 14 }""");

        string exported = PortablePlaybookExchange.Export("Storage review", [step], Catalog);
        ImportedPortablePlaybook imported = PortablePlaybookExchange.Import(exported, Catalog);

        Assert.Equal("Storage review", imported.Name);
        Assert.True(imported.RequiresFreshPreview);
        Assert.Single(imported.Steps);
        Assert.Equal("cleaner-disk-pressure", imported.Steps[0].CommandId);
        Assert.False(exported.Contains("Approval", StringComparison.OrdinalIgnoreCase));
        Assert.False(exported.Contains("executionDigest", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Import_rejects_unknown_commands_and_approval_material()
    {
        const string unknownCommand = """{"schemaVersion":1,"name":"Bad","steps":[{"commandId":"powershell","parameters":{}}]}""";
        const string approvalMaterial = """{"schemaVersion":1,"name":"Bad","approval":{"planId":"AMP"},"steps":[{"commandId":"cleaner-disk-pressure","parameters":{"OlderThanDays":7}}]}""";

        Assert.Throws<PortablePlaybookValidationException>(() => PortablePlaybookExchange.Import(unknownCommand, Catalog));
        Assert.Throws<PortablePlaybookValidationException>(() => PortablePlaybookExchange.Import(approvalMaterial, Catalog));
    }

    [Fact]
    public void Import_rejects_undeclared_or_json_parameters_and_policy_denials()
    {
        const string undeclared = """{"schemaVersion":1,"name":"Bad","steps":[{"commandId":"cleaner-disk-pressure","parameters":{"Command":"cmd.exe"}}]}""";
        const string jsonParameter = """{"schemaVersion":1,"name":"Bad","steps":[{"commandId":"playbook","parameters":{"Steps":[]}}]}""";
        PortablePlaybookStep allowedStep = Step("cleaner-disk-pressure", """{ "OlderThanDays": 7 }""");

        Assert.Throws<PortablePlaybookValidationException>(() => PortablePlaybookExchange.Import(undeclared, Catalog));
        Assert.Throws<PortablePlaybookValidationException>(() => PortablePlaybookExchange.Import(jsonParameter, Catalog));
        Assert.Throws<PortablePlaybookValidationException>(() =>
            PortablePlaybookExchange.Export("Denied", [allowedStep], Catalog, command => false));
    }

    private static PortablePlaybookStep Step(string commandId, string parameters)
    {
        using JsonDocument document = JsonDocument.Parse(parameters);
        return new PortablePlaybookStep(commandId, document.RootElement.Clone());
    }
}
