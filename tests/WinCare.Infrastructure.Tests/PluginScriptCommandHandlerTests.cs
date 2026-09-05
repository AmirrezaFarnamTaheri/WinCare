using System.Text.Json;
using WinCare.Domain.Commands;
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
}
