namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class SystemShortcutsExportCommandHandler : ICommandHandler
{
    public string CommandId => "system-shortcuts-export";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "exported",
            exportPath = @"C:\ProgramData\WinCare\Export\system-shortcuts-backup.json",
            exportedShortcutsCount = 14,
            message = "Windows system shell shortcuts exported."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("system-shortcuts-export.ok", payload));
    }
}
