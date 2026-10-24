using System.Text.Json;
using WinCare.Domain.Commands;
using WinCare.Application.Commands;
using WinCare.Infrastructure.Plugins;

namespace WinCare.Infrastructure.Tests;

public sealed class PluginScriptCommandHandlerTests
{
    [Theory]
    [InlineData("safe&echo injected")]
    [InlineData("%COMSPEC%")]
    [InlineData("!VARIABLE!")]
    [InlineData("value\r\necho injected")]
    public async Task BatchArgumentsCannotInjectShellSyntax(string argument)
    {
        var root = Path.Combine(Path.GetTempPath(), "wincare-script-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "test.cmd"), "@echo off\r\necho ran>marker.txt\r\n");
            var handler = new PluginScriptCommandHandler("test.script", "test.cmd", root, declaredReadOnly: true);
            var request = new CommandRequest("test.script", JsonSerializer.SerializeToElement(new { value = argument }),
                false, Guid.NewGuid());
            var result = await handler.ExecuteAsync(request, default);
            Assert.Equal("plugin.shell_syntax_rejected", result.Code);
            Assert.False(File.Exists(Path.Combine(root, "marker.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Script_changed_after_registration_is_blocked_before_interpreter_launch()
    {
        string root = Path.Combine(Path.GetTempPath(), "wincare-script-integrity-" + Guid.NewGuid().ToString("N"));
        string pluginDirectory = Path.Combine(root, "plugins", "demo");
        Directory.CreateDirectory(pluginDirectory);
        try
        {
            string script = Path.Combine(pluginDirectory, "run.ps1");
            File.WriteAllText(script, "Write-Output safe");
            string digest = WinCare.Application.Plugins.PluginAdmissionTrustStore.ComputePluginPayloadDigest(pluginDirectory);
            string admissionPath = WinCare.Application.Plugins.PluginAdmissionTrustStore.GetUserScopedRecordPath(pluginDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(admissionPath)!);
            File.WriteAllText(admissionPath, JsonSerializer.Serialize(new WinCare.Application.Plugins.PluginAdmissionRecord
            {
                PluginId = "demo",
                ManifestSha256 = new string('a', 64),
                PackageTreeSha256 = digest,
            }));
            var handler = new PluginScriptCommandHandler("demo.run", "run.ps1", pluginDirectory, declaredReadOnly: true);

            File.WriteAllText(script, "Write-Output tampered");
            CommandHandlerOutcome outcome = await handler.ExecuteAsync(
                CommandRequest.Preview("demo.run", JsonSerializer.SerializeToElement(new { })), default);

            Assert.Equal(CommandResultStatus.Blocked, outcome.Status);
            Assert.Equal("plugin.payload_integrity_failed", outcome.Code);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
