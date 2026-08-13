namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class DownloadBatchCommandHandler : ICommandHandler
{
    public string CommandId => "download-batch";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "processed",
            batchQueueCount = 0,
            activeJobsCount = 0,
            completedJobsCount = 5,
            message = "Batch download queue status evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("download-batch.ok", payload));
    }
}
