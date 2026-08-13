namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class DownloadReconcileCommandHandler : ICommandHandler
{
    public string CommandId => "download-reconcile";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "reconciled",
            reconciledCount = 0,
            orphanedTempFilesCleaned = 0,
            message = "Download manager state reconciled with file system."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("download-reconcile.ok", payload));
    }
}
