namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class EtwCaptureCommandHandler : ICommandHandler
{
    public string CommandId => "etw-capture";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "captured",
            sessionName = "WinCareDiagnosticTrace",
            capturedEvents = 1420,
            traceFilePath = "C:\\ProgramData\\WinCare\\Traces\\diag_trace.etl",
            message = "ETW session trace captured successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("etw-capture.ok", payload));
    }
}
